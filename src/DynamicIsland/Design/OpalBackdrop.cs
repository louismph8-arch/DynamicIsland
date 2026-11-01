using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Design;

/// <summary>
/// « Flou » du verre opalin (O1) : une fenêtre « layered » ne peut pas flouter ce qu'il y a derrière elle (3B / 5B). On
/// floute donc une COPIE du fond d'écran (réduite à 256 px, hors du thread de l'interface) et l'île / le dock flottant en
/// affichent le morceau qui est derrière eux. Refaite seulement quand le fond change (message de Windows, ou date du fichier
/// vérifiée une fois par minute pour les diaporamas et Wallpaper Engine) ou quand l'intensité change.
/// </summary>
public static class OpalBackdrop
{
    private static readonly DispatcherTimer Poll = new() { Interval = Opal.BackdropCheck };
    private static (string Path, DateTime Written, long Length, OpalStyle Style)? _seen;
    private static bool _started, _busy, _again;
    private static TaskCompletionSource _ready = new();

    /// <summary>--snapshot (et mesures) : un fond du pack, jamais le vrai fond de l'utilisateur.</summary>
    public static bool SnapshotMode { get; set; }

    /// <summary>Copie floutée du fond d'écran (null : couleur unie, fond illisible, pas encore prête).</summary>
    public static BitmapSource? Image { get; private set; }

    public static event Action? Changed;

    /// <summary>Première surface qui en a besoin : on commence à suivre le fond d'écran.</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        Poll.Tick += (_, _) => Refresh();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop) Guarded.OnUi(Refresh, "Verre opalin : fond d'écran");
        };
        OpalTheme.Changed += Refresh; // intensité changée : flou différent
        if (!SnapshotMode) Poll.Start();
        Refresh();
    }

    /// <summary>--snapshot : attend la première copie floutée (au plus 5 s).</summary>
    public static Task WhenReady() => Task.WhenAny(_ready.Task, Task.Delay(5000));

    public static void Refresh()
    {
        if (_busy)
        {
            _again = true;
            return;
        }
        var style = SettingsStore.Current.Appearance.OpalStyle;
        string? path = SnapshotMode ? null : WallpaperService.CurrentPath();
        (string, DateTime, long, OpalStyle) stamp;
        try
        {
            var info = path != null ? new FileInfo(path) : null;
            stamp = (path ?? "", info?.LastWriteTimeUtc ?? default, info?.Exists == true ? info.Length : 0, style);
        }
        catch (Exception)
        {
            stamp = (path ?? "", default, 0, style);
        }
        if (_seen == stamp) return;
        _seen = stamp;
        if (path == null && !SnapshotMode)
        {
            Publish(null); // couleur unie : pas de flou, la surface garde son voile
            return;
        }
        _busy = true;
        var look = Opal.For(style);
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() =>
        {
            var blurred = OpalPalette.Blurred(Load(path), look.Blur, look.Vibrancy);
            return (blurred, Brightness(blurred));
        }).ContinueWith(task => dispatcher.BeginInvoke(() =>
        {
            _busy = false;
            if (task.Exception != null)
            {
                Log.Info($"Verre opalin : fond d'écran illisible pour le flou ({(task.Exception.InnerException ?? task.Exception).Message})");
                Publish(null);
            }
            else
            {
                Luminance = task.Result.Item2;
                Publish(task.Result.blurred);
            }
            if (_again)
            {
                _again = false;
                Refresh();
            }
        }));
    }

    /// <summary>Luminosité moyenne (0–1) de la copie floutée : un fond clair demande un voile plus sombre (texte blanc lisible).</summary>
    public static double Luminance { get; private set; }

    /// <summary>Voile sombre à poser sur la copie floutée : celui de l'intensité, renforcé si le fond d'écran est clair.</summary>
    public static double SmokeFor(OpalLook look)
    {
        double boost = Math.Clamp((Luminance - Opal.BrightFrom) / Opal.BrightRange, 0, 1) * Opal.BrightSmoke;
        return look.Smoke + (1 - look.Smoke) * boost;
    }

    private static double Brightness(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        double sum = 0;
        for (int i = 0; i < pixels.Length; i += 4) sum += 0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2];
        return pixels.Length == 0 ? 0 : sum / (pixels.Length / 4) / 255;
    }

    private static void Publish(BitmapSource? image)
    {
        Image = image;
        _ready.TrySetResult();
        Changed?.Invoke();
    }

    /// <summary>Image à flouter (hors du thread de l'interface), lue directement en petit.</summary>
    private static BitmapSource Load(string? path)
    {
        if (path == null) return WallpaperArt.Render(WallpaperDesign.Prisme, Wallpapers.Sunset, 1280, 720);
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = Opal.BackdropDecodeWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
