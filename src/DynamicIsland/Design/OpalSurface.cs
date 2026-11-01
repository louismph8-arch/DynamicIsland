using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>
/// Pinceaux partagés par toutes les surfaces d'une intensité : la bordure irisée et le liseré du haut bougent avec la
/// souris (une seule mise à jour pour toute la colonne), le reste est figé.
/// </summary>
public sealed class OpalPaint
{
    public OpalLook Look { get; }
    public LinearGradientBrush Rim { get; }
    public LinearGradientBrush TopLine { get; }
    public Brush Smoke { get; }
    public Brush Tint { get; }
    public Brush Milk { get; }
    public Brush Grain { get; }
    public Brush Sweep { get; }

    /// <param name="accent">Couleur imposée (pochette de l'album) : les reflets irisés tournent autour d'elle.</param>
    public OpalPaint(OpalLook look, ImageSource grain, Color? accent = null)
    {
        Look = look;
        Color start = AppearanceService.AccentStart, end = AppearanceService.AccentEnd;
        var irid = OpalPalette.Iridescent(accent ?? AppearanceService.AccentMid, look.IridShift, look.IridPale);
        Rim = new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(OpalPalette.WithOpacity(irid[0], look.IridAlpha), 0),
                new GradientStop(OpalPalette.WithOpacity(irid[1], look.IridAlpha * 0.55), 0.5),
                new GradientStop(OpalPalette.WithOpacity(irid[2], look.IridAlpha), 1),
            },
        };
        var white = Swatch.White;
        TopLine = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(OpalPalette.WithOpacity(white, look.TopLine * 0.15), 0),
                new GradientStop(OpalPalette.WithOpacity(white, look.TopLine), 0.5),
                new GradientStop(OpalPalette.WithOpacity(white, look.TopLine * 0.15), 1),
            },
        };
        Smoke = Paint.Frozen(OpalPalette.WithOpacity(AppearanceService.SurfaceBase, look.Smoke));
        Tint = Paint.Gradient(OpalPalette.WithOpacity(start, look.Tint), OpalPalette.WithOpacity(end, look.Tint * 0.7), 35);
        var milk = new LinearGradientBrush(OpalPalette.WithOpacity(white, look.MilkTop), OpalPalette.WithOpacity(white, look.MilkBottom), 90);
        milk.Freeze();
        Milk = milk;
        var tile = new ImageBrush(grain)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, Opal.GrainSize, Opal.GrainSize), AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
        };
        tile.Freeze();
        Grain = tile;
        var sweep = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(OpalPalette.WithOpacity(white, 0), 0),
                new GradientStop(OpalPalette.WithOpacity(Opal_Pearl(irid), look.SweepAlpha * 0.6), 0.35),
                new GradientStop(OpalPalette.WithOpacity(white, look.SweepAlpha), 0.5),
                new GradientStop(OpalPalette.WithOpacity(Opal_Pearl(irid), look.SweepAlpha * 0.6), 0.65),
                new GradientStop(OpalPalette.WithOpacity(white, 0), 1),
            },
        };
        sweep.Freeze();
        Sweep = sweep;
        SetLight(0.5, 0.5);
    }

    /// <summary>Lumière à la position (0–1) de la souris dans la fenêtre : orientation de la bordure, reflet du liseré.</summary>
    public void SetLight(double fx, double fy)
    {
        _fx = fx;
        _fy = fy;
        var (from, to) = OpalPalette.Direction(Opal.IridBaseAngle + (fx - 0.5) * Opal.IridSwing + _breathAngle);
        Rim.StartPoint = from;
        Rim.EndPoint = to;
        Rim.GradientStops[1].Offset = Math.Clamp(0.5 + (fy - 0.5) * Opal.IridSlide * 2, 0.15, 0.85);
        TopLine.GradientStops[1].Offset = Math.Clamp(fx, 0.12, 0.88);
    }

    private double _fx = 0.5, _fy = 0.5, _breathAngle;

    /// <summary>Respiration (O5) : reflets tournés de <paramref name="angle"/> degrés, éclat de la bordure × <paramref name="glow"/>.</summary>
    public void SetBreath(double angle, double glow)
    {
        _breathAngle = angle;
        Rim.Opacity = Math.Clamp(glow, 0, 1);
        SetLight(_fx, _fy);
    }

    /// <summary>Reflet nacré du balayage : la teinte voisine « après » l'accent, très pâle.</summary>
    private static Color Opal_Pearl(Color[] irid) => OpalPalette.Mix(irid[2], Swatch.White, 0.5);
}

/// <summary>
/// Une surface en verre opalin : voile sombre + teinte d'accent + voile laiteux + grain, bordure irisée, liseré du haut,
/// ombre douce dessous, lueur au survol (si une couleur est donnée) et balayage nacré. Le flou du décor est dessiné
/// DERRIÈRE par la fenêtre (couche floutée découpée à la forme de <see cref="Body"/>).
/// </summary>
public sealed class OpalSurface : Grid
{
    private readonly OpalPaint _paint;
    private readonly Grid _halo = new() { IsHitTestVisible = false };
    private readonly SquircleBorder _glow;
    private readonly Rectangle _sweep;
    private readonly TranslateTransform _sweepMove = new();
    private readonly Grid _layers = new();

    /// <summary>La forme elle-même (pour découper la couche floutée).</summary>
    public SquircleBorder Body { get; }
    public CornerRadius Corners { get; }

    public OpalSurface(OpalPaint paint, double width, double height, double radius, UIElement? content, Color? glow = null)
    {
        _paint = paint;
        Width = width;
        Height = height;
        Corners = new CornerRadius(Math.Min(radius, Math.Min(width, height) / 2));
        var look = paint.Look;

        // Ombre et lueur : formes pleines sous la surface, floutées, dont l'intérieur est retiré par un découpage
        // (sinon l'ombre se verrait à travers le verre translucide).
        var shadow = new SquircleBorder { CornerRadius = Corners, Background = Paint.Frozen(Swatch.Black), Effect = look.Shadow.Create() };
        _halo.Children.Add(shadow);
        if (glow is { } color)
        {
            _glow = new SquircleBorder
            {
                CornerRadius = Corners, Background = Paint.Frozen(color), Opacity = 0,
                Effect = new DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = look.GlowBlur, Opacity = look.GlowOpacity, RenderingBias = RenderingBias.Performance },
            };
            _halo.Children.Add(_glow);
            MouseEnter += (_, _) => FadeGlow(1);
            MouseLeave += (_, _) => FadeGlow(0);
        }
        else _glow = null!;
        Children.Add(_halo);

        _layers.Children.Add(new Rectangle { Fill = paint.Tint });
        _layers.Children.Add(new Rectangle { Fill = paint.Milk });
        _layers.Children.Add(new Rectangle { Fill = paint.Grain, Opacity = look.Grain }); // 2 à 6 %
        _layers.Children.Add(new Rectangle { Fill = paint.TopLine, Height = Stroke.Hairline, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(radius * 0.4, 0, radius * 0.4, 0) });
        if (content != null) _layers.Children.Add(content);
        _sweep = new Rectangle
        {
            Fill = paint.Sweep, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Width = Math.Max(24, width * look.SweepWidth), Height = height * 2.2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new RotateTransform(Opal.SweepAngle), _sweepMove } },
        };
        _layers.Children.Add(_sweep);

        Body = new SquircleBorder
        {
            CornerRadius = Corners, Background = paint.Smoke, BorderBrush = paint.Rim,
            BorderThickness = new Thickness(look.IridWidth), Child = _layers,
        };
        Children.Add(Body);
        SizeChanged += (_, _) => UpdateHaloClip();
        UpdateHaloClip();
    }

    /// <summary>Ombre et lueur : tout autour de la forme, jamais dessous.</summary>
    private void UpdateHaloClip()
    {
        double w = Width, h = Height, reach = Opal.HaloReach;
        var shape = SquircleGeometry.Create(new Rect(0, 0, w, h), Corners, Radius.Smoothing);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-reach, -reach, w + 2 * reach, h + 2 * reach)), shape);
        outside.Freeze();
        _halo.Clip = outside;
    }

    private void FadeGlow(double to)
    {
        if (_glow == null) return;
        _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Opal.GlowFade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Lueur allumée sans animation (rendu --snapshot).</summary>
    public void ShowGlow()
    {
        if (_glow != null) _glow.Opacity = 1;
    }

    /// <summary>Balayage nacré : un reflet traverse la surface (~600 ms), puis plus rien ne tourne.</summary>
    public void PlaySweep()
    {
        double travel = Width + _sweep.Width * 2;
        _sweep.Visibility = Visibility.Visible;
        var run = new DoubleAnimation(-_sweep.Width * 1.5, travel - _sweep.Width * 1.5, Opal.Sweep)
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
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
        double travel = Width + _sweep.Width * 2;
        _sweep.Visibility = Visibility.Visible;
        _sweepMove.X = -_sweep.Width * 1.5 + travel * t;
    }
}
