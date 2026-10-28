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
    /// disposition « au repos », centrée dans la fenêtre : pas d'effet de boucle quand les icônes grandissent.
    /// </summary>
    private void UpdateMagnification()
    {
        var children = ItemsPanel.Children.Cast<UIElement>().ToList();
        double total = ItemsPanel.Margin.Left + ItemsPanel.Margin.Right;
        foreach (var child in children) total += BaseWidthOf(child);

        double x = Root.ActualWidth / 2 + FloatingOffset() - total / 2 + ItemsPanel.Margin.Left;
        foreach (var child in children)
        {
            double width = BaseWidthOf(child);
            if (child is DockIconView view && !view.IsRemoving)
            {
                double target = 1;
                if (_pointerX is double pointer && _dragView == null && !_hidden)
                {
                    double distance = Math.Abs(pointer - (x + width / 2)) / DockIconView.BaseWidth;
                    if (distance < MagnificationRange)
                        target = 1 + (_settings.Magnification - 1) * (Math.Cos(Math.PI * distance / MagnificationRange) + 1) / 2;
                }
                view.Scale.SetTarget(target, Springs.Magnify);
            }
            x += width;
        }
        StartAnimation();
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
