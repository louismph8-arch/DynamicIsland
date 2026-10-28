using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Themes;

/// <summary>
/// Logo d'Opaline (5H), calculé pixel par pixel (sur-échantillonné) pour rester net de 16 à 1024 px : tuile squircle
/// sombre, anneau d'opale (reflets bleu → lavande → rose → pêche → menthe, voilés de blanc laiteux), modelé comme un
/// tube de verre avec un reflet en haut à gauche, et un léger halo. Sert à l'icône de l'app (<c>--render-logo</c>
/// écrit app.ico) et aux grands logos de l'interface (assistant de bienvenue).
/// </summary>
public static class OpalineLogo
{
    private static readonly Dictionary<int, BitmapSource> Cache = [];

    /// <summary>Logo à cette taille (gardé en mémoire : il ne change jamais).</summary>
    public static BitmapSource Get(int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(size, out var cached)) return cached;
        }
        var image = Render(size);
        lock (Cache) Cache[size] = image;
        return image;
    }

    public static BitmapSource Render(int size)
    {
        int samples = LogoArt.Supersampling;
        var pixels = new byte[size * size * 4];
        double tileHalf = 0.5 - LogoArt.TileMargin;
        double tileLimit = Math.Pow(tileHalf, LogoArt.TileExponent);

        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0; // pré-multiplié, moyenne des échantillons
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double x = (px + (sx + 0.5) / samples) / size - 0.5;
                        double y = (py + (sy + 0.5) / samples) / size - 0.5;
                        var (cr, cg, cb, ca) = Sample(x, y, tileLimit, tileHalf);
                        r += cr; g += cg; b += cb; a += ca;
                    }
                }
                int count = samples * samples;
                int i = (py * size + px) * 4;
                pixels[i] = ToByte(b / count);
                pixels[i + 1] = ToByte(g / count);
                pixels[i + 2] = ToByte(r / count);
                pixels[i + 3] = ToByte(a / count);
            }
        });

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Couleur pré-multipliée (0–1) d'un point, coordonnées centrées (−0,5 à 0,5).</summary>
    private static (double R, double G, double B, double A) Sample(double x, double y, double tileLimit, double tileHalf)
    {
        double r = 0, g = 0, b = 0, a = 0;

        // Tuile : superellipse, dégradé vertical sombre.
        if (Math.Pow(Math.Abs(x), LogoArt.TileExponent) + Math.Pow(Math.Abs(y), LogoArt.TileExponent) <= tileLimit)
        {
            double t = (y + tileHalf) / (2 * tileHalf);
            var top = LogoArt.TileTop;
            var bottom = LogoArt.TileBottom;
            r = Lerp(top.R, bottom.R, t) / 255;
            g = Lerp(top.G, bottom.G, t) / 255;
            b = Lerp(top.B, bottom.B, t) / 255;
            a = 1;
        }

        double distance = Math.Sqrt(x * x + y * y);
        double turn = (Math.Atan2(x, -y) / (2 * Math.PI) + 1) % 1; // 0 en haut, sens des aiguilles d'une montre
        var (or, og, ob) = OpalAt(turn);

        if (distance >= LogoArt.RingInner && distance <= LogoArt.RingOuter)
        {
            // Anneau : tube de verre (plus clair au milieu de son épaisseur) + reflet vers la lumière.
            double across = (distance - LogoArt.RingInner) / (LogoArt.RingOuter - LogoArt.RingInner);
            double shade = 0.74 + 0.34 * Math.Sin(Math.PI * across);
            double gap = Math.Abs(turn - LogoArt.LightAngle);
            gap = Math.Min(gap, 1 - gap);
            double spark = Math.Exp(-gap * gap / (2 * 0.07 * 0.07)) * Math.Exp(-Math.Pow(across - 0.62, 2) / (2 * 0.13 * 0.13));
            r = Math.Min(1, or * shade + 0.62 * spark);
            g = Math.Min(1, og * shade + 0.62 * spark);
            b = Math.Min(1, ob * shade + 0.62 * spark);
            return (r, g, b, 1);
        }

        // Halo de la couleur de l'anneau, et lueur laiteuse au centre.
        double away = distance < LogoArt.RingInner ? LogoArt.RingInner - distance : distance - LogoArt.RingOuter;
        double glow = LogoArt.GlowStrength * Math.Exp(-away / LogoArt.GlowReach);
        if (distance < LogoArt.RingInner) glow += 0.12 * (1 - distance / LogoArt.RingInner);
        glow = Math.Min(glow, 1);
        // « over » : halo par-dessus la tuile (ou par-dessus le transparent hors de la tuile).
        r = or * glow + r * (1 - glow);
        g = og * glow + g * (1 - glow);
        b = ob * glow + b * (1 - glow);
        a = glow + a * (1 - glow);
        return (r, g, b, a);
    }

    /// <summary>Reflet d'opale à cette fraction de tour (dégradé cyclique), voilé de blanc laiteux.</summary>
    private static (double R, double G, double B) OpalAt(double turn)
    {
        var stops = LogoArt.Opal;
        double position = turn * stops.Length;
        int index = (int)Math.Floor(position) % stops.Length;
        double t = position - Math.Floor(position);
        t = t * t * (3 - 2 * t); // transitions douces
        var from = stops[index];
        var to = stops[(index + 1) % stops.Length];
        double milk = LogoArt.Milk;
        return (Lerp(Lerp(from.R, to.R, t), 255, milk) / 255, Lerp(Lerp(from.G, to.G, t), 255, milk) / 255,
            Lerp(Lerp(from.B, to.B, t), 255, milk) / 255);
    }

    /// <summary>Fichier .ico de l'app : une image par taille Windows (PNG pour 256 px, BMP 32 bits sinon).</summary>
    public static void WriteIcon(string path)
    {
        var images = LogoArt.IconSizes.Select(size => (Size: size, Data: size >= 256 ? Png(Render(size)) : Services.CursorTheme.Dib(Render(size)))).ToList();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);           // réservé
        writer.Write((ushort)1);           // type : icône
        writer.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);         // couleurs de palette
            writer.Write((byte)0);
            writer.Write((ushort)1);       // plans
            writer.Write((ushort)32);      // bits par pixel
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) writer.Write(data);
    }

    /// <summary>
    /// Images de l'installeur Inno Setup (BMP 24 bits, à 100, 150 et 200 %) : la grande image de gauche (dégradé sombre,
    /// logo, nom) et la petite en haut à droite (logo sur blanc, le fond de l'en-tête).
    /// </summary>
    public static void WriteInstallerImages(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (scale, suffix) in new[] { (1.0, "100"), (1.5, "150"), (2.0, "200") })
        {
            SaveBmp(WizardImage((int)(LogoArt.WizardWidth * scale), (int)(LogoArt.WizardHeight * scale)), Path.Combine(folder, $"opaline-grande-{suffix}.bmp"));
            SaveBmp(SmallImage((int)(LogoArt.WizardSmall * scale)), Path.Combine(folder, $"opaline-petite-{suffix}.bmp"));
        }
    }

    private static BitmapSource WizardImage(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(LogoArt.TileTop, LogoArt.TileBottom, 90), null, new System.Windows.Rect(0, 0, width, height));
            int logo = (int)(width * 0.62);
            dc.DrawImage(Render(logo), new System.Windows.Rect((width - logo) / 2.0, height * 0.36 - logo / 2.0, logo, logo));
            var name = new FormattedText(AppInfo.Name, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), System.Windows.FlowDirection.LeftToRight,
                new Typeface(FontStack.Display, System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold, System.Windows.FontStretches.Normal),
                width * 0.15, Brushes.White, 1.0);
            dc.DrawText(name, new System.Windows.Point((width - name.Width) / 2, height * 0.62));
        }
        return Flatten(visual, width, height);
    }

    private static BitmapSource SmallImage(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, size, size));
            dc.DrawImage(Render(size), new System.Windows.Rect(0, 0, size, size));
        }
        return Flatten(visual, size, size);
    }

    /// <summary>Rendu opaque en 24 bits (format attendu par Inno Setup).</summary>
    private static BitmapSource Flatten(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        opaque.Freeze();
        return opaque;
    }

    private static void SaveBmp(BitmapSource image, string path)
    {
        using var stream = File.Create(path);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    public static byte[] Png(BitmapSource image)
    {
        using var memory = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(memory);
        return memory.ToArray();
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
