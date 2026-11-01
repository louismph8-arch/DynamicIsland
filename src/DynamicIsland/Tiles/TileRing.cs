using System.Windows;
using System.Windows.Media;
using DynamicIsland.Live;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Anneau de mesure d'une tuile vivante (Moniteur, refonte O6) : piste discrète, arc d'accent (rouge au-delà de
/// <see cref="LiveTileShape.Alert"/>) et lueur douce sous l'arc. <see cref="Fill"/> et <see cref="Breath"/> sont posés par la
/// tuile (ressort, respiration) : l'anneau ne fait que se dessiner, il n'a ni minuterie ni animation à lui.
/// <para>Couleurs : piste = encre du thème (<c>Paint.Track</c>), arc = dégradé d'accent (ressources dynamiques : elles suivent le
/// thème, l'accent et le fond d'écran sans rien recréer).</para>
/// </summary>
public sealed class TileRing : FrameworkElement
{
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush),
        typeof(TileRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(nameof(ArcBrush), typeof(Brush),
        typeof(TileRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(nameof(GlowBrush), typeof(Brush),
        typeof(TileRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly double _stroke;
    private double _fill;
    private double _breath;
    private bool _alert;

    public TileRing(double diameter, double stroke)
    {
        Width = Height = diameter;
        _stroke = stroke;
        IsHitTestVisible = false;
        SetResourceReference(TrackBrushProperty, Paint.Key(InkRole.Track));
        SetResourceReference(ArcBrushProperty, AppearanceService.AccentGradientKey);
        SetResourceReference(GlowBrushProperty, AppearanceService.AccentBrushKey);
    }

    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? ArcBrush { get => (Brush?)GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public Brush? GlowBrush { get => (Brush?)GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }

    /// <summary>Part de l'anneau remplie (0 → 1), valeur affichée (déjà passée par le ressort).</summary>
    public double Fill
    {
        get => _fill;
        set
        {
            value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (Math.Abs(value - _fill) < 1e-4) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    /// <summary>Respiration : −1 → 1 (0 = au repos). Épaisseur et lueur oscillent doucement autour de leur valeur.</summary>
    public double Breath
    {
        get => _breath;
        set
        {
            value = Math.Clamp(value, -1, 1);
            if (Math.Abs(value - _breath) < 1e-3) return;
            _breath = value;
            InvalidateVisual();
        }
    }

    /// <summary>Au-delà du seuil d'alerte : arc et lueur rouges.</summary>
    public bool Alert
    {
        get => _alert;
        set
        {
            if (_alert == value) return;
            _alert = value;
            InvalidateVisual();
        }
    }

    /// <summary>Épaisseur du trait pour une respiration donnée (−1 → 1) : ± <see cref="LiveTileShape.RingBreath"/>.</summary>
    public static double StrokeFor(double stroke, double breath) => stroke * (1 + LiveTileShape.RingBreath * Math.Clamp(breath, -1, 1));

    /// <summary>Opacité de la lueur pour une respiration donnée (−1 → 1).</summary>
    public static double GlowFor(double breath) =>
        LiveTileShape.RingGlowMin + (LiveTileShape.RingGlowMax - LiveTileShape.RingGlowMin) * (Math.Clamp(breath, -1, 1) + 1) / 2;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        // La piste et l'arc gardent la même place quelle que soit la respiration : on centre sur l'épaisseur au repos.
        double stroke = StrokeFor(_stroke, _breath);
        double center = size / 2, radius = (size - _stroke) / 2;
        if (TrackBrush is { } track) dc.DrawEllipse(null, new Pen(track, _stroke), new Point(center, center), radius, radius);
        if (_fill <= 0.002) return;

        var arc = LivePill.Ring(_fill, size, _stroke);
        var glowBrush = _alert ? Paint.Danger : GlowBrush;
        if (glowBrush != null)
        {
            dc.PushOpacity(GlowFor(_breath));
            dc.DrawGeometry(null, new Pen(glowBrush, stroke + LiveTileShape.RingGlowExtra) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            dc.Pop();
        }
        var arcBrush = _alert ? Paint.Danger : ArcBrush;
        if (arcBrush != null)
            dc.DrawGeometry(null, new Pen(arcBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}
