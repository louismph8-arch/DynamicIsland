namespace DynamicIsland.Services;

/// <summary>
/// Calculs purs de la barre de position de l'onglet Musique (testés par --self-test) : clic → temps, molette ±5 s bornée, et
/// période pendant laquelle la position publiée par le lecteur est ignorée (pendant un glisser et 1,5 s après le lâcher).
/// </summary>
public static class SeekMath
{
    /// <summary>Un cran de molette sur la barre.</summary>
    public static readonly TimeSpan WheelStep = TimeSpan.FromSeconds(5);

    /// <summary>Après un changement de position : on ignore le lecteur le temps qu'il suive.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);

    /// <summary>Temps visé par un clic à <paramref name="x"/> DIP sur une barre de <paramref name="width"/> DIP.</summary>
    public static TimeSpan At(double x, double width, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || width <= 0 || double.IsNaN(x)) return TimeSpan.Zero;
        double fraction = Math.Clamp(x / width, 0, 1);
        return TimeSpan.FromSeconds(Math.Round(duration.TotalSeconds * fraction, 1));
    }

    /// <summary>Molette : un cran vers le haut = +5 s, vers le bas = −5 s, sans sortir du morceau.</summary>
    public static TimeSpan Wheel(TimeSpan position, int delta, TimeSpan duration) =>
        delta == 0 ? Clamp(position, duration) : Clamp(position + Math.Sign(delta) * WheelStep, duration);

    /// <summary>Entre 0 et la durée (un peu avant la fin : aller pile à la fin passe souvent au morceau suivant).</summary>
    public static TimeSpan Clamp(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || position < TimeSpan.Zero) return TimeSpan.Zero;
        var last = duration - TimeSpan.FromSeconds(Math.Min(1, duration.TotalSeconds / 2));
        return position > last ? last : position;
    }

    /// <summary>Faux tant que la position du lecteur doit être ignorée (<paramref name="holdUntil"/> pas encore passé).</summary>
    public static bool Accept(DateTimeOffset now, DateTimeOffset holdUntil) => now >= holdUntil;

    /// <summary>Fraction remplie de la barre (0 à 1).</summary>
    public static double Fraction(TimeSpan position, TimeSpan duration) =>
        duration <= TimeSpan.Zero ? 0 : Math.Clamp(position / duration, 0, 1);
}
