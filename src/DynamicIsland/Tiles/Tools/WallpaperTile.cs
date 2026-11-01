using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Miniature du fond d'écran affiché (tuile Fond d'écran) : image réduite une seule fois, hors du fil de l'interface, puis
/// gardée. Le chemin et la date du fichier sont revérifiés (en arrière-plan) au plus toutes les <see cref="Recheck"/>, seulement
/// quand la tuile est affichée : un fond changé ailleurs (Windows, profil) suit tout seul.
/// </summary>
internal static class WallpaperThumbnail
{
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);
    private static string? _path;
    private static DateTime _stamp;
    private static DateTime _checked = DateTime.MinValue;
    private static bool _busy;
    private static bool _demo;

    public static ImageSource? Image { get; private set; }

    private sealed record Found(string? Path, DateTime Stamp, ImageSource? Image, bool Same);

    public static void Check(bool force = false)
    {
        if (_demo || _busy || !force && DateTime.UtcNow - _checked < Recheck) return;
        _busy = true;
        _checked = DateTime.UtcNow;
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        string? known = _path;
        var knownStamp = _stamp;
        _ = Task.Run(() =>
        {
            var path = WallpaperService.CurrentPath();
            if (path == null) return new Found(null, DateTime.MinValue, null, known == null);
            var stamp = File.GetLastWriteTimeUtc(path);
            if (string.Equals(path, known, StringComparison.OrdinalIgnoreCase) && stamp == knownStamp) return new Found(path, stamp, null, true);
            return new Found(path, stamp, Load(path), false);
        }).ContinueWith(task => ui.BeginInvoke(() =>
        {
            _busy = false;
            if (!task.IsCompletedSuccessfully)
            {
                if (task.Exception != null) Log.Error("Tuile Fond d'écran : miniature", task.Exception);
                return;
            }
            var result = task.Result;
            if (result.Same) return;
            _path = result.Path;
            _stamp = result.Stamp;
            Image = result.Image;
        }), TaskScheduler.Default);
    }

    /// <summary>Image décodée directement à la taille de la miniature (un fond 4K ne coûte pas 30 Mo).</summary>
    internal static ImageSource? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = ToolTileShape.ThumbWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null; // format illisible : la tuile garde son verre
        }
    }

    /// <summary>Rendus --snapshot : une image d'exemple (fond du pack), le vrai fond n'est jamais lu.</summary>
    internal static void UseDemo(ImageSource? image)
    {
        _demo = image != null;
        Image = image;
        _path = null;
    }
}

/// <summary>
/// Tuile Fond d'écran : la miniature du fond actuel remplit la tuile (voile sombre en bas pour lire le nom), clic = fond suivant
/// du pack Opaline pour le profil actif (comme « fond Opaline suivant » de la télécommande), clic droit = page Fond d'écran.
/// </summary>
internal sealed class WallpaperFace : ToolFace
{
    private bool _preparing;
    private ImageSource? _shown;
    private bool _hasShown;

    public WallpaperFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context) { }

    /// <summary>Clic : fond suivant (seulement si le fond dynamique n'a pas la main ; sinon sa page, pour le couper).</summary>
    public void Next()
    {
        if (SettingsStore.Current.Wallpaper.Dynamic)
        {
            SettingsWindow.Open(SettingsPage.Wallpaper);
            return;
        }
        if (_preparing) return;
        _preparing = true;
        WallpaperService.NextPackWallpaper(_ =>
        {
            _preparing = false;
            WallpaperThumbnail.Check(force: true);
            Refresh();
        });
    }

    protected override void Update()
    {
        WallpaperThumbnail.Check();
        var image = WallpaperThumbnail.Image;
        if (!_hasShown || !ReferenceEquals(image, _shown)) ShowImage(image);
        var wallpaper = SettingsStore.Current.Wallpaper;
        string line;
        if (_preparing) line = Loc.T("tiles.wallpaper.preparing");
        else if (wallpaper.Dynamic) line = Loc.T("tiles.wallpaper.dynamic");
        else
        {
            var designs = Enum.GetValues<WallpaperDesign>();
            var next = designs[(Array.IndexOf(designs, wallpaper.Design) + 1) % designs.Length];
            line = Loc.F("tiles.wallpaper.next", Loc.T("art." + next.ToString().ToLowerInvariant()));
        }
        SetTone(_preparing ? Icons.NextImage : Icons.Wallpaper, _preparing ? ToolTone.Accent : ToolTone.Idle);
        SetTexts(null, line);
    }

    /// <summary>La miniature en fond de la tuile (recadrée pour la remplir) + un voile sombre en bas ; sans image : le verre.</summary>
    private void ShowImage(ImageSource? image)
    {
        _hasShown = true;
        _shown = image;
        if (image == null)
        {
            SetResourceReference(BackgroundProperty, OpalTheme.TileKey);
            SetOnImage(false);
            return;
        }
        var (columns, rows) = TileSize.Span();
        double width = columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap;
        double height = rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap;
        var area = new Rect(0, 0, width, height);
        double scale = Math.Max(width / Math.Max(1, image.Width), height / Math.Max(1, image.Height));
        var picture = new Rect((width - image.Width * scale) / 2, (height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(area) };
        group.Children.Add(new ImageDrawing(image, picture));
        var scrim = new LinearGradientBrush(ToolTileShape.ScrimTop, ToolTileShape.ScrimBottom, 90);
        scrim.GradientStops[0].Offset = ToolTileShape.ScrimStart;
        group.Children.Add(new GeometryDrawing(scrim, null, new RectangleGeometry(area)));
        group.Freeze();
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        Background = brush;
        SetOnImage(true);
    }
}
