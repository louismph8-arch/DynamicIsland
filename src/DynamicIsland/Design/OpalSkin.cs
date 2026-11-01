using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>Où la surface est posée : ce qui change, c'est le « flou » du fond et le voile minimal.</summary>
public enum OpalRole
{
    /// <summary>Île : copie floutée du fond d'écran derrière elle, voile plus sombre quand une fenêtre est derrière.</summary>
    Island,
    /// <summary>Dock flottant : copie floutée du fond d'écran (le dock en mode Fixe n'a jamais de fenêtre derrière lui).</summary>
    FloatingDock,
    /// <summary>Barre du dock : le vrai acrylique de Windows est dessous (DockBackdrop), seulement la teinte opaline.</summary>
    Bar,
    /// <summary>Palette, cartes (avis, nouveautés…) : posées sur des fenêtres, voile assez sombre pour rester lisible.</summary>
    Panel,
}

/// <summary>
/// Le verre opalin (refonte O1) posé sur une forme existante (<see cref="SquircleBorder"/> de l'île, du dock, de la palette, des
/// cartes) : couches sous le contenu (copie floutée du fond d'écran, voile sombre, teinte d'accent, voile laiteux, grain, liseré du
/// haut), bordure irisée orientée par la lumière du verre liquide (aucun suivi de souris en plus), balayage nacré à la demande.
/// Le verre liquide reste l'<c>Effect</c> de la forme : les couches font partie de ce qu'il réfracte. Au repos rien ne tourne.
/// </summary>
public sealed class OpalSkin
{
    private readonly SquircleBorder _body;
    private readonly OpalRole _role;
    private readonly Grid _layers = new() { IsHitTestVisible = false };
    private readonly Canvas _backdropHost = new();
    private readonly Image _backdrop = new() { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
    private readonly Image _backdropOld = new() { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed }; // ancien fond qui s'efface (O2)
    private readonly Rectangle _smoke = new();
    private readonly Rectangle _tint = new();
    private readonly Rectangle _milk = new();
    private readonly Rectangle _grain = new();
    private readonly Rectangle _topLine = new() { Height = Stroke.Hairline, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _sweepHost = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _sweep = new() { Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly TranslateTransform _sweepMove = new();
    private OpalPaint _paint;
    private Color? _accent;
    private Brush? _rimOverride;
    private bool _covered;
    private bool _backdropEnabled = true;
    private bool _topLineVisible = true;
    private Point _light = new(0.5, 0.5);
    private Rect? _monitor; // pixels de l'écran sous la surface (refait quand la fenêtre bouge)
    private Window? _window;
    private bool _attached = true;

    /// <summary>--snapshot : la surface est supposée collée en bas de l'écran (dock) plutôt qu'en haut (île).</summary>
    public bool SnapshotAtBottom { get; init; }

    /// <summary>La bordure irisée prend l'épaisseur de l'intensité (sinon on garde celle de la forme : tailles calculées avec elle).</summary>
    public bool OwnsBorderThickness { get; set; }

    /// <param name="glass">Verre liquide de la même forme : sa lumière oriente la bordure irisée.</param>
    public OpalSkin(SquircleBorder body, OpalRole role, LiquidGlass? glass = null)
    {
        _body = body;
        _role = role;
        _paint = new OpalPaint(OpalTheme.Look, OpalTheme.Grain);

        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.Linear);
        RenderOptions.SetBitmapScalingMode(_backdropOld, BitmapScalingMode.Linear);
        _backdropHost.Children.Add(_backdrop);
        _backdropHost.Children.Add(_backdropOld);
        foreach (var layer in new UIElement[] { _backdropHost, _smoke, _tint, _milk, _grain, _topLine }) _layers.Children.Add(layer);
        _sweep.RenderTransform = new TransformGroup { Children = { new RotateTransform(Opal.SweepAngle), _sweepMove } };
        _sweepHost.Children.Add(_sweep);
        Insert();

        body.SizeChanged += (_, _) => Layout();
        body.Loaded += OnLoaded;
        if (glass != null) glass.LightMoved += OnLightMoved;
        OpalTheme.Changed += Rebuild;
        if (UsesBackdrop)
        {
            OpalBackdrop.Changed += Layout;
            OpalBackdrop.Start();
        }
        Apply();
    }

    private bool UsesBackdrop => _role is OpalRole.Island or OpalRole.FloatingDock;

    /// <summary>Couches sous le contenu, balayage par-dessus ; le contenu de la forme reste le même élément (noms, code).</summary>
    private void Insert()
    {
        if (_body.Child is Panel panel)
        {
            panel.Children.Insert(0, _layers);
            panel.Children.Add(_sweepHost);
        }
        else
        {
            var old = _body.Child;
            _body.Child = null;
            var grid = new Grid();
            grid.Children.Add(_layers);
            if (old != null) grid.Children.Add(old);
            grid.Children.Add(_sweepHost);
            _body.Child = grid;
        }
        foreach (var element in new UIElement[] { _layers, _sweepHost })
        {
            Grid.SetRowSpan(element, 99);    // une grille à plusieurs lignes / colonnes (carte avec pied de page) : toute la surface
            Grid.SetColumnSpan(element, 99);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window != null) return;
        _window = Window.GetWindow(_body);
        if (_window == null) return;
        _window.LocationChanged += (_, _) => { _monitor = null; Layout(); };
        _window.DpiChanged += (_, _) => { _monitor = null; Layout(); };
        _window.Closed += (_, _) => Detach();
        Layout();
    }

    /// <summary>Fenêtre fermée : plus d'abonnement aux événements de l'app.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        OpalTheme.Changed -= Rebuild;
        OpalBackdrop.Changed -= Layout;
    }

    /// <summary>Couleur imposée (pochette de l'album) : la bordure irisée tourne autour d'elle ; null = l'accent.</summary>
    public void SetAccent(Color? accent)
    {
        if (accent == _accent) return;
        _accent = accent;
        Rebuild();
    }

    /// <summary>Une fenêtre (pas le bureau) est derrière : pas de copie du fond d'écran, voile plus sombre.</summary>
    public void SetCovered(bool covered)
    {
        if (covered == _covered) return;
        _covered = covered;
        Layout();
    }

    /// <summary>Dock : barre (acrylique de Windows dessous) ou flottant (copie floutée du fond).</summary>
    public void SetBackdrop(bool enabled, bool topLine = true)
    {
        _topLineVisible = topLine;
        if (enabled == _backdropEnabled) { Layout(); return; }
        _backdropEnabled = enabled;
        Layout();
    }

    /// <summary>Bordure prise par autre chose pendant un moment (fin du minuteur : liseré orange qui clignote) ; null = irisée.</summary>
    public void OverrideRim(Brush? brush)
    {
        _rimOverride = brush;
        _body.BorderBrush = brush ?? _paint.Rim;
    }

    private void Rebuild()
    {
        _paint = new OpalPaint(OpalTheme.Look, OpalTheme.Grain, _accent);
        _paint.SetLight(_light.X, _light.Y);
        Apply();
    }

    /// <summary>Pinceaux de l'intensité actuelle.</summary>
    private void Apply()
    {
        var look = _paint.Look;
        _tint.Fill = _paint.Tint;
        _milk.Fill = _paint.Milk;
        _grain.Fill = _paint.Grain;
        _grain.Opacity = look.Grain;
        _topLine.Fill = _paint.TopLine;
        _sweep.Fill = _paint.Sweep;
        _body.BorderBrush = _rimOverride ?? _paint.Rim;
        if (OwnsBorderThickness) _body.BorderThickness = new Thickness(look.IridWidth);
        Layout();
    }

    private void OnLightMoved(Point light, Size size)
    {
        if (size.Width < 1 || size.Height < 1 || !OpalTheme.EffectsOn) return;
        _light = new Point(Math.Clamp(light.X / size.Width, 0, 1), Math.Clamp(light.Y / size.Height, 0, 1));
        _paint.SetLight(_light.X, _light.Y);
    }

    // ------------------------------------------------------------------ copie floutée du fond, voile

    /// <summary>Taille, place, fond d'écran ou état changés : copie floutée au bon endroit, voile, liseré.</summary>
    private void Layout()
    {
        double radius = _body.CornerRadius.TopLeft;
        _topLine.Margin = new Thickness(radius * Opal.TopLineInset, 0, radius * Opal.TopLineInset, 0);
        _topLine.Visibility = _topLineVisible ? Visibility.Visible : Visibility.Collapsed;

        var image = OpalBackdrop.Image;
        bool show = UsesBackdrop && _backdropEnabled && !_covered && image != null && PlaceBackdrop(image);
        _backdrop.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var smoke = show ? BackdropSmoke() : Veil();
        if (!ReferenceEquals(_smoke.Fill, smoke)) _smoke.Fill = smoke;
    }

    private (double Alpha, Color Base, Color Text, Color Worst, Brush Brush)? _smokeCache;
    private (double Min, double Opacity, bool Covered, Color Base, Brush? Brush)? _veilCache;

    /// <summary>Opacité du dernier voile posé sur la copie floutée (--self-test, rendus).</summary>
    public double LastSmoke { get; private set; }

    /// <summary>
    /// Voile sur la copie floutée : celui de l'intensité (plus présent sur un fond d'écran clair), puis renforcé juste ce qu'il
    /// faut pour que le texte secondaire garde un contraste WCAG ≥ 4,5 sur la surface réelle (voile + teinte + lait posés sur le
    /// quart le plus clair du fond flouté en sombre, le plus sombre en opale blanche). Gardé tant que rien ne change.
    /// </summary>
    private Brush BackdropSmoke()
    {
        var look = _paint.Look;
        double alpha = OpalBackdrop.SmokeFor(look);
        var color = AppearanceService.SurfaceBase;
        var text = OpalShade.Ink(InkRole.TextSecondary);
        var worst = OpalShade.Light >= 0.5 ? OpalBackdrop.Darkest : OpalBackdrop.Brightest;
        if (_smokeCache is { } cache && cache.Base == color && cache.Text == text && cache.Worst == worst && cache.Alpha == alpha) return cache.Brush;
        var accent = OpalShade.Shown.AccentStart;
        double milk = (look.MilkTop + look.MilkBottom) / 2;
        Color Above(Color c) => OpalTheme.Over(OpalPalette.WithOpacity(Swatch.White, milk), OpalTheme.Over(OpalPalette.WithOpacity(accent, look.Tint), c));
        double veil = OpalColor.VeilFor(text, color, worst, alpha, OpalColor.TextContrast, Above);
        var brush = Paint.Frozen(OpalPalette.WithOpacity(color, veil));
        _smokeCache = (alpha, color, text, worst, brush);
        LastSmoke = veil;
        return brush;
    }

    /// <summary>
    /// Sans copie floutée dessous, le fond de la forme (réglage « Opacité ») se voit ; on ajoute juste ce qu'il faut de voile pour
    /// rester lisible sur une fenêtre (île couverte, palette, cartes).
    /// </summary>
    private Brush? Veil()
    {
        double min = _role switch
        {
            OpalRole.Island or OpalRole.FloatingDock => _covered ? Opal.CoveredSmoke : 0,
            OpalRole.Panel => Opal.PanelSmokeMin,
            _ => 0,
        };
        double opacity = SettingsStore.Current.Appearance.Opacity;
        var color = AppearanceService.SurfaceBase;
        if (_veilCache is { } cache && cache.Min == min && cache.Opacity == opacity && cache.Covered == _covered && cache.Base == color) return cache.Brush;
        Brush? brush = min <= opacity || opacity >= 1 ? null
            : Paint.Frozen(OpalPalette.WithOpacity(color, (min - opacity) / (1 - opacity)));
        _veilCache = (min, opacity, _covered, color, brush);
        return brush;
    }

    /// <summary>Le morceau de la copie floutée qui est derrière la forme (même écran, même place, même échelle).</summary>
    private bool PlaceBackdrop(System.Windows.Media.Imaging.BitmapSource image)
    {
        Rect screen;
        if (OpalBackdrop.SnapshotMode)
        {
            // Rendu hors écran : l'écran supposé est centré sur la forme, collé en haut (île) ou en bas (dock).
            double w = _body.ActualWidth, h = _body.ActualHeight;
            var size = Opal.SnapshotScreen;
            double x = (size.Width - w) / 2, y = SnapshotAtBottom ? size.Height - h - Opal.SnapshotEdge : Opal.SnapshotEdge;
            var border = _body.BorderThickness;
            screen = new Rect(-x - border.Left, -y - border.Top, size.Width, size.Height);
        }
        else
        {
            if (PresentationSource.FromVisual(_backdropHost) == null || _body.ActualWidth < 1) return false;
            var monitor = _monitor ??= MonitorUnder();
            if (monitor is not { } m) return false;
            try
            {
                screen = new Rect(_backdropHost.PointFromScreen(m.TopLeft), _backdropHost.PointFromScreen(m.BottomRight));
            }
            catch (InvalidOperationException)
            {
                return false; // fenêtre en cours de fermeture
            }
        }
        if (screen.Width < 1 || screen.Height < 1) return false;
        if (!ReferenceEquals(_backdrop.Source, image))
        {
            FadeOutOld();
            _backdrop.Source = image;
        }
        // Comme le fond de Windows en « Remplir » : l'image couvre l'écran, centrée.
        double scale = Math.Max(screen.Width / image.PixelWidth, screen.Height / image.PixelHeight);
        double iw = image.PixelWidth * scale, ih = image.PixelHeight * scale;
        _backdrop.Width = iw;
        _backdrop.Height = ih;
        Canvas.SetLeft(_backdrop, screen.X + (screen.Width - iw) / 2);
        Canvas.SetTop(_backdrop, screen.Y + (screen.Height - ih) / 2);
        return true;
    }

    /// <summary>
    /// Nouveau fond d'écran : l'ancienne copie floutée reste par-dessus et s'efface (~1,2 s, une seule animation, puis plus rien
    /// ne tourne) au lieu de sauter d'une image à l'autre.
    /// </summary>
    private void FadeOutOld()
    {
        if (_backdrop.Source == null || _backdrop.Visibility != Visibility.Visible || OpalBackdrop.SnapshotMode || !_body.IsVisible) return;
        _backdropOld.BeginAnimation(OpacityProperty, null);
        _backdropOld.Source = _backdrop.Source;
        _backdropOld.Width = _backdrop.Width;
        _backdropOld.Height = _backdrop.Height;
        Canvas.SetLeft(_backdropOld, Canvas.GetLeft(_backdrop));
        Canvas.SetTop(_backdropOld, Canvas.GetTop(_backdrop));
        _backdropOld.Opacity = 1;
        _backdropOld.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Opal.BackdropFade.TotalMilliseconds / Math.Clamp(AppearanceService.Speed, 0.5, 2)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        fade.Completed += (_, _) =>
        {
            _backdropOld.BeginAnimation(OpacityProperty, null);
            _backdropOld.Visibility = Visibility.Collapsed;
            _backdropOld.Source = null;
        };
        _backdropOld.BeginAnimation(OpacityProperty, fade);
    }

    private static readonly DependencyProperty OpacityProperty = UIElement.OpacityProperty;

    private Rect? MonitorUnder()
    {
        try
        {
            var center = _body.PointToScreen(new Point(_body.ActualWidth / 2, _body.ActualHeight / 2));
            var bounds = ScreenInfo.At(new NativeMethods.POINT { X = (int)center.X, Y = (int)center.Y }).Bounds;
            return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ balayage nacré

    /// <summary>Un reflet traverse la surface (~600 ms : ouverture de l'île, annonce), puis plus rien ne tourne.</summary>
    /// <param name="target">Taille vers laquelle la forme grandit (île qui s'ouvre) : le reflet va jusqu'au bout.</param>
    public void PlaySweep(Size? target = null)
    {
        if (!OpalTheme.EffectsOn || !_body.IsVisible || !PrepareSweep(target, out double from, out double to)) return;
        var run = new DoubleAnimation(from, to, Opal.Sweep) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        run.Completed += (_, _) =>
        {
            _sweepMove.BeginAnimation(TranslateTransform.XProperty, null);
            _sweep.Visibility = Visibility.Collapsed;
        };
        _sweepMove.BeginAnimation(TranslateTransform.XProperty, run);
    }

    /// <summary>Balayage figé à une position (0–1), pour le rendu --snapshot.</summary>
    public void ShowSweepAt(double t)
    {
        if (!PrepareSweep(null, out double from, out double to)) return;
        _sweepMove.BeginAnimation(TranslateTransform.XProperty, null);
        _sweepMove.X = from + (to - from) * t;
    }

    /// <summary>Rendu --snapshot : plus de balayage.</summary>
    public void HideSweep()
    {
        _sweepMove.BeginAnimation(TranslateTransform.XProperty, null);
        _sweep.Visibility = Visibility.Collapsed;
    }

    private bool PrepareSweep(Size? target, out double from, out double to)
    {
        double w = target?.Width ?? _body.ActualWidth, h = target?.Height ?? _body.ActualHeight;
        from = to = 0;
        if (w < 1 || h < 1) return false;
        _sweep.Width = Math.Max(Opal.SweepMinWidth, w * _paint.Look.SweepWidth);
        _sweep.Height = h * Opal.SweepHeight;
        Canvas.SetTop(_sweep, (h - _sweep.Height) / 2);
        from = -_sweep.Width * 1.5;
        to = w + _sweep.Width * 0.5;
        _sweep.Visibility = Visibility.Visible;
        return true;
    }
}
