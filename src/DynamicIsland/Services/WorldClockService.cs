using System.Net.Http;
using System.Text.Json;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Horloges du monde (6C) : une ville = son nom + son fuseau horaire IANA (« Europe/Paris »), trouvé une fois par le
/// géocodage gratuit d'Open-Meteo (celui de la météo), puis l'heure est calculée sur place par Windows (TimeZoneInfo,
/// heure d'été comprise). Rien n'est envoyé ensuite.
/// </summary>
public static class WorldClockService
{
    public const int MaxClocks = 4;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Ville trouvée : nom affiché (« Paris, France ») et fuseau.</summary>
    public sealed record Place(string Name, string TimeZone);

    public static async Task<Place?> FindAsync(string city)
    {
        city = city.Trim();
        if (city.Length == 0) return null;
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language={Loc.Language}&format=json";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var first = results[0];
        var name = first.TryGetProperty("name", out var n) ? n.GetString() : null;
        var country = first.TryGetProperty("country", out var c) ? c.GetString() : null;
        var zone = first.TryGetProperty("timezone", out var z) ? z.GetString() : null;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(zone) || Find(zone) == null) return null;
        return new Place(string.IsNullOrEmpty(country) ? name : $"{name}, {country}", zone);
    }

    /// <summary>Fuseau Windows correspondant (identifiants IANA ou Windows), ou null s'il est inconnu.</summary>
    public static TimeZoneInfo? Find(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && windows != null ? Find(windows) : null;
        }
    }

    /// <summary>Heure de cette horloge maintenant, et décalage avec l'heure du PC (en heures, ex. +6 ou -5,5).</summary>
    public static (DateTime Time, double Offset)? Now(WorldClock clock)
    {
        if (Find(clock.TimeZone) is not { } zone) return null;
        var utc = DateTime.UtcNow;
        var there = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var here = DateTime.Now;
        return (there, Math.Round((there - here).TotalHours * 2) / 2);
    }
}
