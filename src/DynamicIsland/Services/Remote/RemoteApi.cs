using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Les pages et l'API de la télécommande (6D), appelées sur le thread de l'interface :
/// <list type="bullet">
/// <item>appairage : code à 6 chiffres valable 5 minutes, ouvert depuis les paramètres, à usage unique, 5 essais au plus ;</item>
/// <item>chaque téléphone reçoit un jeton aléatoire (gardé par le téléphone) ; le PC n'en garde que l'empreinte SHA-256 ;</item>
/// <item>toutes les commandes demandent ce jeton (en-tête Authorization, donc pas de requête forcée par un autre site) ;</item>
/// <item>l'adresse demandée doit être une adresse IP (protège du « DNS rebinding ») ;</item>
/// <item>veille, redémarrage, arrêt et presse-papiers du PC : seulement pour un téléphone en « Contrôle complet » (6D-1b).</item>
/// </list>
/// </summary>
public sealed class RemoteApi
{
    public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 5;
    private const string Security = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src 'self'; " +
                                    "img-src 'self' blob: data:; connect-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'";

    /// <summary>Textes de la page du téléphone (clés « remote.page.* »).</summary>
    private static readonly string[] PageKeys =
    [
        "title", "pair.title", "pair.text", "pair.code", "pair.button", "pair.wrong", "pair.closed", "pair.full", "pair.done",
        "offline", "nothing", "volume", "mute", "mic", "mic.off", "lock", "lock.confirm", "profiles", "forget", "forget.confirm", "forgotten",
        "tab.play", "tab.apps", "tab.pc", "mic.discord", "deaf", "deaf.on", "deaf.discord", "denied", "copy", "copied",
        "apps.pinned", "apps.search", "apps.none", "apps.loading", "apps.launched",
        "link.title", "link.placeholder", "link.button", "link.bad", "link.done",
        "send.title", "send.text", "send.button", "send.progress", "send.done", "send.error", "send.space", "send.size",
        "text.title", "text.placeholder", "text.button", "text.done", "text.get", "text.busy",
        "brightness", "dark", "wallpaper", "gaming", "gaming.active", "dock", "dock.off", "timer", "timer.stop", "timer.left",
        "message.title", "message.placeholder", "message.button", "message.done",
        "power.title", "screenoff", "sleep", "restart", "shutdown", "power.full", "power.sent",
        "wake.title", "wake.text", "wake.mac", "wake.broadcast", "wake.port", "wake.ready", "wake.notready",
        "tab.screen", "screen.locked.title", "screen.locked", "screen.gaming", "preview.title", "preview.live", "preview.stop", "preview.screen", "preview.hint", "pad.title", "pad.hint", "pad.left", "pad.right",
        "keys.title", "keys.placeholder", "keys.escape", "keys.enter", "keys.backspace", "keys.delete", "shortcuts.title", "sc.alttab", "sc.taskview", "sc.desktop", "sc.fullscreen", "sc.space", "sc.snip",
        "sc.close", "sc.copy", "sc.paste", "sc.undo", "sc.selectall", "sc.zoomin", "sc.zoomout", "sc.close.confirm", "windows.title", "windows.desktop", "windows.none", "windows.close.confirm",
    ];

    /// <summary>Actions réservées aux téléphones en « Contrôle complet ».</summary>
    private static readonly HashSet<string> FullControlActions = ["sleep", "restart", "shutdown", "window-close"];

    private static byte[]? _font, _icon;

    private readonly IRemoteBackend _backend;
    private readonly Func<IReadOnlyList<RemoteDevice>> _devices;
    private readonly Action<RemoteDevice> _add;
    private readonly Action<string> _remove;
    private readonly RemoteUploads? _uploads;
    private int _attempts;

    /// <param name="uploads">Où vont les fichiers envoyés par le téléphone (null : pas d'envoi).</param>
    public RemoteApi(IRemoteBackend backend, Func<IReadOnlyList<RemoteDevice>> devices, Action<RemoteDevice> add, Action<string> remove,
        RemoteUploads? uploads = null)
    {
        _backend = backend;
        _devices = devices;
        _add = add;
        _remove = remove;
        _uploads = uploads;
    }

    public string? PairingCode { get; private set; }
    public DateTime PairingUntil { get; private set; }
    public bool PairingOpen => PairingCode != null && DateTime.UtcNow < PairingUntil;

    /// <summary>Appairage ouvert, fermé, téléphone ajouté ou retiré.</summary>
    public event Action? Changed;

    /// <summary>Dernier téléphone ajouté (annonce dans les paramètres).</summary>
    public string? LastPaired { get; private set; }

    public void OpenPairing(string? code = null)
    {
        PairingCode = code ?? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        PairingUntil = DateTime.UtcNow + PairingWindow;
        _attempts = 0;
        LastPaired = null;
        Changed?.Invoke();
    }

    public void ClosePairing()
    {
        if (PairingCode == null) return;
        PairingCode = null;
        Changed?.Invoke();
    }

    /// <summary>Une requête, sur le thread de l'interface (les envois de fichiers et la carte réseau sont lus à côté).</summary>
    public async Task<RemoteResponse> HandleAsync(RemoteRequest request)
    {
        if (!HostIsAddress(request.Header("host"))) return RemoteResponse.Empty(421);
        var route = (request.Method, request.Path);
        switch (route)
        {
            case ("GET", "/" or "/index.html"):
                return Page();
            case ("GET", "/font.ttf"):
                return new RemoteResponse(200, "font/ttf", _font ??= Resource("Assets/Fonts/lucide.ttf")) { Cacheable = true };
            case ("GET", "/icon.png"):
                return new RemoteResponse(200, "image/png", _icon ??= OpalineLogo.Png(OpalineLogo.Render(180))) { Cacheable = true };
            case ("GET", "/manifest.webmanifest"):
                return new RemoteResponse(200, "application/manifest+json", Encoding.UTF8.GetBytes(Manifest())) { Cacheable = true };
            case ("POST", "/api/pair"):
                return Pair(request);
        }
        if (!request.Path.StartsWith("/api/", StringComparison.Ordinal)) return RemoteResponse.Empty(404);

        var device = Authenticate(request);
        if (device == null) return RemoteResponse.Json(401, """{"error":"unpaired"}""");
        switch (route)
        {
            case ("GET", "/api/state"):
                return RemoteResponse.Json(200, _backend.State(device).ToJsonString());
            case ("GET", "/api/cover"):
                return _backend.Cover() is { } png ? new RemoteResponse(200, "image/png", png) : RemoteResponse.Empty(204);
            case ("POST", "/api/action"):
                var body = ReadJson(request);
                var action = body?["action"]?.GetValue<string>();
                var value = body?["value"] is JsonValue v ? v.ToString() : null;
                if (action != null && FullControlActions.Contains(action) && !device.FullControl) return Denied();
                if (action == null || !await _backend.DoAsync(action, value, device)) return RemoteResponse.Json(400, """{"error":"action"}""");
                return RemoteResponse.Json(200, _backend.State(device).ToJsonString());
            case ("GET", "/api/apps"):
                return RemoteResponse.Json(200, _backend.Apps(request.Param("q")).ToJsonString());
            case ("GET", "/api/icon"):
                return request.Param("id") is { } id && _backend.AppIcon(id) is { } icon ? new RemoteResponse(200, "image/png", icon) : RemoteResponse.Empty(404);
            case ("GET", "/api/clipboard"):
                if (!device.FullControl) return Denied();
                return _backend.Clipboard() is { } text
                    ? RemoteResponse.Json(200, new JsonObject { ["text"] = text }.ToJsonString())
                    : RemoteResponse.Json(409, """{"error":"busy"}""");
            case ("POST", "/api/input"):
                if (!device.FullControl) return Denied();
                if (ReadJson(request) is not { } input) return RemoteResponse.Json(400, """{"error":"input"}""");
                return _backend.Input(input) switch
                {
                    null => RemoteResponse.Json(200, "{}"),
                    "gaming" => RemoteResponse.Json(409, """{"error":"gaming"}"""),
                    _ => RemoteResponse.Json(400, """{"error":"input"}"""),
                };
            case ("GET", "/api/windows"):
                return RemoteResponse.Json(200, _backend.Windows().ToJsonString());
            case ("GET", "/api/screen"):
                if (!device.FullControl) return Denied();
                int.TryParse(request.Param("n"), out int screen);
                Log.Info("Télécommande : aperçu de l'écran demandé");
                return await Task.Run(() => _backend.Screen(screen)) is { } jpeg ? new RemoteResponse(200, "image/jpeg", jpeg) : RemoteResponse.Empty(404);
            case ("GET", "/api/wake"):
                return RemoteResponse.Json(200, (await Task.Run(_backend.Wake))?.ToJsonString() ?? "{}");
            case ("POST", RemoteServer.UploadPath) when _uploads != null:
                var uploads = _uploads;
                return await Task.Run(() => uploads.Receive(request));
            case ("POST", "/api/forget"):
                Log.Info("Télécommande : un téléphone s'est retiré lui-même");
                _remove(device.Id);
                Changed?.Invoke();
                return RemoteResponse.Json(200, "{}");
            default:
                return RemoteResponse.Empty(404);
        }
    }

    private static RemoteResponse Denied() => RemoteResponse.Json(403, """{"error":"denied"}""");

    // ------------------------------------------------------------------ appairage

    private RemoteResponse Pair(RemoteRequest request)
    {
        if (!PairingOpen) return RemoteResponse.Json(403, """{"error":"closed"}""");
        var code = ReadJson(request)?["code"]?.GetValue<string>()?.Trim() ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(PairingCode!)))
        {
            if (++_attempts >= MaxAttempts)
            {
                Log.Info("Télécommande : trop de codes faux, appairage fermé");
                ClosePairing();
                return RemoteResponse.Json(403, """{"error":"closed"}""");
            }
            return RemoteResponse.Json(403, """{"error":"code"}""");
        }
        if (_devices().Count >= RemoteSettings.MaxDevices) return RemoteResponse.Json(409, """{"error":"full"}""");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var device = new RemoteDevice
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Name = UniqueName(DeviceName(request.Header("user-agent"))),
            TokenHash = Hash(token),
            Added = DateTime.Now,
        };
        _add(device);
        Log.Info("Télécommande : téléphone appairé");
        LastPaired = device.Name;
        PairingCode = null;
        Changed?.Invoke();
        return RemoteResponse.Json(200, new JsonObject { ["token"] = token, ["name"] = device.Name }.ToJsonString());
    }

    private RemoteDevice? Authenticate(RemoteRequest request)
    {
        var header = request.Header("authorization");
        if (header == null || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(header[7..].Trim()));
        RemoteDevice? found = null;
        foreach (var device in _devices()) // on compare tout, en temps constant
            if (CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(device.TokenHash))) found = device;
        return found;
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Nom lisible du téléphone d'après son navigateur (« iPhone », « Android (Pixel 8) »…).</summary>
    internal static string DeviceName(string? agent)
    {
        agent ??= "";
        if (agent.Contains("iPhone")) return "iPhone";
        if (agent.Contains("iPad")) return "iPad";
        if (agent.Contains("Android"))
        {
            var model = Regex.Match(agent, @"Android [\d.]+; ([^;)]+)\)").Groups[1].Value.Trim();
            return model.Length is 0 or 1 || model.Length > 30 ? "Android" : $"Android ({model})"; // « K » : modèle masqué par Chrome
        }
        if (agent.Contains("Macintosh")) return "Mac";
        if (agent.Contains("Windows")) return "PC Windows";
        return Loc.T("remote.device.browser");
    }

    private string UniqueName(string name)
    {
        var names = _devices().Select(d => d.Name).ToHashSet();
        if (!names.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!names.Contains($"{name} {i}")) return $"{name} {i}";
    }

    // ------------------------------------------------------------------ fichiers

    private static RemoteResponse Page()
    {
        var strings = new JsonObject();
        foreach (var key in PageKeys) strings[key] = Loc.T($"remote.page.{key}");
        var html = PageTemplate.Value
            .Replace("__LANG__", Loc.Language)
            .Replace("/*STRINGS*/null", strings.ToJsonString()); // JSON échappe < et > : rien ne sort du script
        return new RemoteResponse(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)) { Security = Security };
    }

    private static readonly Lazy<string> PageTemplate = new(() =>
    {
        using var stream = typeof(RemoteApi).Assembly.GetManifestResourceStream("DynamicIsland.Remote.RemotePage.html")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    private static string Manifest() => new JsonObject
    {
        ["name"] = Loc.T("remote.page.title"),
        ["short_name"] = AppInfo.Name,
        // Pas de « start_url » : l'icône de l'écran d'accueil garde l'adresse du moment, avec la clé du téléphone (#t=…).
        ["display"] = "standalone",
        ["background_color"] = "#0B0B10",
        ["theme_color"] = "#0B0B10",
        ["icons"] = new JsonArray(new JsonObject { ["src"] = "/icon.png", ["sizes"] = "180x180", ["type"] = "image/png" }),
    }.ToJsonString();

    private static byte[] Resource(string path)
    {
        using var stream = Application.GetResourceStream(new Uri(path, UriKind.Relative))!.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static JsonObject? ReadJson(RemoteRequest request)
    {
        try
        {
            return request.Body.Length == 0 ? null : JsonNode.Parse(request.Body) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>« 192.168.1.20:48765 » ou « [fe80::1]:48765 » : oui ; un nom de domaine : non.</summary>
    internal static bool HostIsAddress(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (host.StartsWith('['))
        {
            int end = host.IndexOf(']');
            return end > 0 && IPAddress.TryParse(host[1..end], out _);
        }
        int colon = host.LastIndexOf(':');
        var name = colon >= 0 ? host[..colon] : host;
        return IPAddress.TryParse(name, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }
}
