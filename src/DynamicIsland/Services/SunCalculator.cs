namespace DynamicIsland.Services;

/// <summary>
/// Lever et coucher du soleil, calculés sur place (équations de la NOAA, précision de l'ordre de la minute),
/// sans réseau. Heures rendues dans le fuseau horaire du PC.
/// </summary>
public static class SunCalculator
{
    /// <summary>Montréal, ville par défaut de la météo et du thème automatique.</summary>
    public const double MontrealLatitude = 45.5019, MontrealLongitude = -73.5674;

    /// <summary>Soleil levé toute la journée (jour polaire) ou jamais (nuit polaire) : pas d'heures.</summary>
    public enum Polar { None, AlwaysDay, AlwaysNight }

    public readonly record struct SunTimes(DateTime Sunrise, DateTime Sunset, Polar Polar);

    public static SunTimes For(DateOnly date, double latitude, double longitude)
    {
        // Angle de l'année au midi solaire, puis équation du temps (minutes) et déclinaison du soleil (radians).
        double gamma = 2 * Math.PI / 365 * (date.DayOfYear - 1);
        double equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                    - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double declination = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                             - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                             - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double lat = latitude * Math.PI / 180;
        // 90,833° : réfraction de l'atmosphère + rayon du disque solaire.
        double cosHourAngle = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(lat) * Math.Cos(declination))
                              - Math.Tan(lat) * Math.Tan(declination);
        var midnightUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        if (cosHourAngle > 1) return new SunTimes(midnightUtc, midnightUtc, Polar.AlwaysNight);
        if (cosHourAngle < -1) return new SunTimes(midnightUtc, midnightUtc, Polar.AlwaysDay);

        double hourAngle = Math.Acos(cosHourAngle) * 180 / Math.PI;
        double sunriseMinutes = 720 - 4 * (longitude + hourAngle) - equation;
        double sunsetMinutes = 720 - 4 * (longitude - hourAngle) - equation;
        return new SunTimes(midnightUtc.AddMinutes(sunriseMinutes).ToLocalTime(), midnightUtc.AddMinutes(sunsetMinutes).ToLocalTime(), Polar.None);
    }

    /// <summary>Vrai si le soleil est levé à cet instant (heure locale du PC).</summary>
    public static bool IsDay(DateTime now, double latitude, double longitude)
    {
        var sun = For(DateOnly.FromDateTime(now), latitude, longitude);
        return sun.Polar switch
        {
            Polar.AlwaysDay => true,
            Polar.AlwaysNight => false,
            _ => now >= sun.Sunrise && now < sun.Sunset,
        };
    }
}
