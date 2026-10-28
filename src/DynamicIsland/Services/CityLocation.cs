using System.Globalization;
using System.Text;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Coordonnées de la ville de la météo (page Widgets), pour le lever et le coucher du soleil (thème automatique,
/// fond dynamique). Montréal est connue d'office ; une autre ville est cherchée une fois (Open-Meteo) puis gardée
/// dans les réglages. Sans réseau ou ville inconnue : nouvel essai une heure plus tard.
/// </summary>
public static class CityLocation
{
    private static string? _geocoding;  // ville en cours de recherche
    private static (string City, DateTime At)? _notFound;

    public readonly record struct Place(double Latitude, double Longitude, string Name);

    /// <summary>Coordonnées si elles sont connues ; sinon lance la recherche et renvoie null.</summary>
    public static Place? Get()
    {
        var widgets = SettingsStore.Current.Widgets;
        var city = widgets.City.Trim();
        if (widgets.CityLatitude is double lat && widgets.CityLongitude is double lon
            && string.Equals(widgets.CityCoordinatesFor, city, StringComparison.OrdinalIgnoreCase))
            return new Place(lat, lon, city);
        if (IsMontreal(city)) return new Place(SunCalculator.MontrealLatitude, SunCalculator.MontrealLongitude, city);
        bool waiting = _notFound is { } miss && miss.City == city && DateTime.UtcNow - miss.At < TimeSpan.FromHours(1);
        if (_geocoding != city && !waiting && !SettingsStore.IsReadOnly) _ = GeocodeAsync(city);
        return null;
    }

    /// <summary>Lever et coucher du jour, null tant que la ville n'est pas trouvée.</summary>
    public static SunCalculator.SunTimes? SunToday()
    {
        if (Get() is not { } place) return null;
        return SunCalculator.For(DateOnly.FromDateTime(DateTime.Now), place.Latitude, place.Longitude);
    }

    /// <summary>« 7 h 21 ».</summary>
    public static string Hour(DateTime time) => time.ToString(Loc.T("format.hour"), Loc.Culture);

    private static async Task GeocodeAsync(string city)
    {
        _geocoding = city;
        try
        {
            var found = await WeatherService.GeocodeAsync(city);
            if (found is not { } place)
            {
                _notFound = (city, DateTime.UtcNow);
                return;
            }
            var (_, lat, lon, _) = place;
            SettingsStore.Update(s =>
            {
                s.Widgets.CityLatitude = lat;
                s.Widgets.CityLongitude = lon;
                s.Widgets.CityCoordinatesFor = city;
            });
        }
        catch (Exception ex)
        {
            _notFound = (city, DateTime.UtcNow);
            Log.Error($"Coordonnées de « {city} »", ex);
        }
        finally
        {
            _geocoding = null;
        }
    }

    private static bool IsMontreal(string city)
    {
        var plain = new string(city.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return plain.Equals("montreal", StringComparison.OrdinalIgnoreCase);
    }
}
