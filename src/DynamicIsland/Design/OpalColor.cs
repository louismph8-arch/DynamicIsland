using System.Windows.Media;
using DynamicIsland.Settings;

namespace DynamicIsland.Design;

/// <summary>
/// Calculs purs des couleurs du verre opalin (refonte O2), testés par --self-test : interpolation douce en OKLCH (pas de
/// gris au milieu d'un passage bleu → orange), contraste WCAG 2.x, réglage automatique d'un texte, d'un accent ou d'un voile
/// pour rester lisible, et choix du thème (sombre / clair) selon le réglage et l'heure.
/// </summary>
public static class OpalColor
{
    // ------------------------------------------------------------------ OKLab / OKLCH (Björn Ottosson, 2020)

    public readonly record struct Oklch(double L, double C, double H, double A);

    public static Oklch ToOklch(Color color)
    {
        double r = Linear(color.R / 255.0), g = Linear(color.G / 255.0), b = Linear(color.B / 255.0);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double h = Math.Atan2(B, A) * 180 / Math.PI;
        return new Oklch(L, Math.Sqrt(A * A + B * B), (h + 360) % 360, color.A / 255.0);
    }

    /// <summary>Retour en sRGB ; une couleur hors de l'écran perd un peu de saturation (même teinte, même clarté).</summary>
    public static Color FromOklch(Oklch c)
    {
        double chroma = c.C;
        for (int i = 0; i < 12; i++)
        {
            var (r, g, b) = ToRgb(c.L, chroma, c.H);
            if ((r >= -0.0005 && r <= 1.0005 && g >= -0.0005 && g <= 1.0005 && b >= -0.0005 && b <= 1.0005) || chroma < 1e-4)
                return Color.FromArgb(Byte(c.A), Byte(r), Byte(g), Byte(b));
            chroma *= 0.85;
        }
        var (r2, g2, b2) = ToRgb(c.L, 0, c.H);
        return Color.FromArgb(Byte(c.A), Byte(r2), Byte(g2), Byte(b2));
    }

    private static (double R, double G, double B) ToRgb(double L, double C, double H)
    {
        double hr = H * Math.PI / 180, A = C * Math.Cos(hr), B = C * Math.Sin(hr);
        double l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        double m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        double s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);
        return (Gamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                Gamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                Gamma(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    /// <summary>
    /// Mélange de <paramref name="a"/> vers <paramref name="b"/> (0 → 1) en OKLCH : clarté et saturation en ligne droite, teinte
    /// par le plus court chemin sur le cercle ; une couleur presque grise prend la teinte de l'autre (pas de détour coloré).
    /// L'opacité est mélangée en ligne droite.
    /// </summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0) return a;
        if (t >= 1) return b;
        var x = ToOklch(a);
        var y = ToOklch(b);
        const double gray = 0.02;
        double hx = x.C < gray ? y.H : x.H, hy = y.C < gray ? x.H : y.H;
        double dh = ((hy - hx + 540) % 360) - 180;
        return FromOklch(new Oklch(x.L + (y.L - x.L) * t, x.C + (y.C - x.C) * t, (hx + dh * t + 360) % 360, x.A + (y.A - x.A) * t));
    }

    /// <summary>Courbe douce (accélère puis ralentit) pour les transitions de couleur.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    // ------------------------------------------------------------------ contraste WCAG

    /// <summary>Luminance relative WCAG (0 = noir, 1 = blanc) d'une couleur opaque.</summary>
    public static double Luminance(Color color) =>
        0.2126 * Linear(color.R / 255.0) + 0.7152 * Linear(color.G / 255.0) + 0.0722 * Linear(color.B / 255.0);

    /// <summary>Rapport de contraste WCAG (1 à 21) entre deux couleurs opaques.</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Contraste d'un texte (éventuellement translucide) posé sur un fond opaque : on compose d'abord le texte.</summary>
    public static double ContrastOn(Color text, Color background)
    {
        var bg = Opaque(background);
        return Contrast(OpalTheme.Over(text, bg), bg);
    }

    /// <summary>Seuils WCAG AA : texte normal 4,5, icônes et grands textes 3.</summary>
    public const double TextContrast = 4.5;
    public const double IconContrast = 3;

    /// <summary>
    /// Texte (ou icône) lisible sur <paramref name="background"/> : on rend d'abord le texte plus opaque, puis, si ça ne suffit
    /// pas, on l'éclaircit ou l'assombrit (même teinte, en OKLCH) juste ce qu'il faut pour atteindre <paramref name="min"/>.
    /// </summary>
    public static Color Readable(Color text, Color background, double min)
    {
        var bg = Opaque(background);
        if (ContrastOn(text, bg) >= min) return text;
        if (text.A < 255)
        {
            var opaque = Color.FromRgb(text.R, text.G, text.B);
            if (Contrast(opaque, bg) >= min)
            {
                byte lo = text.A, hi = 255;
                while (hi - lo > 1)
                {
                    byte mid = (byte)((lo + hi) / 2);
                    if (ContrastOn(Color.FromArgb(mid, text.R, text.G, text.B), bg) >= min) hi = mid; else lo = mid;
                }
                return Color.FromArgb(hi, text.R, text.G, text.B);
            }
            text = opaque;
        }
        // Clarté : vers le blanc sur un fond sombre, vers le noir sur un fond clair (le côté qui donne le plus de contraste).
        var c = ToOklch(text);
        bool lighten = Contrast(Colors.White, bg) >= Contrast(Colors.Black, bg);
        double from = c.L, to = lighten ? 1 : 0;
        var end = FromOklch(c with { L = to });
        if (Contrast(end, bg) < min) return lighten ? Colors.White : Colors.Black;
        double a = 0, b = 1;
        for (int i = 0; i < 20; i++)
        {
            double mid = (a + b) / 2;
            if (Contrast(FromOklch(c with { L = from + (to - from) * mid }), bg) >= min) b = mid; else a = mid;
        }
        return FromOklch(c with { L = from + (to - from) * b, A = 1 });
    }

    /// <summary>
    /// Voile le plus léger (opacité ≥ <paramref name="minimum"/>) à poser sur le décor pour qu'un texte reste lisible :
    /// surface = <paramref name="above"/>(voile sur décor). Recherche par dichotomie ; 1 si même un voile opaque ne suffit pas.
    /// </summary>
    public static double VeilFor(Color text, Color veil, Color backdrop, double minimum, double min, Func<Color, Color>? above = null)
    {
        above ??= c => c;
        Color Surface(double alpha) => Opaque(above(OpalTheme.Over(OpalPalette.WithOpacity(veil, alpha), Opaque(backdrop))));
        minimum = Math.Clamp(minimum, 0, 1);
        if (ContrastOn(text, Surface(minimum)) >= min) return minimum;
        if (ContrastOn(text, Surface(1)) < min) return 1;
        double a = minimum, b = 1;
        for (int i = 0; i < 18; i++)
        {
            double mid = (a + b) / 2;
            if (ContrastOn(text, Surface(mid)) >= min) b = mid; else a = mid;
        }
        return b;
    }

    /// <summary>Couleur opaque (une couleur translucide est supposée posée sur du noir… ou ignorée si elle est déjà opaque).</summary>
    public static Color Opaque(Color color) => color.A == 255 ? color : Color.FromRgb(color.R, color.G, color.B);

    // ------------------------------------------------------------------ thème sombre / clair

    /// <summary>
    /// Thème clair à cet instant ? Sombre / Clair = fixe ; Comme Windows = « mode des applis » de Windows ; Auto = clair entre
    /// le lever et le coucher du soleil (<paramref name="sun"/> null = coordonnées inconnues : on suit Windows).
    /// </summary>
    public static bool IsLight(OpalThemeMode mode, DateTime now, Services.SunCalculator.SunTimes? sun, bool windowsLight) => mode switch
    {
        OpalThemeMode.Light => true,
        OpalThemeMode.Dark => false,
        OpalThemeMode.Windows => windowsLight,
        _ => sun is not { } s ? windowsLight
            : s.Polar == Services.SunCalculator.Polar.AlwaysDay ? true
            : s.Polar == Services.SunCalculator.Polar.AlwaysNight ? false
            : now >= s.Sunrise && now < s.Sunset,
    };

    /// <summary>Prochain passage jour / nuit d'aujourd'hui après <paramref name="now"/> (sinon minuit : on recalcule demain).</summary>
    public static DateTime NextSwitch(DateTime now, Services.SunCalculator.SunTimes? sun)
    {
        var midnight = now.Date.AddDays(1);
        if (sun is not { Polar: Services.SunCalculator.Polar.None } s) return midnight;
        if (now < s.Sunrise) return s.Sunrise;
        if (now < s.Sunset) return s.Sunset;
        return midnight;
    }

    // ------------------------------------------------------------------ outils

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Gamma(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(Math.Max(c, 0), 1 / 2.4) - 0.055;
    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
