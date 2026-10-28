using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>
/// Icône du dock : taille pilotée par deux ressorts (grossissement au survol, apparition/disparition),
/// point sous l'icône quand l'appli est ouverte, petit rebond au lancement.
/// </summary>
public sealed class DockIconView : Grid
{
    /// <summary>Taille des icônes (réglage « Taille du dock », 48 par défaut). Le reste en découle.</summary>
    public static double BaseIcon { get; set; } = DockShape.ReferenceIcon;
    public static double Gap => BaseIcon * DockShape.GapRatio; // marge de chaque côté de l'icône
    /// <summary>Hauteur de la zone du point sous l'icône, en fraction de l'icône (plus petite en mode barre).</summary>
    public static double DotRatio { get; set; } = DockShape.DotRatio;
    public static double DotZone => BaseIcon * DotRatio; // espace sous l'icône pour le point
    public static double BaseWidth => BaseIcon + 2 * Gap;
    /// <summary>Dock en haut de l'écran (5I) : tout est retourné, le point est au-dessus et l'icône grandit vers le bas.</summary>
    public static bool AtTop { get; set; }

    /// <summary>Marge du côté du bord de l'écran (le bas, ou le haut si le dock est en haut).</summary>
    private static Thickness FromEdge(double value) => AtTop ? new Thickness(0, value, 0, 0) : new Thickness(0, 0, 0, value);


    private readonly Image _icon = new();
    private readonly Ellipse _dot = new();
    /// <summary>Cadre affiché quand on lâcherait une autre icône dessus (création de dossier).</summary>
    private readonly SquircleBorder _highlight = new()
    {
        BorderThickness = Borders.Focus,
        Background = Paint.Selected,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsHitTestVisible = false,
        Opacity = 0,
    };
    private readonly TranslateTransform _bounce = new();
    private bool _running;

    public DockIconView(DockItem item)
    {
        Item = item;
        VerticalAlignment = VerticalAlignment.Bottom;
        // Presque transparent (alpha 1) : dans une fenêtre « layered », un pixel 100 % transparent laisse
        // passer la souris, ce qui ferait clignoter le grossissement entre deux icônes.
        Background = Paint.HitTarget;
        RenderTransform = Drag;

        _icon.Source = item.Icon;
        _icon.VerticalAlignment = VerticalAlignment.Bottom;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.Margin = new Thickness(0, 0, 0, DotZone); // recalculé dans Apply() si la taille change
        _icon.RenderTransform = _bounce;
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);

        _dot.Width = _dot.Height = DockShape.Dot;
        _dot.Fill = Paint.Indicator;
        _dot.VerticalAlignment = VerticalAlignment.Bottom;
        _dot.HorizontalAlignment = HorizontalAlignment.Center;
        _dot.Opacity = 0;

        _highlight.SetResourceReference(Border.BorderBrushProperty, Services.AppearanceService.AccentBrushKey);

        Children.Add(_highlight);
        Children.Add(_icon);
        Children.Add(_dot);
        Apply();
    }

    public DockItem Item { get; }

    /// <summary>Grossissement (1 = normal).</summary>
    public Spring Scale { get; } = new(1);

    /// <summary>Apparition (0 → 1) ; vers 0 quand l'icône est retirée du dock.</summary>
    public Spring Appear { get; } = new(0);

    /// <summary>Décalage pendant un glisser-déposer.</summary>
    public TranslateTransform Drag { get; } = new();

    public bool IsRemoving { get; set; }

    /// <summary>Largeur hors grossissement (sert au calcul du grossissement, sans effet de boucle).</summary>
    public double LayoutBaseWidth => BaseWidth * Math.Max(Appear.Value, 0);

    public bool StepSprings(double dt) => Scale.Step(dt) & Appear.Step(dt);

    public void Apply()
    {
        var edge = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        VerticalAlignment = _icon.VerticalAlignment = _dot.VerticalAlignment = _highlight.VerticalAlignment = edge;
        _icon.Margin = FromEdge(DotZone);
        _dot.Margin = FromEdge(DotZone / 3);
        double appear = Math.Clamp(Appear.Value, 0, 1.2);
        double size = BaseIcon * Scale.Value * appear;
        _icon.Width = _icon.Height = Math.Max(size, 0.1);
        Width = Math.Max((BaseIcon * Scale.Value + 2 * Gap) * appear, 0);
        Opacity = Math.Clamp(appear, 0, 1);

        double ring = Math.Max(size + DockShape.HighlightPadding * Gap, 0);
        _highlight.Width = _highlight.Height = ring;
        _highlight.CornerRadius = new CornerRadius(ring * DockShape.HighlightRadius);
        _highlight.Margin = FromEdge(Math.Max(0, DotZone - (ring - size) / 2));
    }

    /// <summary>Icône du dossier refaite (appli ajoutée ou retirée).</summary>
    public void RefreshIcon() => _icon.Source = Item.Icon;

    /// <summary>Glisser-déposer : lâcher l'icône ici créerait un dossier (ou l'ajouterait à ce dossier).</summary>
    public void SetMergeHighlight(bool on) =>
        _highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, on ? Motion.Brisk : Motion.Fast));

    /// <summary>Met à jour le point « appli ouverte » (et arrête le rebond quand une fenêtre apparaît).</summary>
    public void UpdateRunning()
    {
        if (Item.IsRunning == _running) return;
        _running = Item.IsRunning;
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(_running ? 1 : 0, Motion.Base));
        if (_running) StopBounce();
    }

    /// <summary>Rebond façon macOS pendant le lancement (3 sauts maximum).</summary>
    public void StartBounce()
    {
        var bounce = new DoubleAnimationUsingKeyFrames { Duration = Motion.Bounce, RepeatBehavior = new RepeatBehavior(3) };
        double height = AtTop ? Motion.BounceHeight : -Motion.BounceHeight; // saute vers l'intérieur de l'écran
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(height, KeyTime.FromPercent(0.45), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        _bounce.BeginAnimation(TranslateTransform.YProperty, bounce);
    }

    private void StopBounce() => _bounce.BeginAnimation(TranslateTransform.YProperty, null);

    /// <summary>Zone de l'image de l'icône dans les coordonnées de <paramref name="ancestor"/>.</summary>
    public Rect IconBounds(Visual ancestor) =>
        _icon.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, _icon.ActualWidth, _icon.ActualHeight));

    /// <summary>
    /// Milieu du bord de l'icône tourné vers l'intérieur de l'écran (le haut, ou le bas si le dock est en haut), dans les
    /// coordonnées de <paramref name="ancestor"/> : l'étiquette, l'aperçu et la grille d'un dossier s'y accrochent.
    /// </summary>
    public Point IconInnerCenter(Visual ancestor) =>
        _icon.TransformToAncestor(ancestor).Transform(new Point(_icon.ActualWidth / 2, AtTop ? _icon.ActualHeight : 0));
}
