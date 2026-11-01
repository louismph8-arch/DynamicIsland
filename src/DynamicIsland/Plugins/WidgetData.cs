using System.Diagnostics;
using DynamicIsland.Services;
using DynamicIsland.Settings;

namespace DynamicIsland.Plugins;

/// <summary>
/// Données réelles de la liste blanche, lues aux services d'Opaline (thread de l'interface). Les mesures coûteuses ne sont
/// faites que si un widget affiché les demande : processeur / mémoire / carte graphique / réseau au plus toutes les 1,5 s,
/// température du GPU (pilote NVIDIA) seulement si un widget l'utilise.
/// </summary>
public sealed class LiveWidgetData : IWidgetData
{
    private static readonly TimeSpan StatsAge = TimeSpan.FromSeconds(1.5);
    private readonly SystemStatsService _stats = new();
    private SystemStats? _sample;
    private long _sampledAt;
    private Services.Health.NvidiaGpu? _gpu;
    private bool _gpuTried;
    private int? _gpuTemperature;
    private long _gpuAt;

    public MediaService? Media { get; set; }

    public string? Text(string key, string? format)
    {
        switch (key)
        {
            case "time": return DateTime.Now.ToString(WidgetTemplate.DateFormatOr(format, "HH:mm"), Loc.Culture);
            case "date": return DateTime.Now.ToString(WidgetTemplate.DateFormatOr(format, "d MMMM"), Loc.Culture);
            case "weather.temp" or "weather.min" or "weather.max" or "weather.city" or "weather.desc" or "weather.icon":
            {
                var weather = WeatherService.Shared;
                weather.RefreshIfNeeded(SettingsStore.Current.Widgets.City); // relit seulement si c'est trop vieux
                if (weather.Current is not { } report) return key == "weather.city" ? SettingsStore.Current.Widgets.City : "—";
                return key switch
                {
                    "weather.temp" => $"{(int)Math.Round(report.Temperature)}°",
                    "weather.min" => $"{(int)Math.Round(report.Min)}°",
                    "weather.max" => $"{(int)Math.Round(report.Max)}°",
                    "weather.city" => report.City,
                    "weather.desc" => report.Description,
                    _ => report.Symbol,
                };
            }
            case "music.title": return Media is { HasSession: true } m && m.Title.Length > 0 ? m.Title : Loc.T("widget.music.none");
            case "music.artist": return Media is { HasSession: true } a ? a.Artist : "";
            case "music.app": return Media is { HasSession: true } p ? p.AppName : "";
            case "cpu" or "ram" or "gpu":
                return Number(key) is { } percent ? Loc.F("format.percent", Math.Round(percent)) : "—";
            case "gpu.temp": return Number(key) is { } celsius ? $"{celsius:0} °C" : "—";
            case "net.down": return SystemStatsService.FormatRate(Stats()?.Download);
            case "net.up": return SystemStatsService.FormatRate(Stats()?.Upload);
            case "friends.online": return Number(key)?.ToString("0", Loc.Culture);
            default: return null;
        }
    }

    public double? Number(string key) => key switch
    {
        "cpu" => Stats()?.Cpu,
        "ram" => Stats()?.Ram,
        "gpu" => Stats()?.Gpu,
        "gpu.temp" => GpuTemperature(),
        "weather.temp" => WeatherService.Shared.Current?.Temperature,
        "friends.online" => Services.Friends.FriendsService.Lists.Friends.Count(f => f.Online),
        _ => null,
    };

    private SystemStats? Stats()
    {
        if (_sample == null || Stopwatch.GetElapsedTime(_sampledAt) > StatsAge)
        {
            bool first = _sample == null;
            _sample = _stats.Sample();
            if (first) _sample = _sample with { Cpu = null, Download = null, Upload = null }; // premier relevé = référence seulement
            _sampledAt = Stopwatch.GetTimestamp();
        }
        return _sample;
    }

    private double? GpuTemperature()
    {
        if (!_gpuTried)
        {
            _gpuTried = true;
            try
            {
                _gpu = new Services.Health.NvidiaGpu();
            }
            catch (Exception ex)
            {
                Log.Error("Widgets des potes : température du GPU", ex);
            }
        }
        if (_gpu is not { Available: true }) return null;
        if (Stopwatch.GetElapsedTime(_gpuAt) > StatsAge)
        {
            _gpuTemperature = _gpu.Temperature();
            _gpuAt = Stopwatch.GetTimestamp();
        }
        return _gpuTemperature;
    }
}

/// <summary>Données d'exemple (aperçu avant d'installer, rendus --snapshot, auto-test) : rien n'est mesuré ni demandé.</summary>
public sealed class SampleWidgetData : IWidgetData
{
    public static readonly SampleWidgetData Instance = new();

    /// <summary>Champs d'exemple d'une source web (« web.usd » → 67 512).</summary>
    public static readonly IReadOnlyDictionary<string, string> Web = new Dictionary<string, string>
    {
        ["usd"] = "67512.4", ["eur"] = "62230.9", ["cad"] = "93125", ["change"] = "2.37", ["price"] = "67512.4",
    };

    /// <summary>Heure figée pour les rendus (null = heure réelle).</summary>
    public DateTime? Now { get; init; }

    public string? Text(string key, string? format)
    {
        var now = Now ?? DateTime.Now;
        return key switch
        {
            "time" => now.ToString(WidgetTemplate.DateFormatOr(format, "HH:mm"), Loc.Culture),
            "date" => now.ToString(WidgetTemplate.DateFormatOr(format, "d MMMM"), Loc.Culture),
            "weather.temp" => "6°",
            "weather.min" => "2°",
            "weather.max" => "9°",
            "weather.city" => "Montréal",
            "weather.desc" => Loc.T("weather.partly_cloudy"),
            "weather.icon" => Themes.Icons.WeatherCloud,
            "music.title" => "Lumières de minuit",
            "music.artist" => "Opaline",
            "music.app" => "Spotify",
            "cpu" => Loc.F("format.percent", 23),
            "ram" => Loc.F("format.percent", 58),
            "gpu" => Loc.F("format.percent", 12),
            "gpu.temp" => "54 °C",
            "net.down" => SystemStatsService.FormatRate(2_400_000),
            "net.up" => SystemStatsService.FormatRate(180_000),
            "friends.online" => "3",
            _ => null,
        };
    }

    public double? Number(string key) => key switch
    {
        "cpu" => 23,
        "ram" => 58,
        "gpu" => 12,
        "gpu.temp" => 54,
        "weather.temp" => 6,
        "friends.online" => 3,
        _ => null,
    };
}
