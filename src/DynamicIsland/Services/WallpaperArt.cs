using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>Les fonds d'écran du pack (dessins originaux générés par le code).</summary>
public enum WallpaperDesign { Aurore, Nebuleuse, Vagues, Prisme, Orbite }

/// <summary>
/// Dessine les fonds du pack, pixel par pixel (sur tous les cœurs), à n'importe quelle taille et dans n'importe
/// quelle ambiance (<see cref="Mood"/>). « Aurore » suit la même formule que le fond animé
/// (Themes/Shaders/AnimatedAurora.fx) : l'image fixe ressemble à une image de l'animation.
/// </summary>
public static class WallpaperArt
{
    /// <summary>Les 5 dessins et leur nom dans la langue de l'interface.</summary>
    public static (WallpaperDesign Design, string Name)[] Designs =>
    [
        (WallpaperDesign.Aurore, Loc.T("art.aurore")), (WallpaperDesign.Nebuleuse, Loc.T("art.nebuleuse")), (WallpaperDesign.Vagues, Loc.T("art.vagues")),
        (WallpaperDesign.Prisme, Loc.T("art.prisme")), (WallpaperDesign.Orbite, Loc.T("art.orbite")),
    ];

    public static string NameOf(WallpaperDesign design) => Designs.First(d => d.Design == design).Name;

    /// <param name="time">Instant de l'animation pour « Aurore » (secondes).</param>
    public static BitmapSource Render(WallpaperDesign design, Mood mood, int width, int height, double time = 0)
    {
        var palette = new Palette(mood);
        var pixels = new byte[width * height * 4];
        double aspect = (double)width / height;
        Parallel.For(0, height, y =>
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                // Repère centré, hauteur 1 : p.X de -aspect/2 à +aspect/2, p.Y de -0,5 (haut) à +0,5 (bas).
                double px = ((x + 0.5) / width - 0.5) * aspect, py = (y + 0.5) / height - 0.5;
                var color = design switch
                {
                    WallpaperDesign.Nebuleuse => Nebula(px, py, palette),
                    WallpaperDesign.Vagues => Waves(px, py, palette),
                    WallpaperDesign.Prisme => Prism(px, py, palette),
                    WallpaperDesign.Orbite => Orbit(px, py, palette),
                    _ => Aurora(px, py, palette, time),
                };
                color = Tone(color);
                double dither = (Hash(x, y) - 0.5) * 2 / 255; // évite les marches dans les dégradés sombres
                pixels[row + x * 4] = ToByte(color.B + dither);
                pixels[row + x * 4 + 1] = ToByte(color.G + dither);
                pixels[row + x * 4 + 2] = ToByte(color.R + dither);
                pixels[row + x * 4 + 3] = 255;
            }
        });
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    public static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Fond du pack (ambiance de nuit) à la taille de l'écran principal, dessiné une seule fois dans
    /// %APPDATA%\DynamicIsland\Fonds ; renvoie son chemin. À appeler hors du thread de l'interface.
    /// </summary>
    public static string RenderForScreen(WallpaperDesign design)
    {
        var (bounds, _, _) = ScreenInfo.Primary();
        int width = Math.Max(640, bounds.Right - bounds.Left), height = Math.Max(360, bounds.Bottom - bounds.Top);
        var path = Path.Combine(AppPaths.DataFolder, "Fonds", $"{design.ToString().ToLowerInvariant()}-{width}x{height}.png");
        if (!File.Exists(path)) SavePng(Render(design, Wallpapers.Night, width, height), path);
        return path;
    }

    // ------------------------------------------------------------------ dessins

    /// <summary>Rubans d'aurore boréale et leurs rideaux, sur un ciel de nuit (même formule que le shader).</summary>
    private static Rgb Aurora(double px, double py, Palette c, double time)
    {
        double t = time * Themes.Wallpapers.AnimationSpeed;
        var color = Rgb.Mix(c.Deep, c.Base * 0.8, SmoothStep(-0.6, 0.7, py));
        for (int i = 0; i < 3; i++)
        {
            double y = -0.22 + 0.07 * i + 0.06 * Math.Sin(px * 1.9 + i * 1.7 + t * 0.11) + 0.03 * Math.Sin(px * 4.7 + i * 0.9 - t * 0.17);
            double w = 0.045 + 0.025 * Math.Sin(px * 1.3 + i + t * 0.07);
            double d = py - y;
            double core = Math.Exp(-(d * d) / (w * w));
            // Rayons irréguliers (trois fréquences) qui pendent sous le ruban et s'éteignent vite.
            double rays = 0.55 + 0.25 * Math.Sin(px * 37 + Math.Sin(px * 5 + t * 0.3) * 2.5 + t * 0.4) + 0.2 * Math.Sin(px * 91 - t * 0.6 + i);
            double curtain = d > 0 ? Math.Exp(-d * 7) * rays : 0;
            var tint = Rgb.Mix(c.Accent1, c.Accent2, 0.5 + 0.5 * Math.Sin(px * 1.7 + i * 2 + t * 0.05));
            color += tint * ((core * 0.8 + curtain * 0.3) * (0.6 + 0.2 * i));
        }
        color += c.Base * (0.25 * SmoothStep(0.1, 0.5, py)); // lueur de l'horizon
        double gx = px - 0.3, gy = py + 0.35;
        color += c.Glow * (0.05 * Math.Exp(-(gx * gx + gy * gy) / 0.1));
        return color * (1 - 0.35 * (px * px + py * py));
    }

    /// <summary>Nuages de gaz colorés déformés par du bruit, poussière sombre et étoiles.</summary>
    private static Rgb Nebula(double px, double py, Palette c)
    {
        double qx = px + 0.18 * (Fbm(px * 2 + 5.2, py * 2 + 1.3) - 0.47);
        double qy = py + 0.18 * (Fbm(px * 2 + 9.7, py * 2 + 4.1) - 0.47);
        var color = c.Deep;
        color += Cloud(qx, qy, -0.45, -0.08, 0.55, 0.0) * c.Accent1 * 0.75;
        color += Cloud(qx, qy, 0.38, 0.12, 0.6, 3.0) * c.Accent2 * 0.8;
        color += Cloud(qx, qy, 0.6, -0.32, 0.42, 6.0) * c.Base * 0.9;
        color += Cloud(qx, qy, 0.05, -0.25, 0.3, 9.0) * c.Glow * 0.22;
        double dust = SmoothStep(0.45, 0.75, Fbm(qx * 3.5 + 20, qy * 3.5 + 20));
        color *= 1 - 0.55 * dust;
        color += c.Glow * Stars(px, py, 0.9965);
        return color * (1 - 0.3 * (px * px + py * py));

        static double Cloud(double x, double y, double cx, double cy, double radius, double seed)
        {
            double dx = x - cx, dy = y - cy;
            double shape = Math.Exp(-(dx * dx + dy * dy) / (radius * radius));
            return shape * (0.35 + 0.9 * Fbm(x * 3.2 + seed, y * 3.2 - seed));
        }
    }

    /// <summary>Collines / vagues en couches, crêtes éclairées, lueur derrière.</summary>
    private static Rgb Waves(double px, double py, Palette c)
    {
        var color = Rgb.Mix(c.Deep, c.Base * 1.2, SmoothStep(-0.5, 0.1, py));
        double sx = px + 0.45, sy = py - 0.02; // soleil bas, à gauche, derrière les vagues
        double sun = sx * sx + sy * sy;
        color += c.Accent2 * (0.6 * Math.Exp(-sun / 0.06)) + c.Glow * (0.5 * Math.Exp(-sun / 0.004));
        for (int k = 0; k < 5; k++)
        {
            double crest = -0.02 + 0.09 * k + 0.05 * Math.Sin(px * (1.5 + 0.3 * k) + k * 1.3) + 0.02 * Math.Sin(px * 4.1 + k * 2);
            double below = py - crest;
            if (below < -0.004) continue;
            double edge = SmoothStep(-0.004, 0.002, below);
            // Couches du fond plus claires et plus colorées, celles de devant plus sombres.
            var layer = Rgb.Mix(c.Accent1, c.Accent2, k / 4.0) * (0.55 - 0.08 * k) + c.Base * 0.2;
            var lit = layer * (0.5 + 0.8 * Math.Exp(-Math.Max(below, 0) * 12));
            lit += c.Glow * (0.3 * Math.Exp(-Math.Pow(below / 0.004, 2)));
            color = Rgb.Mix(color, lit, edge);
        }
        return color * (1 - 0.25 * (px * px + py * py));
    }

    /// <summary>Plans de lumière obliques qui se croisent (facettes de verre), arêtes brillantes.</summary>
    private static Rgb Prism(double px, double py, Palette c)
    {
        var color = Rgb.Mix(c.Deep, c.Base * 0.7, SmoothStep(-0.9, 0.9, px * 0.6 - py));
        (double Angle, double Offset, double Alpha, double Mix)[] planes =
        [
            (0.35, -0.25, 0.16, 0.0), (0.95, 0.05, 0.12, 0.6), (-0.6, 0.18, 0.14, 1.0),
            (2.2, -0.1, 0.10, 0.3), (1.4, 0.3, 0.09, 0.8), (-1.2, -0.35, 0.11, 0.45),
        ];
        foreach (var (angle, offset, alpha, mix) in planes)
        {
            double s = px * Math.Cos(angle) + py * Math.Sin(angle) - offset;
            if (s < -0.003) continue;
            double inside = SmoothStep(-0.003, 0.003, s);
            var tint = Rgb.Mix(c.Accent1, c.Accent2, mix);
            color += tint * (alpha * inside * Math.Exp(-Math.Max(s, 0) * 2.5));
            color += c.Glow * (0.12 * Math.Exp(-Math.Pow(s / 0.0025, 2)));
        }
        return color * (1 - 0.3 * (px * px + py * py));
    }

    /// <summary>Planète éclairée par le bord, atmosphère, anneaux d'orbite et étoiles.</summary>
    private static Rgb Orbit(double px, double py, Palette c)
    {
        double cx = 0.42, cy = 0.2, radius = 0.36;
        double dx = px - cx, dy = py - cy, d = Math.Sqrt(dx * dx + dy * dy);
        var color = Rgb.Mix(c.Deep, c.Base * 0.6, Math.Exp(-d * 1.6));
        color += c.Glow * Stars(px, py, 0.997);

        bool onPlanet = d < radius;
        if (onPlanet)
        {
            // Lumière venant d'en haut à gauche : face sombre, liseré lumineux.
            double light = Math.Clamp((-dx * 0.6 - dy * 0.8) / radius, 0, 1);
            var surface = Rgb.Mix(c.Deep, c.Base, 0.35 + 0.4 * light);
            surface += c.Accent1 * (0.5 * Math.Pow(SmoothStep(radius * 0.82, radius, d), 3) * (0.3 + 0.7 * light));
            color = Rgb.Mix(color, surface, SmoothStep(radius, radius - 0.003, d));
        }
        else
        {
            color += c.Accent2 * (0.55 * Math.Exp(-(d - radius) / 0.045));
        }

        // Anneaux : ellipses inclinées autour de la planète (masquées derrière elle).
        foreach (var (rx, ry, tilt, mix) in new[] { (0.62, 0.13, -0.25, 0.2), (0.85, 0.2, -0.25, 0.7), (1.15, 0.3, -0.22, 0.45) })
        {
            double ex = dx * Math.Cos(tilt) - dy * Math.Sin(tilt), ey = dx * Math.Sin(tilt) + dy * Math.Cos(tilt);
            double len = Math.Sqrt(ex * ex / (rx * rx) + ey * ey / (ry * ry));
            double distance = Math.Abs(len - 1) * Math.Min(rx, ry);
            if (onPlanet && ey < 0) continue; // moitié arrière cachée par la planète
            color += Rgb.Mix(c.Accent1, c.Accent2, mix) * (0.55 * Math.Exp(-Math.Pow(distance / 0.0018, 2)));
        }
        return color * (1 - 0.3 * (px * px + py * py));
    }

    // ------------------------------------------------------------------ outils

    private static double Stars(double px, double py, double rarity)
    {
        const double Cell = 0.004;
        int ix = (int)Math.Floor(px / Cell), iy = (int)Math.Floor(py / Cell);
        double h = Hash(ix + 7919, iy - 104729);
        if (h < rarity) return 0;
        double brightness = (h - rarity) / (1 - rarity);
        return 0.25 + 0.75 * brightness * brightness;
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    private static double Noise(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        double a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
    }

    private static double Fbm(double x, double y)
    {
        double sum = 0, amplitude = 0.5;
        for (int i = 0; i < 4; i++)
        {
            sum += amplitude * Noise(x, y);
            x = x * 2.03 + 17.1;
            y = y * 2.03 - 9.4;
            amplitude *= 0.5;
        }
        return sum;
    }

    /// <summary>Douce compression des lumières fortes (les zones sombres restent quasi identiques).</summary>
    private static Rgb Tone(Rgb c) => new(1 - Math.Exp(-c.R * 1.1), 1 - Math.Exp(-c.G * 1.1), 1 - Math.Exp(-c.B * 1.1));

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private readonly record struct Palette(Rgb Deep, Rgb Base, Rgb Accent1, Rgb Accent2, Rgb Glow)
    {
        public Palette(Mood mood) : this(Rgb.From(mood.Deep), Rgb.From(mood.Base), Rgb.From(mood.Accent1), Rgb.From(mood.Accent2), Rgb.From(mood.Glow)) { }
    }

    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb From(Color color) => new(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        public static Rgb Mix(Rgb a, Rgb b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
        public static Rgb operator *(Rgb a, double k) => new(a.R * k, a.G * k, a.B * k);
        public static Rgb operator *(double k, Rgb a) => a * k;
    }
}
