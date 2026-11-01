using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using Drawing = System.Drawing;

namespace DynamicIsland.Tiles.Tools;

/// <summary>Couleurs choisies à l'écran (tuile Couleur) : 10 au plus, la plus récente d'abord, en mémoire seulement.</summary>
internal static class ColorHistory
{
    private static readonly List<Color> Items = [];

    public static IReadOnlyList<Color> All => Items;

    /// <summary>Change à chaque ajout : la tuile ne refait ses pastilles que dans ce cas.</summary>
    public static int Revision { get; private set; }

    public static void Add(Color color)
    {
        color.A = 0xFF;
        Items.Remove(color);
        Items.Insert(0, color);
        if (Items.Count > ToolTileShape.HistoryKept) Items.RemoveRange(ToolTileShape.HistoryKept, Items.Count - ToolTileShape.HistoryKept);
        Revision++;
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string Rgb(Color color) => $"rgb({color.R}, {color.G}, {color.B})";

    /// <summary>Copie le code HEX (« #3A7BFF ») et remet la couleur en tête ; faux si le presse-papiers est pris.</summary>
    public static bool Copy(Color color)
    {
        try
        {
            Clipboard.SetText(Hex(color));
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Error("Tuile Couleur : copie", ex);
            return false;
        }
        Add(color);
        return true;
    }

    /// <summary>Rendus --snapshot et auto-test : couleurs d'exemple (plus anciennes d'abord).</summary>
    internal static void UseDemo(params Color[] colors)
    {
        Items.Clear();
        foreach (var color in colors) Add(color);
    }
}

/// <summary>L'écran figé sous la souris : pixels BGRA (pour lire une couleur) et image à afficher.</summary>
internal sealed record ColorScreenShot(int Width, int Height, int Stride, byte[] Pixels, BitmapSource Image)
{
    public Color At(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = y * Stride + x * 4;
        return Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    /// <summary>Copie GDI de l'écran (pixels réels) : une seule image, au clic, jamais écrite sur le disque.</summary>
    public static ColorScreenShot Capture(NativeMethods.RECT bounds)
    {
        int width = Math.Max(1, bounds.Right - bounds.Left), height = Math.Max(1, bounds.Bottom - bounds.Top);
        using var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Drawing.Size(width, height), Drawing.CopyPixelOperation.SourceCopy);
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height), Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return FromPixels(width, height, data.Stride, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static ColorScreenShot FromPixels(int width, int height, int stride, byte[] pixels)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        image.Freeze();
        return new ColorScreenShot(width, height, stride, pixels, image);
    }

    /// <summary>Rendus --snapshot : une image déjà dessinée (fond du pack) à la place de l'écran.</summary>
    public static ColorScreenShot FromImage(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return FromPixels(converted.PixelWidth, converted.PixelHeight, stride, pixels);
    }
}

/// <summary>
/// Pipette (tuile Couleur) : l'île se referme, l'écran sous la souris est figé dans une fenêtre plein écran (pas de crochet
/// global : c'est notre fenêtre qui reçoit la souris), une loupe suit le curseur ; clic = la couleur, son HEX est copié et
/// gardé dans la tuile ; Échap, clic droit ou une autre fenêtre = annuler. Rien n'est enregistré sur le disque.
/// </summary>
internal static class ColorPicker
{
    private static ColorPickerWindow? _window;

    public static bool IsOpen => _window != null;

    public static void Start(TileContext context)
    {
        if (_window != null) return;
        context.CollapseIsland(); // l'île ouverte ne doit pas être sur l'image
        var timer = new DispatcherTimer { Interval = ToolTiles.CaptureDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Open(context);
        };
        timer.Start();
    }

    private static void Open(TileContext context)
    {
        if (_window != null) return;
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = ScreenInfo.At(cursor);
        ColorScreenShot shot;
        try
        {
            shot = ColorScreenShot.Capture(monitor.Bounds);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        {
            Log.Error("Tuile Couleur : capture de l'écran", ex);
            context.Notice(Icons.Alert, Loc.T("tiles.color.error"), null);
            return;
        }
        var window = new ColorPickerWindow(shot, monitor.Bounds, cursor);
        window.Picked += color =>
        {
            if (ColorHistory.Copy(color)) context.Notice(Icons.Pipette, Loc.F("tiles.color.copied", ColorHistory.Hex(color)), null);
        };
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        window.Activate();
    }
}

/// <summary>Le contenu de la pipette : l'écran figé, la loupe (pixels agrandis, pixel visé encadré) et l'étiquette HEX.</summary>
internal sealed class ColorPickerSurface : Grid
{
    private readonly ColorScreenShot _shot;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Grid _loupe = new() { Width = ToolTileShape.Loupe, Height = ToolTileShape.Loupe };
    private readonly Image _zoom = new() { Stretch = Stretch.Fill };
    private readonly Canvas _zoomCanvas = new() { ClipToBounds = true };
    private readonly Rectangle _reticle = new() { Width = ToolTileShape.LoupeCell, Height = ToolTileShape.LoupeCell, StrokeThickness = Stroke.Hairline };
    private readonly SquircleBorder _label = new() { CornerRadius = Corners.Pill, Padding = new Thickness(Space.S4, Space.S2, Space.S5, Space.S2), BorderThickness = Borders.Hairline };
    private readonly SquircleBorder _labelSwatch = new() { Width = IconSize.M, Height = IconSize.M, CornerRadius = new CornerRadius(IconSize.M / 2), BorderThickness = Borders.Hairline, Margin = new Thickness(0, 0, Space.S3, 0) };
    private readonly TextBlock _labelText = new() { FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public Color Current { get; private set; }

    public ColorPickerSurface(ColorScreenShot shot)
    {
        _shot = shot;
        var screen = new Image { Source = shot.Image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(screen, BitmapScalingMode.NearestNeighbor);
        Children.Add(screen);
        Children.Add(_overlay);

        // Loupe ronde : pixels agrandis sans flou, liseré blanc, ombre légère, pixel visé encadré (blanc + noir : visible partout).
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        _zoom.Width = _zoom.Height = ToolTileShape.Loupe;
        _zoomCanvas.Children.Add(_zoom);
        _reticle.Stroke = Paint.Frozen(Swatch.White);
        var reticleShade = new Rectangle { Width = ToolTileShape.LoupeCell + 2 * Stroke.Hairline, Height = ToolTileShape.LoupeCell + 2 * Stroke.Hairline, StrokeThickness = Stroke.Hairline, Stroke = Paint.Frozen(ToolTileShape.LoupeShade) };
        _zoomCanvas.Children.Add(reticleShade);
        _zoomCanvas.Children.Add(_reticle);
        _reticle.Tag = reticleShade;
        var lens = new SquircleBorder
        {
            CornerRadius = new CornerRadius(ToolTileShape.Loupe / 2),
            BorderThickness = new Thickness(ToolTileShape.LoupeRimWidth),
            BorderBrush = Paint.Frozen(ToolTileShape.LoupeRim),
            Child = _zoomCanvas,
        };
        _loupe.Children.Add(lens);
        _loupe.Effect = Shadows.Window.Create();
        _overlay.Children.Add(_loupe);

        // Étiquette sous la loupe : pastille de la couleur + HEX, en verre opalin (comme les menus).
        _label.SetResourceReference(Border.BackgroundProperty, OpalTheme.PopupKey);
        _label.SetResourceReference(Border.BorderBrushProperty, OpalTheme.PopupRimKey);
        _labelSwatch.BorderBrush = Paint.Frozen(ToolTileShape.LoupeShade);
        _labelText.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_labelSwatch);
        row.Children.Add(_labelText);
        _label.Child = row;
        _overlay.Children.Add(_label);

        // Aide en haut au centre.
        var hint = new SquircleBorder { CornerRadius = Corners.Pill, Padding = new Thickness(Space.S6, Space.S3, Space.S6, Space.S3), BorderThickness = Borders.Hairline, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Space.S12, 0, 0), IsHitTestVisible = false };
        hint.SetResourceReference(Border.BackgroundProperty, OpalTheme.PopupKey);
        hint.SetResourceReference(Border.BorderBrushProperty, OpalTheme.PopupRimKey);
        var hintText = new TextBlock { Text = Loc.T("tiles.color.hint"), FontSize = TypeRamp.Footnote };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
        hint.Child = hintText;
        Children.Add(hint);
    }

    /// <summary>Pixel de l'écran sous ce point (en DIP de la surface).</summary>
    public (int X, int Y) PixelAt(Point point)
    {
        double width = Math.Max(1, ActualWidth), height = Math.Max(1, ActualHeight);
        return (Math.Clamp((int)(point.X * _shot.Width / width), 0, _shot.Width - 1), Math.Clamp((int)(point.Y * _shot.Height / height), 0, _shot.Height - 1));
    }

    /// <summary>La loupe suit ce point : pixels autour, pixel visé encadré, étiquette HEX ; elle passe de l'autre côté près d'un bord.</summary>
    public void MoveTo(Point point)
    {
        var (px, py) = PixelAt(point);
        int n = Math.Min(ToolTileShape.LoupePixels, Math.Min(_shot.Width, _shot.Height));
        int x0 = Math.Clamp(px - n / 2, 0, _shot.Width - n), y0 = Math.Clamp(py - n / 2, 0, _shot.Height - n);
        _zoom.Source = new CroppedBitmap(_shot.Image, new Int32Rect(x0, y0, n, n));
        double cell = ToolTileShape.Loupe / n;
        Canvas.SetLeft(_reticle, (px - x0) * cell);
        Canvas.SetTop(_reticle, (py - y0) * cell);
        if (_reticle.Tag is Rectangle shade)
        {
            Canvas.SetLeft(shade, (px - x0) * cell - Stroke.Hairline);
            Canvas.SetTop(shade, (py - y0) * cell - Stroke.Hairline);
        }
        Current = _shot.At(px, py);
        _labelSwatch.Background = Paint.Frozen(Current);
        _labelText.Text = ColorHistory.Hex(Current);

        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double loupe = ToolTileShape.Loupe, offset = ToolTileShape.LoupeOffset;
        double blockHeight = loupe + Space.S4 + _label.DesiredSize.Height;
        double left = point.X + offset, top = point.Y + offset;
        if (left + loupe > ActualWidth) left = point.X - offset - loupe;
        if (top + blockHeight > ActualHeight) top = point.Y - offset - blockHeight;
        Canvas.SetLeft(_loupe, left);
        Canvas.SetTop(_loupe, top);
        Canvas.SetLeft(_label, left + (loupe - _label.DesiredSize.Width) / 2);
        Canvas.SetTop(_label, top + loupe + Space.S4);
    }
}

/// <summary>Fenêtre plein écran de la pipette, posée sur l'écran de la souris (pixels réels), au premier plan, activée (Échap).</summary>
internal sealed class ColorPickerWindow : Window
{
    private readonly ColorPickerSurface _surface;
    private readonly NativeMethods.RECT _bounds;
    private readonly NativeMethods.POINT _cursor;
    private bool _done;

    public event Action<Color>? Picked;

    public ColorPickerWindow(ColorScreenShot shot, NativeMethods.RECT bounds, NativeMethods.POINT cursor)
    {
        _bounds = bounds;
        _cursor = cursor;
        Title = AppInfo.Name + " — " + Loc.T("tiles.color");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Paint.Frozen(Swatch.Black);
        Cursor = Cursors.Cross;
        Content = _surface = new ColorPickerSurface(shot);
        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) =>
        {
            Place(); // une autre mise à l'échelle sur cet écran : WPF a pu redimensionner la fenêtre
            // La loupe apparaît tout de suite sous la souris (avant le premier mouvement).
            var dpi = VisualTreeHelper.GetDpi(this);
            _surface.MoveTo(new Point((_cursor.X - _bounds.Left) / dpi.DpiScaleX, (_cursor.Y - _bounds.Top) / dpi.DpiScaleY));
        };
        MouseMove += (_, e) => _surface.MoveTo(e.GetPosition(_surface));
        MouseLeftButtonUp += (_, e) =>
        {
            _surface.MoveTo(e.GetPosition(_surface));
            Finish(_surface.Current);
        };
        MouseRightButtonUp += (_, _) => Finish(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
            else if (e.Key == Key.Enter) Finish(_surface.Current);
        };
        Deactivated += (_, _) => Finish(null);
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top, 0);
    }

    private void Finish(Color? color)
    {
        if (_done) return;
        _done = true;
        Close();
        if (color is { } picked) Picked?.Invoke(picked);
    }
}
