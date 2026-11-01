using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>
/// Calculs purs du « verre opalin » (refonte O1), testés par --self-test : teintes voisines de l'accent pour les reflets
/// irisés, grain (bruit déterministe), flou simulé du fond d'écran (copie réduite floutée), orientation des dégradés.
/// </summary>
public static class OpalPalette
{
    /// <summary>
    /// Trois teintes pâles autour de l'accent : teinte − décalage, accent, teinte + décalage (degrés, roue fermée), saturation
    /// réduite et luminosité remontée, puis mêlées de blanc (<paramref name="pale"/> de 0 à 1).
    /// </summary>
    /// <param name="light">Opale blanche (0 → 1) : teintes un peu plus soutenues et moins claires, sinon elles disparaissent sur du blanc.</param>
    public static Color[] Iridescent(Color accent, double shift, double pale, double light = 0)
    {
        var (h, s, v) = ColorTools.ToHsv(accent);
        var colors = new Color[3];
        light = Math.Clamp(light, 0, 1);
        for (int i = 0; i < 3; i++)
        {
            double hue = ColorTools.Wrap(h + (i - 1) * shift);
            double saturation = Math.Min(1, Math.Min(s, 0.85) * (1 - pale * 0.5) + 0.12 * light);
            var c = ColorTools.FromHsv(hue, saturation, Math.Max(v, 0.9) - 0.16 * light);
            colors[i] = Mix(c, Colors.White, pale);
        }
        return colors;
    }

    /// <summary>Grain : <paramref name="size"/>² pixels gris (BGRA, opaques), toujours les mêmes pour une même graine.</summary>
    public static byte[] Noise(int size, int seed)
    {
        var pixels = new byte[size * size * 4];
        uint state = (uint)seed * 2654435761u | 1; // xorshift32 : jamais 0
        for (int i = 0; i < size * size; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            byte gray = (byte)(state >> 24);
            pixels[i * 4] = gray;
            pixels[i * 4 + 1] = gray;
            pixels[i * 4 + 2] = gray;
            pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }

    /// <summary>Texture de grain prête à poser en mosaïque (gelée).</summary>
    public static BitmapSource NoiseImage()
    {
        var image = BitmapSource.Create(Opal.GrainSize, Opal.GrainSize, 96, 96, PixelFormats.Bgra32, null,
            Noise(Opal.GrainSize, Opal.GrainSeed), Opal.GrainSize * 4);
        image.Freeze();
        return image;
    }

    /// <summary>Points de départ et d'arrivée (relatifs, 0–1) d'un dégradé linéaire orienté à <paramref name="degrees"/>.</summary>
    public static (Point Start, Point End) Direction(double degrees)
    {
        double a = degrees * Math.PI / 180, dx = Math.Cos(a) / 2, dy = Math.Sin(a) / 2;
        return (new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
    }

    /// <summary>
    /// Flou simulé (la vraie île ne peut pas flouter ce qui est derrière elle) : l'image est réduite à
    /// <see cref="Opal.BlurWidth"/> px, saturée (<paramref name="vibrancy"/>), floutée en « boîte » plusieurs fois (≈ flou
    /// gaussien), puis WPF l'agrandit en lissant. À appeler hors du thread de l'interface ; résultat gelé.
    /// </summary>
    public static BitmapSource Blurred(BitmapSource source, double radius, double vibrancy)
    {
        double scale = (double)Opal.BlurWidth / source.PixelWidth;
        var small = new FormatConvertedBitmap(new TransformedBitmap(source, new ScaleTransform(scale, scale)), PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var pixels = new byte[w * h * 4];
        small.CopyPixels(pixels, w * 4, 0);
        Saturate(pixels, vibrancy);
        var buffer = new byte[pixels.Length];
        int r = Math.Max(1, (int)Math.Round(radius));
        for (int pass = 0; pass < Opal.BlurPasses; pass++)
        {
            BoxBlur(pixels, buffer, w, h, r, horizontal: true);
            BoxBlur(buffer, pixels, w, h, r, horizontal: false);
        }
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        image.Freeze();
        return image;
    }

    /// <summary>Saturation : chaque pixel s'éloigne (&gt; 1) ou se rapproche (&lt; 1) de son gris.</summary>
    internal static void Saturate(byte[] bgra, double amount)
    {
        if (Math.Abs(amount - 1) < 0.001) return;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            double b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            double gray = 0.299 * r + 0.587 * g + 0.114 * b;
            bgra[i] = Clamp(gray + (b - gray) * amount);
            bgra[i + 1] = Clamp(gray + (g - gray) * amount);
            bgra[i + 2] = Clamp(gray + (r - gray) * amount);
        }
    }

    /// <summary>Moyenne glissante sur 2r + 1 pixels (bords répétés), dans un sens.</summary>
    internal static void BoxBlur(byte[] from, byte[] to, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        double norm = 1.0 / (2 * r + 1);
        Parallel.For(0, lines, line =>
        {
            int Index(int k) => horizontal ? (line * w + Math.Clamp(k, 0, length - 1)) * 4 : (Math.Clamp(k, 0, length - 1) * w + line) * 4;
            for (int ch = 0; ch < 4; ch++)
            {
                double sum = 0;
                for (int k = -r; k <= r; k++) sum += from[Index(k) + ch];
                for (int k = 0; k < length; k++)
                {
                    to[Index(k) + ch] = Clamp(sum * norm);
                    sum += from[Index(k + r + 1) + ch] - from[Index(k - r) + ch];
                }
            }
        });
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
