using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using DynamicIsland.Settings;
using Windows.Devices.Geolocation;

namespace DynamicIsland.Services;

/// <summary>
/// « Détecter ma ville » (correctif 18), seulement quand l'utilisateur clique sur le bouton : d'abord la localisation de Windows
/// (Geolocator : Windows demande lui-même l'accord si besoin) puis le nom de la ville par BigDataCloud (gratuit, sans clé) ; si
/// la localisation est refusée, coupée ou ne répond pas en 10 s : la ville de l'adresse IP (ipwho.is, approximative). Le réseau
/// tourne hors du thread de l'interface, avec des délais courts ; rien n'est gardé ici.
/// </summary>
public static class CityDetector
{
    public enum Source { Windows, IpAddress }
    public enum Failure { None, Offline, NotFound }

    public sealed record Place(string City, double Latitude, double Longitude, Source Source);

    /// <summary>Résultat : la ville trouvée (et comment), ou pourquoi ça n'a pas marché.</summary>
    public sealed record Outcome(Place? Place, Failure Failure, bool WindowsUnavailable)
    {
        /// <summary>Phrase pour l'utilisateur (sous le champ de la ville).</summary>
        public string Message => Place is { } place
            ? place.Source == Source.Windows ? Loc.F("city.detect.found.windows", place.City)
            : WindowsUnavailable ? Loc.F("city.detect.found.ip_fallback", place.City)
            : Loc.F("city.detect.found.ip", place.City)
            : Failure == Failure.Offline ? Loc.T("city.detect.offline")
            : Loc.T("city.detect.failed");
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LocationMaxAge = TimeSpan.FromMinutes(30);

    /// <summary>Cherche la ville (à appeler depuis le clic : Windows peut alors demander l'accord pour la localisation).</summary>
    public static async Task<Outcome> DetectAsync()
    {
        string language = Loc.Language;
        var position = await WindowsPositionAsync(); // null = refusée, coupée ou sans réponse
        if (position is { } point)
        {
            try
            {
                var city = await Task.Run(() => ReverseGeocodeAsync(point.Latitude, point.Longitude, language));
                if (!string.IsNullOrWhiteSpace(city)) return new Outcome(new Place(city, point.Latitude, point.Longitude, Source.Windows), Failure.None, false);
            }
            catch (Exception ex) when (IsOffline(ex))
            {
                return new Outcome(null, Failure.Offline, false); // sans réseau, l'adresse IP ne répondrait pas non plus
            }
            catch (Exception ex)
            {
                Log.Error("Ville : nom de la position", ex);
            }
        }

        try
        {
            var place = await Task.Run(() => FromIpAddressAsync(language));
            return new Outcome(place, place == null ? Failure.NotFound : Failure.None, position == null);
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            return new Outcome(null, Failure.Offline, position == null);
        }
        catch (Exception ex)
        {
            Log.Error("Ville : adresse IP", ex);
            return new Outcome(null, Failure.NotFound, position == null);
        }
    }

    /// <summary>Range la ville trouvée dans les réglages (nom + coordonnées : la météo et le soleil n'ont plus à la chercher).</summary>
    public static void Apply(WidgetSettings widgets, Place place)
    {
        widgets.City = place.City;
        widgets.CityLatitude = place.Latitude;
        widgets.CityLongitude = place.Longitude;
        widgets.CityCoordinatesFor = place.City;
    }

    /// <summary>Position de Windows, ou null (accès refusé, localisation coupée, pas de réponse en 10 s).</summary>
    private static async Task<(double Latitude, double Longitude)?> WindowsPositionAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                Log.Info("Ville : localisation de Windows refusée ou coupée, adresse IP");
                return null;
            }
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(LocationMaxAge, LocationTimeout).AsTask()
                .WaitAsync(LocationTimeout + TimeSpan.FromSeconds(2));
            var coordinate = position.Coordinate.Point.Position;
            return (coordinate.Latitude, coordinate.Longitude);
        }
        catch (Exception ex)
        {
            Log.Info($"Ville : localisation de Windows indisponible ({ex.GetType().Name}), adresse IP");
            return null;
        }
    }

    private static async Task<string?> ReverseGeocodeAsync(double latitude, double longitude, string language)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={latitude}&longitude={longitude}&localityLanguage={language}");
        return CityFromReverse(await Http.GetStringAsync(url).ConfigureAwait(false));
    }

    private static async Task<Place?> FromIpAddressAsync(string language) =>
        PlaceFromIp(await Http.GetStringAsync($"https://ipwho.is/?lang={language}").ConfigureAwait(false));

    /// <summary>Réponse de BigDataCloud → nom de la ville (sinon la localité), null si aucun.</summary>
    internal static string? CityFromReverse(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var field in new[] { "city", "locality" })
            if (document.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } name && name.Trim().Length > 0)
                return name.Trim();
        return null;
    }

    /// <summary>Réponse d'ipwho.is (champs success, city, latitude, longitude) → ville, null si l'adresse n'a rien donné.</summary>
    internal static Place? PlaceFromIp(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) return null;
        if (!root.TryGetProperty("city", out var city) || city.ValueKind != JsonValueKind.String || city.GetString() is not { } name || name.Trim().Length == 0)
            return null;
        if (!root.TryGetProperty("latitude", out var lat) || !lat.TryGetDouble(out double latitude)
            || !root.TryGetProperty("longitude", out var lon) || !lon.TryGetDouble(out double longitude))
            return null;
        return new Place(name.Trim(), latitude, longitude, Source.IpAddress);
    }

    /// <summary>Pas de réseau (ou trop lent) : on le dit plutôt que « ville introuvable ».</summary>
    private static bool IsOffline(Exception ex) =>
        ex is HttpRequestException { StatusCode: null } or TaskCanceledException or TimeoutException or SocketException
        || ex.InnerException is SocketException;
}
