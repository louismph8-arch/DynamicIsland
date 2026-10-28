using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Border aux coins « squircle » : la courbure varie en continu le long du coin (comme les icônes et
/// fenêtres modernes) au lieu d'un arc de cercle collé à une ligne droite. S'utilise comme un Border
/// (CornerRadius, BorderThickness uniforme, Padding, Background) ; son contenu est découpé à la même forme.
/// Un rayon ≥ la moitié du côté donne une pilule. Sans rayon, le dessin est celui d'un Border normal.
/// </summary>
public class SquircleBorder : Border
{
    public static readonly DependencyProperty SmoothingProperty = DependencyProperty.Register(
        nameof(Smoothing), typeof(double), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(Radius.Smoothing, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent), typeof(bool), typeof(SquircleBorder),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange));

    private Geometry? _childClip;

    /// <summary>Lissage du coin (0 = arc de cercle, 1 = le plus continu).</summary>
    public double Smoothing
    {
        get => (double)GetValue(SmoothingProperty);
        set => SetValue(SmoothingProperty, value);
    }

    /// <summary>Découper le contenu à la forme (vrai par défaut).</summary>
    public bool ClipContent
    {
        get => (bool)GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    private bool IsRounded => CornerRadius is { TopLeft: > 0 } or { TopRight: > 0 } or { BottomRight: > 0 } or { BottomLeft: > 0 };

    protected override void OnRender(DrawingContext context)
    {
        if (!IsRounded)
        {
            base.OnRender(context); // rectangle : bordures non uniformes possibles (liseré du haut du dock)
            return;
        }
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        double stroke = BorderThickness.Left;
        var outer = SquircleGeometry.Create(new Rect(size), CornerRadius, Smoothing);
        if (BorderBrush != null && stroke > 0)
        {
            var inner = SquircleGeometry.Create(Inner(size, stroke), Deflate(CornerRadius, stroke), Smoothing);
            context.DrawGeometry(BorderBrush, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
            if (Background != null) context.DrawGeometry(Background, null, inner);
        }
        else if (Background != null)
        {
            context.DrawGeometry(Background, null, outer);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (Child is { } child)
        {
            if (ClipContent && IsRounded && arranged.Width > 0 && arranged.Height > 0)
            {
                // La forme intérieure, exprimée dans le repère du contenu (décalé de la bordure et du remplissage).
                double stroke = BorderThickness.Left;
                var inner = Inner(arranged, stroke);
                inner.Offset(-(stroke + Padding.Left), -(stroke + Padding.Top));
                if (child.Clip == null || ReferenceEquals(child.Clip, _childClip))
                {
                    _childClip = SquircleGeometry.Create(inner, Deflate(CornerRadius, stroke), Smoothing);
                    child.Clip = _childClip;
                }
            }
            else if (_childClip != null && ReferenceEquals(child.Clip, _childClip))
            {
                child.Clip = null;
                _childClip = null;
            }
        }
        return arranged;
    }

    private static Rect Inner(Size size, double stroke) =>
        new(stroke, stroke, Math.Max(0, size.Width - 2 * stroke), Math.Max(0, size.Height - 2 * stroke));

    private static CornerRadius Deflate(CornerRadius radius, double by) => new(
        Math.Max(0, radius.TopLeft - by), Math.Max(0, radius.TopRight - by),
        Math.Max(0, radius.BottomRight - by), Math.Max(0, radius.BottomLeft - by));
}

/// <summary>
/// Contour « squircle » d'un rectangle : chaque coin = courbe de Bézier → arc → courbe de Bézier, les
/// courbes raccordant l'arc aux côtés avec une courbure qui croît progressivement (« lissage » 0 → 1).
/// Le coin prend jusqu'à (1 + lissage) × rayon de chaque côté, sans dépasser la moitié du plus petit côté :
/// au-delà, le lissage diminue (une pilule reste une pilule).
/// </summary>
public static class SquircleGeometry
{
    public static Geometry Create(Rect rect, CornerRadius radii, double smoothing)
    {
        var geometry = new StreamGeometry();
        double budget = Math.Min(rect.Width, rect.Height) / 2;
        var tl = Corner.For(radii.TopLeft, smoothing, budget);
        var tr = Corner.For(radii.TopRight, smoothing, budget);
        var br = Corner.For(radii.BottomRight, smoothing, budget);
        var bl = Corner.For(radii.BottomLeft, smoothing, budget);
        double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(left + tl.P, top), true, true);

            // Haut → coin haut-droit
            path.LineTo(new Point(right - tr.P, top), true, false);
            if (tr.R > 0)
            {
                var start = new Point(right - tr.P, top);
                var q = Draw(path, start, (tr.A, 0), (tr.A + tr.B, 0), (tr.A + tr.B + tr.C, tr.D), (tr.Arc, tr.Arc), tr.R);
                Bezier(path, q, (tr.D, tr.C), (tr.D, tr.B + tr.C), (tr.D, tr.A + tr.B + tr.C));
            }

            // Droite → coin bas-droit
            path.LineTo(new Point(right, bottom - br.P), true, false);
            if (br.R > 0)
            {
                var start = new Point(right, bottom - br.P);
                var q = Draw(path, start, (0, br.A), (0, br.A + br.B), (-br.D, br.A + br.B + br.C), (-br.Arc, br.Arc), br.R);
                Bezier(path, q, (-br.C, br.D), (-(br.B + br.C), br.D), (-(br.A + br.B + br.C), br.D));
            }

            // Bas → coin bas-gauche
            path.LineTo(new Point(left + bl.P, bottom), true, false);
            if (bl.R > 0)
            {
                var start = new Point(left + bl.P, bottom);
                var q = Draw(path, start, (-bl.A, 0), (-(bl.A + bl.B), 0), (-(bl.A + bl.B + bl.C), -bl.D), (-bl.Arc, -bl.Arc), bl.R);
                Bezier(path, q, (-bl.D, -bl.C), (-bl.D, -(bl.B + bl.C)), (-bl.D, -(bl.A + bl.B + bl.C)));
            }

            // Gauche → coin haut-gauche
            path.LineTo(new Point(left, top + tl.P), true, false);
            if (tl.R > 0)
            {
                var start = new Point(left, top + tl.P);
                var q = Draw(path, start, (0, -tl.A), (0, -(tl.A + tl.B)), (tl.D, -(tl.A + tl.B + tl.C)), (tl.Arc, -tl.Arc), tl.R);
                Bezier(path, q, (tl.C, -tl.D), (tl.B + tl.C, -tl.D), (tl.A + tl.B + tl.C, -tl.D));
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Première courbe puis l'arc du coin ; renvoie le point d'arrivée de l'arc.</summary>
    private static Point Draw(StreamGeometryContext path, Point start,
        (double X, double Y) c1, (double X, double Y) c2, (double X, double Y) end, (double X, double Y) arc, double radius)
    {
        var curveEnd = Bezier(path, start, c1, c2, end);
        var arcEnd = new Point(curveEnd.X + arc.X, curveEnd.Y + arc.Y);
        path.ArcTo(arcEnd, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        return arcEnd;
    }

    /// <summary>Courbe de Bézier dont les points sont donnés relativement à <paramref name="from"/>.</summary>
    private static Point Bezier(StreamGeometryContext path, Point from,
        (double X, double Y) c1, (double X, double Y) c2, (double X, double Y) end)
    {
        var to = new Point(from.X + end.X, from.Y + end.Y);
        path.BezierTo(new Point(from.X + c1.X, from.Y + c1.Y), new Point(from.X + c2.X, from.Y + c2.Y), to, true, false);
        return to;
    }

    /// <summary>Mesures d'un coin : longueur prise sur chaque côté (P), segments des courbes (A, B, C, D), corde de l'arc, rayon.</summary>
    private readonly record struct Corner(double P, double A, double B, double C, double D, double Arc, double R)
    {
        public static Corner For(double radius, double smoothing, double budget)
        {
            radius = Math.Min(radius, budget);
            if (radius <= 0) return default;
            smoothing = Math.Clamp(Math.Min(smoothing, budget / radius - 1), 0, 1);

            double p = (1 + smoothing) * radius;
            double arcMeasure = 90 * (1 - smoothing);
            double arcSection = Math.Sin(ToRadians(arcMeasure / 2)) * radius * Math.Sqrt(2);
            double alpha = (90 - arcMeasure) / 2;
            double p3ToP4 = radius * Math.Tan(ToRadians(alpha / 2));
            double beta = 45 * smoothing;
            double c = p3ToP4 * Math.Cos(ToRadians(beta));
            double d = c * Math.Tan(ToRadians(beta));
            double b = (p - arcSection - c - d) / 3;
            return new Corner(p, 2 * b, b, c, d, arcSection, radius);
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
