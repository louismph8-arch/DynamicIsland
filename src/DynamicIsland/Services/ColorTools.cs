using System.Windows.Media;

namespace DynamicIsland.Services;

/// <summary>Conversions TSV / TSL des couleurs (teinte en degrés, le reste de 0 à 1).</summary>
public static class ColorTools
{
    public static (double H, double S, double V) ToHsv(Color color) => ToHsv(color.R, color.G, color.B);

    /// <param name="r">Rouge de 0 à 255 (moyennes comprises).</param>
    public static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        return (Hue(r, g, b, max, d), max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(Wrap(h) / 60 % 2 - 1)), m = v - c;
        return FromChroma(h, c, x, m);
    }

    public static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        return (Hue(r, g, b, max, d), s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(Wrap(h) / 60 % 2 - 1)), m = l - c / 2;
        return FromChroma(h, c, x, m);
    }

    /// <summary>Écart entre deux teintes sur le cercle (0 à 180°).</summary>
    public static double HueDistance(double a, double b)
    {
        double d = Math.Abs(Wrap(a) - Wrap(b));
        return Math.Min(d, 360 - d);
    }

    public static double Wrap(double h) => (h % 360 + 360) % 360;

    private static double Hue(double r, double g, double b, double max, double d) =>
        d == 0 ? 0
        : max == r ? 60 * (((g - b) / d + 6) % 6)
        : max == g ? 60 * ((b - r) / d + 2)
        : 60 * ((r - g) / d + 4);

    private static Color FromChroma(double h, double c, double x, double m)
    {
        var (r, g, b) = Wrap(h) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
