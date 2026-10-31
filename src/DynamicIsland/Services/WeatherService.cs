using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>Météo actuelle de la ville choisie.</summary>
public sealed record WeatherReport(string City, double Temperature, double Min, double Max, int Code, bool IsDay, DateTime UpdatedAt)
{
    /// <summary>Description française du code météo WMO.</summary>
    public string Description => Code switch
    {
        0 => Loc.T("weather.clear"),
        1 => Loc.T("weather.mostly_clear"),
        2 => Loc.T("weather.partly_cloudy"),
        3 => Loc.T("weather.overcast"),
        45 or 48 => Loc.T("weather.fog"),
        51 or 53 or 55 or 56 or 57 => Loc.T("weather.drizzle"),
        61 or 63 or 65 or 66 or 67 => Loc.T("weather.rain"),
        71 or 73 or 75 or 77 => Loc.T("weather.snow"),
        80 or 81 or 82 => Loc.T("weather.showers"),
        85 or 86 => Loc.T("weather.snow_showers"),
        95 or 96 or 99 => Loc.T("weather.storm"),
        _ => "—",
    };

    /// <summary>Icône Lucide (police d'icônes de l'app).</summary>
    public string Symbol => Code switch
    {
        0 or 1 => IsDay ? Themes.Icons.WeatherSun : Themes.Icons.WeatherMoon,
        2 => Themes.Icons.WeatherPartly,
        3 => Themes.Icons.WeatherCloud,
        45 or 48 => Themes.Icons.WeatherFog,
        >= 51 and <= 67 or >= 80 and <= 82 => Themes.Icons.WeatherRain,
        >= 71 and <= 77 or 85 or 86 => Themes.Icons.WeatherSnow,
        >= 95 => Themes.Icons.WeatherStorm,
        _ => Themes.Icons.WeatherCloud,
    };
}

/// <summary>
/// Météo via Open-Meteo (gratuit, sans clé). La ville est d'abord géocodée (une fois par nom, sauf si ses coordonnées sont
/// déjà connues), puis la météo actuelle est relue au plus toutes les 5 à 60 minutes (réglage, 10 par défaut), seulement
/// quand elle est affichée, et à la sortie de veille (correctif 18).
/// </summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    /// <summary>Sortie de veille : on laisse le réseau revenir avant de relire.</summary>
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(10);
    /// <summary>Après un échec (réseau), nouvel essai au plus tôt une minute plus tard.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private (string Query, double Latitude, double Longitude, string Name)? _place;
    private Task? _pending;
    private bool _stale;
    private DateTime _failedAt;
    private DispatcherTimer? _resumeTimer;

    /// <summary>Météo partagée (onglet Widgets de l'île, widgets du bureau, barre de menus) : une seule requête.</summary>
    public static WeatherService Shared { get; } = new();

    private WeatherService()
    {
        // Après la veille, la météo affichée peut dater de la veille au soir : marquée périmée puis relue.
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Guarded.OnUi(OnResume, "Météo : sortie de veille");
        };
    }

    public WeatherReport? Current { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Levé (sur le thread UI) quand une nouvelle météo est disponible ou en cas d'erreur.</summary>
    public event Action? Changed;

    /// <summary>Âge maximal de la météo affichée (Paramètres → Widgets).</summary>
    private static TimeSpan RefreshEvery => TimeSpan.FromMinutes(SettingsStore.Current.Widgets.WeatherRefreshMinutes);

    /// <summary>Relit la météo si elle est trop ancienne ou si la ville a changé.</summary>
    public void RefreshIfNeeded(string city)
    {
        city = string.IsNullOrWhiteSpace(city) ? "Montréal" : city.Trim();
        bool cityChanged = _place?.Query != city;
        bool stale = _stale || Current == null || DateTime.Now - Current.UpdatedAt > RefreshEvery;
        if (!cityChanged && !stale) return;
        if (!cityChanged && DateTime.Now - _failedAt < RetryAfter) return; // hors ligne : pas un essai à chaque affichage
        if (_pending is { IsCompleted: false }) return;
        _pending = RefreshAsync(city);
    }

    private void OnResume()
    {
        _stale = true;
        if (Current == null && Error == null) return; // jamais affichée depuis le lancement : rien à relire
        if (_resumeTimer == null)
        {
            _resumeTimer = new DispatcherTimer { Interval = ResumeDelay };
            _resumeTimer.Tick += (_, _) =>
            {
                _resumeTimer.Stop();
                Guarded.Run(() => RefreshIfNeeded(SettingsStore.Current.Widgets.City), "Météo : sortie de veille");
            };
        }
        _resumeTimer.Stop();
        _resumeTimer.Start();
    }

    private async Task RefreshAsync(string city)
    {
        try
        {
            if (_place?.Query != city) _place = Known(city) ?? await GeocodeAsync(city);
            if (_place is not { } place)
            {
                Error = Loc.F("weather.city_not_found", city);
                Current = null;
                Changed?.Invoke();
                return;
            }

            var url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1");
            using var json = JsonDocument.Parse(await Http.GetStringAsync(url));
            var current = json.RootElement.GetProperty("current");
            var daily = json.RootElement.GetProperty("daily");
            Current = new WeatherReport(
                place.Name,
                current.GetProperty("temperature_2m").GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                current.GetProperty("weather_code").GetInt32(),
                current.GetProperty("is_day").GetInt32() == 1,
                DateTime.Now);
            Error = null;
            _stale = false;
        }
        catch (Exception ex)
        {
            Log.Error("Météo", ex);
            Error = Loc.T("weather.unavailable");
            _failedAt = DateTime.Now;
        }
        Changed?.Invoke();
    }

    /// <summary>Coordonnées déjà rangées pour cette ville (détectée, ou cherchée une fois pour le soleil) : pas de géocodage.</summary>
    private static (string, double, double, string)? Known(string city)
    {
        var widgets = SettingsStore.Current.Widgets;
        return widgets.CityLatitude is double latitude && widgets.CityLongitude is double longitude
               && string.Equals(widgets.CityCoordinatesFor, city, StringComparison.OrdinalIgnoreCase)
            ? (city, latitude, longitude, city)
            : null;
    }

    /// <summary>Coordonnées d'une ville (Open-Meteo) : nom demandé, latitude, longitude, nom trouvé ; null si inconnue.</summary>
    internal static async Task<(string, double, double, string)?> GeocodeAsync(string city)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language={Loc.Language}&format=json";
        using var json = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!json.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var place = results[0];
        return (city, place.GetProperty("latitude").GetDouble(), place.GetProperty("longitude").GetDouble(),
            place.GetProperty("name").GetString() ?? city);
    }
}
