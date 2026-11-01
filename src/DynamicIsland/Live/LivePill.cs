using System.Windows;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Live;

/// <summary>Calculs purs de la pilule (O5), vérifiés par <c>--self-test</c> : choix de l'activité, largeur, batterie.</summary>
public static class LivePill
{
    /// <summary>
    /// Activité principale = la plus prioritaire (à égalité : la plus ancienne, pour ne pas sauter de l'une à l'autre) ;
    /// les autres ne font que compter (« +1 »).
    /// </summary>
    public static (LiveActivity? Main, int Others) Select(IEnumerable<LiveActivity> activities)
    {
        LiveActivity? main = null;
        int count = 0;
        foreach (var activity in activities)
        {
            count++;
            if (main == null || activity.Priority > main.Priority || (activity.Priority == main.Priority && activity.Since < main.Since)) main = activity;
        }
        return (main, Math.Max(0, count - 1));
    }

    /// <summary>Texte du compteur des autres activités : « +1 », « +2 »… (vide s'il n'y en a pas).</summary>
    public static string MoreText(int others) => others > 0 ? $"+{Math.Min(others, 9)}" : "";

    /// <summary>
    /// Largeur de la pilule : le groupe du centre (heure, lune, points, batterie) reste centré, avec de chaque côté la place
    /// du plus large des deux bords (anneau / pochette à gauche, valeur / égaliseur / « +1 » à droite). Au repos : l'heure
    /// seule avec un peu d'air, jamais moins de <see cref="IslandShape.CompactMin"/> ; jamais plus de <see cref="IslandShape.CompactMax"/>.
    /// </summary>
    /// <param name="center">Largeur du groupe du centre.</param>
    /// <param name="leading">Largeur du bord gauche (0 = rien).</param>
    /// <param name="trailing">Largeur du bord droit (0 = rien).</param>
    public static double Width(double center, double leading, double trailing)
    {
        double side = Math.Max(leading > 0 ? leading + IslandShape.PillGap : 0, trailing > 0 ? trailing + IslandShape.PillGap : 0);
        double width = side > 0
            ? center + 2 * (side + IslandShape.PillPad)
            : center + 2 * IslandShape.PillRestPad;
        return Math.Clamp(Math.Ceiling(width), IslandShape.CompactMin, IslandShape.CompactMax);
    }

    /// <summary>
    /// Batterie de la pilule : null sur un PC sans batterie (BatteryFlag 128 = pas de batterie, 255 = inconnu : un PC fixe
    /// n'affiche jamais rien), sinon icône Lucide + pourcentage.
    /// </summary>
    public static (string Icon, int Percent)? Battery(byte flag, byte percent, byte acLine)
    {
        if (flag is 128 or 255) return null;
        int value = percent <= 100 ? percent : 0;
        string icon = acLine == 1 ? Icons.BatteryCharging : value < 20 ? Icons.BatteryLow : value < 70 ? Icons.BatteryMedium : Icons.BatteryFull;
        return (icon, value);
    }

    /// <summary>Arc d'un anneau de progression (départ en haut, sens horaire) dans un carré de <paramref name="size"/>.</summary>
    public static Geometry Ring(double progress, double size, double stroke)
    {
        double center = size / 2, radius = (size - stroke) / 2;
        if (progress >= 0.999) return Frozen(new EllipseGeometry(new Point(center, center), radius, radius));
        double angle = Math.Max(progress, 0.001) * 2 * Math.PI;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true));
        return Frozen(new PathGeometry { Figures = { figure } });
    }

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>
/// « Respiration » de la bordure irisée pendant une activité (O5) : l'angle des reflets et leur éclat oscillent lentement
/// (période <see cref="Opal.BreathPeriod"/>), à faible cadence. Jamais au repos, ni en « Réduire les animations », en mode
/// Gaming, verre coupé, île ouverte / cachée, ni pendant l'alerte orange de fin du minuteur (elle passe devant).
/// </summary>
public static class LiveBreath
{
    /// <summary>Décalage d'angle (degrés) et opacité de la bordure à l'instant <paramref name="seconds"/>.</summary>
    public static (double Angle, double Glow) At(double seconds)
    {
        double phase = 2 * Math.PI * seconds / Opal.BreathPeriod.TotalSeconds;
        double wave = Math.Sin(phase); // −1 … 1
        double glow = Opal.BreathLow + (1 - Opal.BreathLow) * (0.5 + 0.5 * Math.Cos(phase)); // part de l'éclat normal : pas de saut
        return (wave * Opal.BreathSwing, glow);
    }

    public static bool ShouldRun(bool breathingActivity, bool reduceMotion, bool gaming, bool effectsOn, bool expanded, bool shown, bool alerting) =>
        breathingActivity && !reduceMotion && !gaming && effectsOn && !expanded && shown && !alerting;
}
