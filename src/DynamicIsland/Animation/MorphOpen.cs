using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Themes;

namespace DynamicIsland.Animation;

/// <summary>
/// « Ouvrir un outil » (refonte O3, prévu pour les vues détaillées d'O8) : la tuile source s'agrandit en morphing jusqu'à remplir
/// la zone de l'outil (contenu croisé : l'image de la tuile s'efface pendant que l'outil apparaît dessous), puis
/// <see cref="Close"/> fait l'inverse (l'outil rentre dans la tuile). Le « fantôme » est une forme du calque des mouvements
/// (<see cref="MotionLayer"/>) : fond et bordure de la tuile + son image prise au clic, recalés à chaque image sur la tuile et
/// la zone d'arrivée (qui peuvent bouger pendant que l'île change de taille). Retiré à la fin. Animations réduites : rien,
/// l'outil s'ouvre avec son fondu habituel.
/// </summary>
public sealed class MorphOpen : IMotion
{
    private readonly Canvas _host;
    private readonly SquircleBorder _ghost;
    private readonly Image _picture;
    private readonly Spring _progress = new(0);
    private FrameworkElement _tile;
    private readonly FrameworkElement _area;
    private readonly CornerRadius _tileCorner, _areaCorner;
    private double _tileOpacity = 1;

    /// <summary>La tuile d'où l'outil est parti (pour le retour).</summary>
    public FrameworkElement Tile => _tile;

    /// <summary>Ouvert et pas encore refermé.</summary>
    public bool IsOpen { get; private set; }

    private MorphOpen(Canvas host, FrameworkElement tile, FrameworkElement area, CornerRadius areaCorner)
    {
        _host = host;
        _tile = tile;
        _area = area;
        _tileCorner = tile is Border border ? border.CornerRadius : Corners.Tile;
        _areaCorner = areaCorner;
        _picture = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        _ghost = new SquircleBorder
        {
            IsHitTestVisible = false,
            BorderThickness = Borders.Hairline,
            Child = _picture,
        };
        if (tile is Border { Background: { } background }) _ghost.Background = background;
        else _ghost.SetResourceReference(Border.BackgroundProperty, OpalTheme.TileKey);
        _ghost.SetResourceReference(Border.BorderBrushProperty, OpalTheme.TileRimKey);
    }

    /// <summary>
    /// La tuile <paramref name="tile"/> s'ouvre sur <paramref name="area"/> ; <paramref name="swap"/> montre l'outil (appelé tout de
    /// suite : l'outil apparaît sous le fantôme). Renvoie null (et appelle quand même <paramref name="swap"/>) sans calque ou
    /// avec les animations réduites.
    /// </summary>
    public static MorphOpen? Open(FrameworkElement tile, FrameworkElement area, CornerRadius areaCorner, Action swap)
    {
        var host = MotionPrefs.Rich ? MotionLayer.HostOf(tile) : null;
        if (host == null || tile.ActualWidth < 1 || tile.ActualHeight < 1)
        {
            swap();
            return null;
        }
        var morph = new MorphOpen(host, tile, area, areaCorner);
        morph.Begin(closing: false);
        swap();
        return morph;
    }

    /// <summary>Retour : l'outil rentre dans la tuile (<paramref name="tile"/> si elle a été recréée), <paramref name="swap"/> remet la vue d'avant.</summary>
    public void Close(Action swap, FrameworkElement? tile = null)
    {
        IsOpen = false;
        if (tile != null) _tile = tile;
        swap();
        if (!MotionPrefs.Rich || _tile.ActualWidth < 1 || !_tile.IsLoaded)
        {
            Finish();
            return;
        }
        Begin(closing: true);
    }

    private void Begin(bool closing)
    {
        _picture.Source = Snapshot(_tile);
        if (!_host.Children.Contains(_ghost)) _host.Children.Add(_ghost);
        _tileOpacity = _tile.ReadLocalValue(UIElement.OpacityProperty) is double local ? local : 1;
        _tile.Opacity = 0; // c'est le fantôme qu'on voit
        if (!closing) IsOpen = true;
        _progress.Jump(closing ? 100 : 0);
        _progress.SetTarget(closing ? 0 : 100, Springs.MorphOpen);
        Apply();
        MotionClock.Start(this);
    }

    public bool Step(double dt)
    {
        bool done = _progress.Step(dt);
        Apply();
        if (done) Finish();
        return done;
    }

    public void Finish()
    {
        MotionClock.Stop(this);
        _progress.Snap();
        _host.Children.Remove(_ghost);
        _picture.Source = null;
        if (_tileOpacity >= 1) _tile.ClearValue(UIElement.OpacityProperty);
        else _tile.Opacity = _tileOpacity;
    }

    /// <summary>
    /// Fondus du fantôme à l'avancement 0 (tuile) → 1 (outil), les mêmes à l'aller et au retour : à 0 il EST la tuile (image
    /// comprise), l'image s'efface d'abord, puis le fantôme lui-même laisse voir l'outil (calcul pur, --self-test).
    /// </summary>
    public static (double Picture, double Ghost) FadesAt(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return (Math.Clamp(1 - t / 0.45, 0, 1), Math.Clamp((1 - t) / 0.4, 0, 1));
    }

    private void Apply()
    {
        double t = _progress.Value / 100;
        double clamped = Math.Clamp(t, 0, 1.03); // un rien de dépassement, jamais hors de l'île
        if (!TryBounds(_tile, out var from) || !TryBounds(_area, out var to)) return;
        double Lerp(double a, double b) => a + (b - a) * clamped;
        var rect = new Rect(Lerp(from.X, to.X), Lerp(from.Y, to.Y), Math.Max(1, Lerp(from.Width, to.Width)), Math.Max(1, Lerp(from.Height, to.Height)));
        Canvas.SetLeft(_ghost, rect.X);
        Canvas.SetTop(_ghost, rect.Y);
        _ghost.Width = rect.Width;
        _ghost.Height = rect.Height;
        _ghost.CornerRadius = new CornerRadius(Lerp(_tileCorner.TopLeft, _areaCorner.TopLeft));
        var (picture, ghost) = FadesAt(t);
        _picture.Opacity = picture;
        _ghost.Opacity = ghost;
    }

    private bool TryBounds(FrameworkElement element, out Rect bounds)
    {
        bounds = default;
        if (!element.IsLoaded || element.TransformToVisual(_host) is not { } transform) return false;
        bounds = transform.TransformBounds(new Rect(element.RenderSize));
        return bounds.Width > 0;
    }

    private static BitmapSource? Snapshot(FrameworkElement element)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            int width = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
            if (width < 1 || height < 1) return null;
            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Rendue sans sa propre transformation (survol, appui) : l'image a la taille de la tuile.
            var brush = new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(element.RenderSize));
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null; // pas d'image : le fantôme garde le fond de la tuile
        }
    }
}
