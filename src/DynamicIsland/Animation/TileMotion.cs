using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Themes;

namespace DynamicIsland.Animation;

/// <summary>
/// Toucher d'une tuile (refonte O3) : au survol, légère inclinaison vers la souris (simulée en 2D : cisaillement ≤ 2,5° +
/// échelle + décalage d'un rien, le tout lissé par des ressorts) et reflet radial qui suit le curseur sur la tuile ; à l'appui,
/// la tuile s'enfonce (~97 %) puis rebondit. En XAML : <c>a:TileMotion.Tilt="True"</c> (posé d'office avec
/// <c>OpalTile.Glow</c>) et <c>a:TileMotion.Press="True"</c> (boutons ronds : <c>PressDepth</c> = leur ancienne échelle).
/// <para>Rien ne tourne au repos : l'horloge partagée n'avance que pendant le survol qui bouge, l'appui, puis le retour. Le reflet
/// est un rectangle du calque des mouvements (<see cref="MotionLayer"/>) découpé à la forme de la tuile (et à la zone visible
/// d'une liste qui défile), retiré à la sortie. Animations réduites ou mode Gaming : pas d'inclinaison ni de reflet, appui sans
/// rebond.</para>
/// </summary>
public static class TileMotion
{
    public static readonly DependencyProperty TiltProperty = DependencyProperty.RegisterAttached(
        "Tilt", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, OnTiltChanged));

    public static bool GetTilt(DependencyObject element) => (bool)element.GetValue(TiltProperty);
    public static void SetTilt(DependencyObject element, bool value) => element.SetValue(TiltProperty, value);

    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
        "Press", typeof(bool), typeof(TileMotion), new PropertyMetadata(false, OnPressChanged));

    public static bool GetPress(DependencyObject element) => (bool)element.GetValue(PressProperty);
    public static void SetPress(DependencyObject element, bool value) => element.SetValue(PressProperty, value);

    /// <summary>Échelle de l'élément enfoncé (0,97 pour une tuile ; les boutons ronds gardent leur 0,92).</summary>
    public static readonly DependencyProperty PressDepthProperty = DependencyProperty.RegisterAttached(
        "PressDepth", typeof(double), typeof(TileMotion), new PropertyMetadata(Kinetics.PressScale));

    public static double GetPressDepth(DependencyObject element) => (double)element.GetValue(PressDepthProperty);
    public static void SetPressDepth(DependencyObject element, double value) => element.SetValue(PressDepthProperty, value);

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    /// <summary>Inclinaison (degrés) pour une souris en (nx, ny) ∈ [-1, 1]² depuis le centre ; bornée à <see cref="Kinetics.TiltMax"/>.</summary>
    public static (double AngleX, double AngleY) TiltFor(double nx, double ny)
    {
        if (double.IsNaN(nx) || double.IsNaN(ny)) return (0, 0);
        nx = Math.Clamp(nx, -1, 1);
        ny = Math.Clamp(ny, -1, 1);
        // Le haut de la tuile penche vers la souris (cisaillement horizontal), le côté le plus proche descend un peu.
        double ax = -nx * Kinetics.TiltMax;
        double ay = ny * nx * Kinetics.TiltMax * 0.5;
        return (Math.Clamp(ax, -Kinetics.TiltMax, Kinetics.TiltMax), Math.Clamp(ay, -Kinetics.TiltMax, Kinetics.TiltMax));
    }

    private static void OnTiltChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        var state = States.GetValue(element, el => new State(el));
        state.Tilt = (bool)e.NewValue;
    }

    private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        var state = States.GetValue(element, el => new State(el));
        state.Press = (bool)e.NewValue;
    }

    /// <summary>--snapshot : tuile figée inclinée vers <paramref name="local"/> (et enfoncée), reflet compris.</summary>
    public static void ShowPose(FrameworkElement element, Point local, bool pressed)
    {
        var state = States.GetValue(element, el => new State(el));
        state.Pose(local, pressed);
    }

    /// <summary>--snapshot : tuile remise au repos.</summary>
    public static void ClearPose(FrameworkElement element)
    {
        if (States.TryGetValue(element, out var state)) state.Pose(null, false);
    }

    private sealed class State : IMotion
    {
        private readonly FrameworkElement _element;
        private readonly Spring _angleX = new(0), _angleY = new(0), _hover = new(100), _push = new(100), _shine = new(0);
        private bool _hooked, _over, _down;
        private Point _local;
        private double _nx, _ny;
        private Canvas? _host;
        private Rectangle? _shineRect;
        private RadialGradientBrush? _brush;
        private Window? _window;

        public bool Tilt { get; set; }
        public bool Press { get; set; }

        public State(FrameworkElement element)
        {
            _element = element;
            Hook();
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            _element.MouseEnter += (_, e) => { _over = true; Move(e.GetPosition(_element)); };
            _element.MouseMove += (_, e) => { if (_over && Tilt) Move(e.GetPosition(_element)); };
            _element.MouseLeave += (_, _) => { _over = false; Kick(); };
            _element.PreviewMouseLeftButtonDown += (_, _) => Down();
            _element.PreviewMouseLeftButtonUp += (_, _) => Release();
            _element.Unloaded += (_, _) => Reset();
        }

        private void Move(Point local)
        {
            _local = local;
            Kick();
        }

        private void Down()
        {
            if (!Press) return;
            _down = true;
            // L'île peut capturer la souris (glisser) : le relâcher arrive alors à la fenêtre, jamais à la tuile.
            _window ??= Window.GetWindow(_element);
            if (_window != null)
            {
                _window.PreviewMouseLeftButtonUp -= OnWindowUp;
                _window.PreviewMouseLeftButtonUp += OnWindowUp;
            }
            Kick();
        }

        private void OnWindowUp(object sender, MouseButtonEventArgs e) => Release();

        private void Release()
        {
            if (_window != null) _window.PreviewMouseLeftButtonUp -= OnWindowUp;
            if (!_down) return;
            _down = false;
            Kick();
        }

        private void Kick()
        {
            Retarget();
            if (_angleX.IsSettled && _angleY.IsSettled && _hover.IsSettled && _push.IsSettled && _shine.IsSettled)
            {
                Apply(); // déjà posé (souris immobile) : juste le reflet au bon endroit
                return;
            }
            MotionClock.Start(this);
        }

        private bool TiltOn => Tilt && _over && MotionPrefs.Rich && _element.ActualWidth > 0 && _element.ActualHeight > 0;

        private void Retarget()
        {
            bool tilt = TiltOn;
            if (tilt)
            {
                _nx = Math.Clamp(_local.X / _element.ActualWidth * 2 - 1, -1, 1);
                _ny = Math.Clamp(_local.Y / _element.ActualHeight * 2 - 1, -1, 1);
                _host ??= MotionLayer.HostOf(_element);
            }
            var (ax, ay) = tilt ? TiltFor(_nx, _ny) : (0, 0);
            _angleX.SetTarget(ax, Springs.Tilt);
            _angleY.SetTarget(ay, Springs.Tilt);
            _hover.SetTarget(tilt ? Kinetics.TiltHover * 100 : 100, Springs.Tilt);
            _shine.SetTarget(tilt && _host != null ? 100 : 0, Springs.Tilt);
            double depth = GetPressDepth(_element);
            _push.SetTarget(_down ? depth * 100 : 100, _down ? Springs.PressDown : Springs.PressRelease);
        }

        public bool Step(double dt)
        {
            bool settled = _angleX.Step(dt) & _angleY.Step(dt) & _hover.Step(dt) & _push.Step(dt) & _shine.Step(dt);
            Apply();
            return settled;
        }

        public void Finish()
        {
            foreach (var spring in new[] { _angleX, _angleY, _hover, _push, _shine }) spring.Snap();
            Apply();
        }

        public void Pose(Point? local, bool pressed)
        {
            _over = local != null;
            _down = pressed;
            if (local is { } point) _local = point;
            Retarget();
            if (_over && Tilt && !TiltOn) _host ??= MotionLayer.HostOf(_element);
            Finish();
        }

        private void Reset()
        {
            _over = _down = false;
            if (_window != null) _window.PreviewMouseLeftButtonUp -= OnWindowUp;
            MotionClock.Stop(this);
            Retarget();
            Finish();
        }

        private void Apply()
        {
            double hover = (_hover.Value - 100) / (Kinetics.TiltHover * 100 - 100); // 0 → 1 pendant le survol
            bool resting = _angleX.Value == 0 && _angleY.Value == 0 && _hover.Value == 100 && _push.Value == 100;
            var parts = resting ? MotionTransform.Existing(_element) : MotionTransform.For(_element);
            if (parts != null)
            {
                parts.Touch.ScaleX = parts.Touch.ScaleY = _hover.Value / 100 * (_push.Value / 100);
                parts.Skew.AngleX = _angleX.Value;
                parts.Skew.AngleY = _angleY.Value;
                parts.TouchShift.X = _nx * Kinetics.TiltShift * hover;
                parts.TouchShift.Y = _ny * Kinetics.TiltShift * hover;
            }
            UpdateShine();
        }

        // ------------------------------------------------------------------ reflet qui suit le curseur

        private void UpdateShine()
        {
            double opacity = _shine.Value / 100;
            if (opacity <= 0.005 || _host == null || !_element.IsVisible)
            {
                RemoveShine();
                return;
            }
            if (_element.TransformToVisual(_host) is not Transform toHost) return;
            if (_shineRect == null)
            {
                _brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
                double alpha = OpalShade.Light >= 0.5 ? Kinetics.ShineAlphaLight : Kinetics.ShineAlpha;
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), 255, 255, 255), 0));
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.35 * 255), 255, 255, 255), 0.45));
                _brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                _shineRect = new Rectangle { Fill = _brush, IsHitTestVisible = false };
                _host.Children.Add(_shineRect);
            }
            var size = _element.RenderSize;
            var shape = new GeometryGroup { Transform = toHost };
            shape.Children.Add(SquircleGeometry.Create(new Rect(size), CornerOf(_element), Radius.Smoothing));
            Geometry clip = shape;
            if (Viewport() is { } viewport) clip = new CombinedGeometry(GeometryCombineMode.Intersect, shape, new RectangleGeometry(viewport));
            _shineRect.Width = Math.Max(_host.ActualWidth, 1);
            _shineRect.Height = Math.Max(_host.ActualHeight, 1);
            _shineRect.Clip = clip;
            _shineRect.Opacity = opacity;
            var center = toHost.Transform(_local);
            _brush!.Center = _brush.GradientOrigin = center;
            _brush.RadiusX = _brush.RadiusY = Math.Max(size.Width, size.Height) * Kinetics.ShineRadius;
        }

        /// <summary>Zone visible de la liste qui défile autour de la tuile (sinon le reflet déborderait).</summary>
        private Rect? Viewport()
        {
            DependencyObject? node = VisualTreeHelper.GetParent(_element);
            while (node != null && !ReferenceEquals(node, _host))
            {
                if (node is ScrollViewer viewer && viewer.ActualWidth > 0 && viewer.TransformToVisual(_host) is { } transform)
                    return transform.TransformBounds(new Rect(viewer.RenderSize));
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        private void RemoveShine()
        {
            if (_shineRect == null) return;
            (_shineRect.Parent as Panel)?.Children.Remove(_shineRect);
            _shineRect = null;
            _brush = null;
        }

        private static CornerRadius CornerOf(FrameworkElement element) => element switch
        {
            Border border => border.CornerRadius,
            _ => Corners.Tile,
        };
    }
}
