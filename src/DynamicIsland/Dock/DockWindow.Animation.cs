using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Animation;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>Dock : grossissement façon macOS, ressorts, étiquette du nom.</summary>
public partial class DockWindow
{
    private const double MagnificationRange = DockShape.MagnificationRange; // portée, en largeurs d'icône

    /// <summary>Décalage du dock vers le bord de l'écran : 0 = visible, positif = caché (vers le bas, ou le haut si le dock est en haut).</summary>
    private readonly Spring _shift = new(0);
    private bool _animating;
    private TimeSpan _lastFrame;
    private double? _pointerX;
    private DockIconView? _hovered;

    private void DockRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (OnMoveDock()) return; // appui sur le fond du dock : il se déplace (ou va le faire si la souris glisse)
        _pointerX = e.GetPosition(Root).X;
        UpdateMagnification();
        SetHovered(FindView(e.OriginalSource as DependencyObject));
        OnDragMove(e);
    }

    private void DockRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragView != null) return;
        _pointerX = null;
        UpdateMagnification();
        SetHovered(null);
    }

    /// <summary>
    /// Grossit les icônes proches du curseur (courbe en cosinus). Les distances sont calculées sur la
    /// disposition « au repos » : pas d'effet de boucle quand les icônes grandissent.
    /// </summary>
    private void UpdateMagnification()
    {
        var children = ItemsPanel.Children.Cast<UIElement>().ToList();
        var widths = children.Select(BaseWidthOf).ToArray();
        var icons = children.Select(c => c is DockIconView { IsRemoving: false }).ToArray();
        double? pointer = _pointerX is double x && _dragView == null && !_hidden ? x : null;
        // Barre ou dock flottant : centrés, ils grandissent des deux côtés ; posés à gauche (à droite), seulement vers l'intérieur.
        var targets = MagnifyTargets(widths, icons, RestingContentLeft(widths.Sum()), pointer, PlaceAlignment,
            DockIconView.BaseWidth, DockIconView.BaseIcon, _settings.Magnification);
        for (int i = 0; i < children.Count; i++)
            if (icons[i]) ((DockIconView)children[i]).Scale.SetTarget(targets[i], Springs.Magnify);
        StartAnimation();
    }

    /// <summary>Bord gauche des applis au repos, dans la fenêtre (même place que celle que WPF leur donne sans grossissement).</summary>
    private double RestingContentLeft(double content)
    {
        double margins = ItemsPanel.Margin.Left + ItemsPanel.Margin.Right, width = Root.ActualWidth;
        return (IsBar, PlaceAlignment) switch
        {
            (true, DockAlignment.Left) => ItemsPanel.Margin.Left,
            (true, DockAlignment.Right) => width - ItemsPanel.Margin.Right - content,
            (true, _) => width / 2 - (content + margins) / 2 + ItemsPanel.Margin.Left,
            (false, DockAlignment.Left) => DockRoot.Margin.Left + ItemsPanel.Margin.Left,
            (false, DockAlignment.Right) => width - DockRoot.Margin.Right - (content + margins) + ItemsPanel.Margin.Left,
            _ => width / 2 + FloatingOffset() - (content + margins) / 2 + ItemsPanel.Margin.Left,
        };
    }

    /// <summary>
    /// Grossissement de chaque élément du panneau (correctif 15a), calcul pur vérifié par --self-test. <paramref name="widths"/> :
    /// largeurs au repos ; <paramref name="icons"/> : ce qui grossit (pas les séparateurs ni les boutons) ; <paramref name="left"/> :
    /// bord gauche du contenu au repos ; <paramref name="pointer"/> : souris (null = rien ne grossit).
    /// Centré (calcul d'avant, inchangé) : le panneau grandit des deux côtés, les distances sont prises sur la disposition au repos.
    /// Aligné à gauche (à droite), il ne grandit que vers la droite (la gauche) : tout ce qui grossit avant la souris pousserait
    /// l'icône survolée plus loin. On cherche donc le point « au repos » qui, une fois les icônes grossies, tombe sous la souris.
    /// </summary>
    internal static double[] MagnifyTargets(IReadOnlyList<double> widths, IReadOnlyList<bool> icons, double left, double? pointer,
        DockAlignment alignment, double baseWidth, double baseIcon, double magnification)
    {
        var targets = new double[widths.Count];
        Array.Fill(targets, 1.0);
        if (pointer is not double p || magnification <= 1 || baseWidth <= 0) return targets;
        double center = alignment == DockAlignment.Center ? p : SolveCenter(widths, icons, left, p, alignment, baseWidth, baseIcon, magnification);
        Fill(targets, widths, icons, left, center, baseWidth, magnification);
        return targets;
    }

    /// <summary>Courbe en cosinus : ×<paramref name="magnification"/> sous le centre, 1 à <see cref="MagnificationRange"/> icônes.</summary>
    private static void Fill(double[] targets, IReadOnlyList<double> widths, IReadOnlyList<bool> icons, double left, double center,
        double baseWidth, double magnification)
    {
        double x = left;
        for (int i = 0; i < widths.Count; i++)
        {
            double distance = Math.Abs(center - (x + widths[i] / 2)) / baseWidth;
            targets[i] = icons[i] && distance < MagnificationRange
                ? 1 + (magnification - 1) * (Math.Cos(Math.PI * distance / MagnificationRange) + 1) / 2
                : 1;
            x += widths[i];
        }
    }

    /// <summary>
    /// Où tombe, une fois les icônes grossies (<paramref name="targets"/>), le point <paramref name="rest"/> de la disposition au
    /// repos : chaque icône s'élargit de (grossissement − 1) × taille d'icône, le panneau grandit selon son alignement.
    /// </summary>
    internal static double MagnifiedPosition(double rest, IReadOnlyList<double> targets, IReadOnlyList<double> widths, IReadOnlyList<bool> icons,
        double left, DockAlignment alignment, double baseWidth, double baseIcon)
    {
        double Growth(int i) => icons[i] ? (targets[i] - 1) * baseIcon * widths[i] / baseWidth : 0; // apparition comprise
        double total = 0;
        for (int i = 0; i < widths.Count; i++) total += Growth(i);
        double x = left + alignment switch
        {
            DockAlignment.Center => -total / 2,
            DockAlignment.Right => -total,
            _ => 0,
        };
        if (rest <= left) return x + (rest - left);
        double restX = left;
        for (int i = 0; i < widths.Count; i++)
        {
            double width = widths[i], growth = Growth(i);
            if (rest < restX + width) return x + (rest - restX) * (width + growth) / width;
            x += width + growth;
            restX += width;
        }
        return x + (rest - restX);
    }

    /// <summary>Point au repos qui arrive sous la souris une fois grossi (dichotomie : la position grossie croît avec lui).</summary>
    private static double SolveCenter(IReadOnlyList<double> widths, IReadOnlyList<bool> icons, double left, double pointer,
        DockAlignment alignment, double baseWidth, double baseIcon, double magnification)
    {
        var targets = new double[widths.Count];
        double content = 0;
        foreach (var width in widths) content += width;
        double low = left - MagnificationRange * baseWidth, high = left + content + MagnificationRange * baseWidth;
        double Error(double center)
        {
            Fill(targets, widths, icons, left, center, baseWidth, magnification);
            return MagnifiedPosition(center, targets, widths, icons, left, alignment, baseWidth, baseIcon) - pointer;
        }
        if (Error(low) >= 0) return low; // souris loin avant les applis : rien ne grossit
        if (Error(high) <= 0) return high; // … ou loin après
        for (int i = 0; i < 40; i++)
        {
            double middle = (low + high) / 2;
            if (Error(middle) < 0) low = middle;
            else high = middle;
        }
        return (low + high) / 2;
    }

    /// <summary>Largeur hors grossissement : icônes (selon leur apparition), séparateur et bouton Rechercher (fixes).</summary>
    private static double BaseWidthOf(UIElement child) => child switch
    {
        DockIconView view => view.LayoutBaseWidth,
        FrameworkElement element => element.Width + element.Margin.Left + element.Margin.Right,
        _ => 0,
    };

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        dt *= AppearanceService.Speed; // réglage « Vitesse des animations »

        bool settled = _shift.Step(dt);
        double shift = AtTop ? -_shift.Value : _shift.Value;
        if (DockShift.Y != shift)
        {
            DockShift.Y = shift;
            UpdateBackdrop(); // le flou suit la barre quand elle se cache ou revient
        }

        foreach (var view in _views.Values.ToList())
        {
            settled &= view.StepSprings(dt);
            view.Apply();
            if (view.IsRemoving && view.Appear.IsSettled)
            {
                ItemsPanel.Children.Remove(view);
                _views.Remove(view.Item);
                if (_hovered == view) SetHovered(null);
            }
        }

        PlaceLabel();
        if (settled) StopAnimation();
    }

    // ------------------------------------------------------------------ étiquette

    private void SetHovered(DockIconView? view)
    {
        if (view == _hovered) return;
        _hovered = view;
        OnHoveredChanged();

        if (view != null && _dragView == null && !(_preview?.IsVisible ?? false)) ShowLabel(view, view.Item.Name);
        else if (_labelAnchor is DockIconView) HideLabel(); // ne touche pas à l'étiquette d'une icône d'arrière-plan
    }

    /// <summary>Élément au-dessus duquel l'étiquette du nom est affichée (icône du dock ou d'arrière-plan).</summary>
    private FrameworkElement? _labelAnchor;

    private void ShowLabel(FrameworkElement anchor, string text)
    {
        _labelAnchor = anchor;
        NameText.Text = text;
        NameLabel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Motion.Quick));
        PlaceLabel();
    }

    private void HideLabel()
    {
        _labelAnchor = null;
        NameLabel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Motion.Instant));
    }

    private void PlaceLabel()
    {
        var anchor = _labelAnchor;
        if (anchor == null || !anchor.IsLoaded || PresentationSource.FromVisual(anchor) == null) return;
        NameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Au-dessus de l'icône (en dessous si le dock est en haut de l'écran).
        var inner = anchor is DockIconView view
            ? view.IconInnerCenter(Root)
            : anchor.TransformToAncestor(Root).Transform(new Point(anchor.ActualWidth / 2, AtTop ? anchor.ActualHeight : 0));
        Canvas.SetLeft(NameLabel, Math.Round(inner.X - NameLabel.DesiredSize.Width / 2));
        Canvas.SetTop(NameLabel, Math.Round(AtTop ? inner.Y + DockShape.LabelOffset : inner.Y - NameLabel.DesiredSize.Height - DockShape.LabelOffset));
    }

    private static DockIconView? FindView(DependencyObject? source)
    {
        while (source != null && source is not DockIconView)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        return source as DockIconView;
    }
}
