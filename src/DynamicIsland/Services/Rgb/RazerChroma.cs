using System.Net.Http;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// Razer : API REST locale officielle de Razer Chroma (installée avec Razer Synapse + son module Chroma). On s'inscrit par un
/// POST sur http://localhost:54235/razer/chromasdk (nom de l'appli, appareils), Synapse répond une adresse de session ; un
/// PUT …/heartbeat au moins toutes les 15 s la garde en vie ; chaque type d'appareil reçoit un effet CHROMA_STATIC (couleur
/// en BGR : 0x00BBGGRR). Le DELETE de la session rend la main à Synapse (ses effets reviennent).
/// </summary>
internal sealed class RazerChroma : IRgbBackend
{
    public const string DefaultUrl = "http://localhost:54235/razer/chromasdk";
    /// <summary>Processus de Synapse 3 / 4 et du service Chroma (sans .exe).</summary>
    private static readonly string[] Processes = ["RzSDKService", "RzChromaSDKService", "Razer Synapse 3", "Razer Synapse Service", "RazerAppEngine"];
    internal static readonly string[] Devices = ["keyboard", "mouse", "headset", "mousepad", "keypad", "chromalink"];

    private readonly string _url;
    private readonly bool _checkProcess;
    private readonly TimeSpan _settle;
    private readonly HttpClient _http = RgbHttp.CreateClient();
    private string? _session;

    /// <param name="url">Adresse d'inscription (un faux serveur pour l'auto-test).</param>
    /// <param name="settle">Attente après l'inscription : Synapse ignore les effets envoyés trop tôt.</param>
    public RazerChroma(string url = DefaultUrl, bool checkProcess = true, TimeSpan? settle = null)
    {
        _url = url;
        _checkProcess = checkProcess;
        _settle = settle ?? TimeSpan.FromSeconds(1);
    }

    public string Name => "Razer Chroma";
    public RgbBrandState State { get; private set; }
    public bool IsConnected => _session != null;

    public void Start()
    {
        if (_session != null) return;
        if (_checkProcess && !RgbHttp.AnyProcess(Processes))
        {
            State = RgbBrandState.NotRunning;
            return;
        }
        try
        {
            var app = new JsonObject
            {
                ["title"] = AppInfo.Name,
                ["description"] = "Opaline : couleur d'accent sur l'éclairage",
                ["author"] = new JsonObject { ["name"] = AppInfo.Name, ["contact"] = "github.com/louismph8-arch/DynamicIsland" },
                ["device_supported"] = new JsonArray([.. Devices.Select(d => (JsonNode)JsonValue.Create(d))]),
                ["category"] = "application",
            };
            var (ok, body) = RgbHttp.Send(_http, HttpMethod.Post, _url, app);
            if (!ok || body?["uri"]?.GetValue<string>() is not { Length: > 0 } uri
                || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsLoopback && parsed.Host != "chromasdk.io")
            {
                // Pas d'adresse de session (Chroma désactivé dans Synapse, ou code d'erreur dans « result »).
                State = RgbBrandState.Refused;
                Log.Info($"RVB : Razer Chroma refuse l'inscription ({body?["result"]?.ToJsonString() ?? "sans réponse"})");
                return;
            }
            _session = uri.TrimEnd('/');
            if (_settle > TimeSpan.Zero) Thread.Sleep(_settle);
            State = RgbBrandState.Connected;
            Log.Info("RVB : Razer Chroma connecté");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            State = RgbBrandState.Refused;
            Log.Info($"RVB : Razer Chroma indisponible ({ex.GetType().Name})");
        }
    }

    public bool SetColor(byte red, byte green, byte blue)
    {
        if (_session == null) return false;
        int bgr = red | green << 8 | blue << 16;
        try
        {
            bool any = false;
            foreach (var device in Devices)
            {
                var effect = new JsonObject { ["effect"] = "CHROMA_STATIC", ["param"] = new JsonObject { ["color"] = bgr } };
                var (ok, _) = RgbHttp.Send(_http, HttpMethod.Put, $"{_session}/{device}", effect);
                any |= ok;
            }
            if (any) return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        Lost();
        return false;
    }

    public bool Heartbeat()
    {
        if (_session == null) return false;
        try
        {
            if (RgbHttp.Send(_http, HttpMethod.Put, _session + "/heartbeat", null).Ok) return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        Lost();
        return false;
    }

    public void Stop()
    {
        if (_session == null) return;
        var session = _session;
        _session = null;
        State = RgbBrandState.Off;
        try
        {
            RgbHttp.Send(_http, HttpMethod.Delete, session, null);
            Log.Info("RVB : éclairage rendu à Razer Synapse");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Synapse oubliera la session tout seul, faute de battement de cœur.
        }
    }

    /// <summary>Session perdue (Synapse fermé ou redémarré) : on réessaiera au prochain tour.</summary>
    private void Lost()
    {
        _session = null;
        State = RgbBrandState.Refused;
        Log.Info("RVB : Razer Chroma ne répond plus");
    }
}
