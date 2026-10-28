using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DynamicIsland.Services.Remote;

public sealed record RemoteRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, byte[] Body, IPAddress Peer)
{
    /// <summary>Paramètres de l'adresse (« ?q=spotify »), déjà décodés.</summary>
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();

    public string? Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;

    public string? Param(string name) => Query.TryGetValue(name, out var value) ? value : null;
}

public sealed record RemoteResponse(int Status, string ContentType, byte[] Body)
{
    /// <summary>Fichier qui ne change pas (police, icône) : le téléphone le garde en cache.</summary>
    public bool Cacheable { get; init; }
    public string? Security { get; init; }

    public static RemoteResponse Json(int status, string json) => new(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
    public static RemoteResponse Empty(int status) => new(status, "text/plain; charset=utf-8", []);
}

/// <summary>
/// Tout petit serveur HTTP/1.1 (6D) pour la page de la télécommande : `TcpListener` sur les adresses données (pas de
/// HttpListener, qui demanderait les droits administrateur pour écouter sur le réseau), une requête par connexion,
/// en-têtes et corps limités (16 Ko ; 4 Mo pour un morceau de fichier envoyé), 10 s au plus par requête (2 min pour un
/// morceau de fichier), 16 connexions à la fois, réseau local seulement. La connexion reste ouverte entre deux requêtes
/// (« keep-alive », 15 s, 500 requêtes) : le pavé tactile du téléphone envoie des dizaines de petits mouvements par seconde.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    public const string UploadPath = "/api/upload";
    public const int MaxUploadChunk = 4 * 1024 * 1024;
    private const int MaxHeaderBytes = 8 * 1024;
    private const int MaxBodyBytes = 16 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    private const int MaxRequestsPerConnection = 500;

    private readonly Func<RemoteRequest, Task<RemoteResponse>> _handler;
    private readonly List<TcpListener> _listeners = [];
    private readonly SemaphoreSlim _slots = new(16);
    private CancellationTokenSource? _stop;

    public RemoteServer(Func<RemoteRequest, Task<RemoteResponse>> handler) => _handler = handler;

    public IReadOnlyList<IPEndPoint> Endpoints => _listeners.Select(l => (IPEndPoint)l.LocalEndpoint).ToList();

    /// <summary>Écoute sur chaque adresse ; une adresse déjà prise lève une SocketException (après avoir tout refermé).</summary>
    public void Start(IEnumerable<IPAddress> addresses, int port)
    {
        Stop();
        _stop = new CancellationTokenSource();
        try
        {
            foreach (var address in addresses)
            {
                var listener = new TcpListener(address, port);
                listener.Server.ExclusiveAddressUse = true;
                listener.Start(16);
                _listeners.Add(listener);
                _ = AcceptLoopAsync(listener, _stop.Token);
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
        foreach (var listener in _listeners) listener.Stop();
        _listeners.Clear();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            _ = ServeAsync(client, token);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stopping)
    {
        using var _ = client;
        if (!await _slots.WaitAsync(0, CancellationToken.None)) return; // trop de connexions : on ferme celle-ci
        try
        {
            var peer = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            client.NoDelay = true; // petits mouvements du pavé tactile : envoyés tout de suite
            var stream = client.GetStream();
            for (int served = 0; served < MaxRequestsPerConnection; served++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(served == 0 ? RequestTimeout : IdleTimeout);
                if (!LocalNetwork.IsLocalPeer(peer))
                {
                    await WriteAsync(stream, RemoteResponse.Empty(403), keepAlive: false, timeout.Token);
                    return;
                }
                var (request, closed) = await ReadAsync(stream, peer, timeout);
                if (request == null)
                {
                    // Connexion refermée par le téléphone entre deux requêtes (normal), ou requête illisible.
                    if (!closed) await WriteAsync(stream, RemoteResponse.Empty(400), keepAlive: false, timeout.Token);
                    return;
                }
                timeout.CancelAfter(request.Path == UploadPath ? UploadTimeout : RequestTimeout);
                var response = await _handler(request);
                bool keepAlive = served + 1 < MaxRequestsPerConnection
                                 && !string.Equals(request.Header("connection"), "close", StringComparison.OrdinalIgnoreCase);
                await WriteAsync(stream, response, keepAlive, timeout.Token);
                if (!keepAlive) return;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // téléphone parti, délai dépassé, serveur arrêté : rien à signaler
        }
        catch (Exception ex)
        {
            Log.Error("Télécommande : requête", ex);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Une requête, ou null : <c>Closed</c> = le téléphone a refermé la connexion avant d'envoyer quoi que ce soit.</summary>
    internal static async Task<(RemoteRequest? Request, bool Closed)> ReadAsync(Stream stream, IPAddress peer, CancellationTokenSource timeout)
    {
        var token = timeout.Token;
        var head = new byte[MaxHeaderBytes];
        int length = 0, headerEnd = -1;
        while (headerEnd < 0)
        {
            if (length >= MaxHeaderBytes) return (null, false);
            int read = await stream.ReadAsync(head.AsMemory(length, MaxHeaderBytes - length), token);
            if (read == 0) return (null, length == 0);
            length += read;
            headerEnd = head.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }

        var lines = Encoding.ASCII.GetString(head, 0, headerEnd).Split("\r\n");
        var start = lines[0].Split(' ');
        if (start.Length != 3 || !start[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return (null, false);
        var headers = new Dictionary<string, string>();
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) return (null, false);
            headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }

        var target = start[1];
        int mark = target.IndexOf('?');
        var path = mark >= 0 ? target[..mark] : target;
        var query = mark >= 0 ? ParseQuery(target[(mark + 1)..]) : new Dictionary<string, string>();

        bool upload = path == UploadPath;
        int maxBody = upload ? MaxUploadChunk : MaxBodyBytes;
        int bodyLength = 0;
        if (headers.TryGetValue("content-length", out var declared) && (!int.TryParse(declared, out bodyLength) || bodyLength < 0 || bodyLength > maxBody))
            return (null, false);
        if (headers.ContainsKey("transfer-encoding")) return (null, false); // pas de corps « chunked » : la page n'en envoie jamais
        if (upload) timeout.CancelAfter(UploadTimeout);

        var body = new byte[bodyLength];
        int already = Math.Min(length - (headerEnd + 4), bodyLength);
        Array.Copy(head, headerEnd + 4, body, 0, already);
        while (already < bodyLength)
        {
            int read = await stream.ReadAsync(body.AsMemory(already, bodyLength - already), token);
            if (read == 0) return (null, false);
            already += read;
        }
        return (new RemoteRequest(start[0].ToUpperInvariant(), path, headers, body, peer) { Query = query }, false);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equal = pair.IndexOf('=');
            var name = Uri.UnescapeDataString((equal >= 0 ? pair[..equal] : pair).Replace('+', ' '));
            var value = equal >= 0 ? Uri.UnescapeDataString(pair[(equal + 1)..].Replace('+', ' ')) : "";
            result[name] = value;
        }
        return result;
    }

    private static async Task WriteAsync(Stream stream, RemoteResponse response, bool keepAlive, CancellationToken token)
    {
        var head = new StringBuilder();
        head.Append($"HTTP/1.1 {response.Status} {Reason(response.Status)}\r\n");
        head.Append($"Content-Type: {response.ContentType}\r\n");
        head.Append($"Content-Length: {response.Body.Length}\r\n");
        head.Append(response.Cacheable ? "Cache-Control: max-age=604800\r\n" : "Cache-Control: no-store\r\n");
        head.Append("X-Content-Type-Options: nosniff\r\n");
        head.Append("X-Frame-Options: DENY\r\n");
        head.Append("Referrer-Policy: no-referrer\r\n");
        if (response.Security != null) head.Append($"Content-Security-Policy: {response.Security}\r\n");
        head.Append(keepAlive ? $"Connection: keep-alive\r\nKeep-Alive: timeout={(int)IdleTimeout.TotalSeconds}\r\n\r\n" : "Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token);
        if (response.Body.Length > 0) await stream.WriteAsync(response.Body, token);
        await stream.FlushAsync(token);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        413 => "Payload Too Large",
        421 => "Misdirected Request",
        429 => "Too Many Requests",
        507 => "Insufficient Storage",
        _ => "Error",
    };
}
