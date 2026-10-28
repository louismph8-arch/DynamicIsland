using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>Couleurs tirées d'un fond d'écran : dégradé d'accent + teinte sombre pour l'île et le dock.</summary>
public sealed record WallpaperColors(Color Start, Color End, Color Surface);

/// <summary>
/// Option « Couleurs du fond d'écran » : relit le fond de Windows quand il change (message de Windows + vérification
/// toutes les 30 s pour les diaporamas et Wallpaper Engine, qui remplace l'image), en extrait la palette hors du
/// thread de l'interface et la range dans les réglages (<c>Appearance.WallpaperAccent*</c>, gardée au redémarrage).
/// </summary>
public static class WallpaperPalette
{
    private static readonly DispatcherTimer Poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private static (string Path, DateTime Written, long Length)? _seen;
    private static bool _busy;

    public static void Start()
    {
        Poll.Tick += (_, _) => Refresh();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop) Refresh();
        };
        SettingsStore.Changed += OnSettingsChanged;
        OnSettingsChanged();
    }

    private static void OnSettingsChanged()
    {
        bool on = SettingsStore.Current.Appearance.AccentFromWallpaper && !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Colors);
        if (on == Poll.IsEnabled) return;
        if (on)
        {
            Poll.Start();
            Refresh();
        }
        else
        {
            Poll.Stop();
            _seen = null; // réactivé plus tard : on relit tout de suite
        }
    }

    /// <summary>Relit le fond d'écran s'il a changé (chemin, date ou taille du fichier).</summary>
    public static void Refresh()
    {
        if (!Poll.IsEnabled || _busy) return;
        var path = WallpaperService.CurrentPath();
        if (path == null) return; // couleur unie : on garde les dernières couleurs
        var info = new FileInfo(path);
        var stamp = (path, info.LastWriteTimeUtc, info.Length);
        if (_seen == stamp) return;
        _seen = stamp;
        _busy = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => Extract(path)).ContinueWith(task => dispatcher.BeginInvoke(() =>
        {
            _busy = false;
            if (task.Exception != null) Log.Error("Couleurs du fond d'écran", task.Exception.InnerException ?? task.Exception);
            else if (task.Result is { } colors) Store(colors);
        }));
    }

    private static void Store(WallpaperColors colors)
    {
        var look = SettingsStore.Current.Appearance;
        string start = Swatch.Hex(colors.Start), end = Swatch.Hex(colors.End), surface = Swatch.Hex(colors.Surface);
        if (look.WallpaperAccentStart == start && look.WallpaperAccentEnd == end && look.WallpaperSurface == surface) return;
        SettingsStore.Update(s =>
        {
            s.Appearance.WallpaperAccentStart = start;
            s.Appearance.WallpaperAccentEnd = end;
            s.Appearance.WallpaperSurface = surface;
        });
    }

    /// <summary>Palette d'une image ; null si elle n'a presque pas de couleur (fond gris, noir et blanc).</summary>
    public static WallpaperColors? Extract(string path)
    {
        var pixels = ReadPixels(path);
        var weight = new double[AutoColor.HueBins];
        var red = new double[AutoColor.HueBins];
        var green = new double[AutoColor.HueBins];
        var blue = new double[AutoColor.HueBins];
        int colorful = 0, counted = 0;

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            counted++;
            var (h, s, v) = ColorTools.ToHsv(r, g, b);
            if (s < AutoColor.MinSaturation || v < AutoColor.MinValue) continue;
            colorful++;
            int bin = (int)(h / 360 * AutoColor.HueBins) % AutoColor.HueBins;
            double w = s * s * v; // les couleurs vives et lumineuses comptent le plus
            weight[bin] += w;
            red[bin] += r * w;
            green[bin] += g * w;
            blue[bin] += b * w;
        }
        if (counted == 0 || (double)colorful / counted < AutoColor.MinColorfulShare) return null;

        // Chaque case compte avec la moitié de ses voisines : une teinte à cheval sur deux cases n'est pas coupée.
        double Score(int bin) => weight[Wrap(bin - 1)] * 0.5 + weight[bin] + weight[Wrap(bin + 1)] * 0.5;
        int first = Enumerable.Range(0, AutoColor.HueBins).MaxBy(Score);
        var main = Average(first);

        int gap = (int)Math.Ceiling(AutoColor.SecondHueGap / (360.0 / AutoColor.HueBins));
        var others = Enumerable.Range(0, AutoColor.HueBins)
            .Where(bin => Math.Min(Math.Abs(bin - first), AutoColor.HueBins - Math.Abs(bin - first)) >= gap).ToList();
        int? second = others.Count > 0 ? others.MaxBy(Score) : null;
        Color end;
        if (second is int other && Score(other) >= AutoColor.SecondMinShare * Score(first))
        {
            end = Accent(Average(other));
        }
        else
        {
            // Une seule teinte : dégradé vers sa voisine, du côté où l'image a le plus de couleur.
            var (h, s, v) = ColorTools.ToHsv(main);
            int side = Score(Wrap(first + 1)) >= Score(Wrap(first - 1)) ? 1 : -1;
            end = Accent(ColorTools.FromHsv(h + side * AutoColor.AnalogousShift, s, v));
        }

        var (mainHue, _, _) = ColorTools.ToHsv(main);
        return new WallpaperColors(Accent(main), end, ColorTools.FromHsv(mainHue, AutoColor.SurfaceSaturation, AutoColor.SurfaceValue));

        Color Average(int center)
        {
            double r = 0, g = 0, b = 0, w = 0;
            foreach (int bin in new[] { Wrap(center - 1), center, Wrap(center + 1) })
            {
                double share = bin == center ? 1 : 0.5;
                r += red[bin] * share;
                g += green[bin] * share;
                b += blue[bin] * share;
                w += weight[bin] * share;
            }
            return w <= 0 ? Swatch.AccentStart : Color.FromRgb((byte)(r / w), (byte)(g / w), (byte)(b / w));
        }
    }

    /// <summary>Couleur d'accent lisible sur l'île sombre : assez saturée et lumineuse.</summary>
    private static Color Accent(Color color)
    {
        var (h, s, v) = ColorTools.ToHsv(color);
        return ColorTools.FromHsv(h, Math.Clamp(s * 1.1, AutoColor.AccentMinSaturation, AutoColor.AccentMaxSaturation),
            Math.Max(v, AutoColor.AccentMinValue));
    }

    private static int Wrap(int bin) => (bin % AutoColor.HueBins + AutoColor.HueBins) % AutoColor.HueBins;

    /// <summary>Image réduite en BGRA ; lue sans verrouiller le fichier (Wallpaper Engine le réécrit).</summary>
    private static byte[] ReadPixels(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.DecodePixelWidth = AutoColor.SampleWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();

        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
