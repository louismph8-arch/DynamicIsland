using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// SteelSeries : API locale « GameSense » de SteelSeries GG (rien à installer ni à activer). L'adresse du serveur est écrite
/// par GG dans %PROGRAMDATA%\SteelSeries\SteelSeries Engine 3\coreProps.json (champ « address »). On déclare l'appli
/// (/game_metadata), un événement « COLOR » dont les gestionnaires prennent leur couleur dans les données de l'événement
/// (mode « context-color », clé « color ») pour le clavier, la souris, le casque et les appareils à zones (/bind_game_event),
/// puis on envoie la couleur (/game_event) et un battement de cœur (/game_heartbeat) toutes les quelques secondes.
/// /remove_game à l'arrêt : GG reprend ses effets.
/// </summary>
internal sealed class SteelSeriesGameSense : IRgbBackend
{
    private const string Game = "OPALINE";
    private const string Event = "COLOR";
    private const string FrameKey = "color";
    private static readonly string[] Processes = ["SteelSeriesGG", "SteelSeriesGGClient", "SteelSeriesEngine", "SteelSeriesEngine3"];

    /// <summary>Gestionnaires : (type d'appareil, zone), d'après les zones standard de GameSense.</summary>
    internal static readonly (string Device, string Zone)[] Zones =
    [
        ("rgb-per-key-zones", "all"),
        ("mouse", "wheel"),
        ("mouse", "logo"),
        ("headset", "earcups"),
        ("rgb-1-zone", "one"),
        ("rgb-2-zone", "one"), ("rgb-2-zone", "two"),
        ("rgb-3-zone", "one"), ("rgb-3-zone", "two"), ("rgb-3-zone", "three"),
        ("rgb-5-zone", "one"), ("rgb-5-zone", "two"), ("rgb-5-zone", "three"), ("rgb-5-zone", "four"), ("rgb-5-zone", "five"),
    ];

    private readonly string? _address;
    private readonly HttpClient _http = RgbHttp.CreateClient();
    private string? _base;
    private int _value;

    /// <param name="address">« hôte:port » imposé (faux serveur de l'auto-test) ; sinon lu dans coreProps.json, GG lancé.</param>
    public SteelSeriesGameSense(string? address = null) => _address = address;

    public static string CorePropsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "SteelSeries Engine 3", "coreProps.json");

    /// <summary>Adresse « hôte:port » du serveur GameSense lue dans coreProps.json, null si absente ou pas locale.</summary>
    internal static string? ReadAddress(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var address = JsonNode.Parse(File.ReadAllText(path))?["address"]?.GetValue<string>();
            // Seulement une adresse locale : ce fichier ne doit pas pouvoir nous envoyer ailleurs.
            return address != null && Uri.TryCreate("http://" + address, UriKind.Absolute, out var uri) && uri.IsLoopback ? address : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string Name => "SteelSeries GG";
    public RgbBrandState State { get; private set; }
    public bool IsConnected => _base != null;

    public void Start()
    {
        if (_base != null) return;
        var address = _address;
        if (address == null)
        {
            if (!RgbHttp.AnyProcess(Processes) || ReadAddress(CorePropsPath) is not { } read)
            {
                State = RgbBrandState.NotRunning;
                return;
            }
            address = read;
        }
        var root = "http://" + address;
        try
        {
            var metadata = new JsonObject
            {
                ["game"] = Game,
                ["game_display_name"] = AppInfo.Name,
                ["developer"] = AppInfo.Name,
                ["deinitialize_timer_length_ms"] = 15000,
            };
            var handlers = new JsonArray();
            foreach (var (device, zone) in Zones)
                handlers.Add(new JsonObject
                {
                    ["device-type"] = device,
                    ["zone"] = zone,
                    ["mode"] = "context-color",
                    ["context-frame-key"] = FrameKey,
                });
            var bind = new JsonObject
            {
                ["game"] = Game,
                ["event"] = Event,
                ["min_value"] = 0,
                ["max_value"] = 100,
                ["icon_id"] = 0,
                ["value_optional"] = true,
                ["handlers"] = handlers,
            };
            if (!Post(root, "game_metadata", metadata) || !Post(root, "bind_game_event", bind))
            {
                State = RgbBrandState.Refused;
                Log.Info("RVB : SteelSeries GG refuse l'inscription");
                return;
            }
            _base = root;
            State = RgbBrandState.Connected;
            Log.Info("RVB : SteelSeries GG connecté");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            State = RgbBrandState.Refused;
            Log.Info($"RVB : SteelSeries GG indisponible ({ex.GetType().Name})");
        }
    }

    public bool SetColor(byte red, byte green, byte blue)
    {
        if (_base == null) return false;
        // GG ignore un événement dont la valeur n'a pas changé : elle avance à chaque couleur.
        _value = (_value + 1) % 100;
        var payload = new JsonObject
        {
            ["game"] = Game,
            ["event"] = Event,
            ["data"] = new JsonObject
            {
                ["value"] = _value,
                ["frame"] = new JsonObject
                {
                    [FrameKey] = new JsonObject { ["red"] = (int)red, ["green"] = (int)green, ["blue"] = (int)blue },
                },
            },
        };
        return Try(() => Post(_base, "game_event", payload));
    }

    public bool Heartbeat() => _base != null && Try(() => Post(_base, "game_heartbeat", new JsonObject { ["game"] = Game }));

    public void Stop()
    {
        if (_base == null) return;
        var root = _base;
        _base = null;
        State = RgbBrandState.Off;
        try
        {
            Post(root, "remove_game", new JsonObject { ["game"] = Game });
            Log.Info("RVB : éclairage rendu à SteelSeries GG");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // GG arrête l'appli tout seul après 15 s sans nouvelles.
        }
    }

    private bool Post(string root, string path, JsonNode body) => RgbHttp.Send(_http, HttpMethod.Post, $"{root}/{path}", body).Ok;

    private bool Try(Func<bool> call)
    {
        try
        {
            if (call()) return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        _base = null;
        State = RgbBrandState.Refused;
        Log.Info("RVB : SteelSeries GG ne répond plus");
        return false;
    }
}
