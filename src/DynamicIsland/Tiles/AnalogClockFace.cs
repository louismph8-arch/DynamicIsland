using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuile Horloge analogique (O6) : cadran opalin, repères, chiffres en L, aiguilles fines et trotteuse FLUIDE à l'accent (elle
/// glisse en continu, sans saut). Dessinée une fois ; ensuite seules les rotations des aiguilles changent (RotateTransform), à
/// 30 i/s au plus, tuile à l'écran seulement. Animations coupées (« Réduire les animations », jeu, verre coupé) : pas de
/// trotteuse, heures et minutes relues chaque seconde par la grille.
/// </summary>
public sealed class AnalogClockFace : LiveTileFace
{
    private readonly RotateTransform _hour;
    private readonly RotateTransform _minute;
    private readonly RotateTransform _second;
    private readonly UIElement _secondHand;
    private readonly TextBlock? _date;
    private DateTime _dateShown = DateTime.MinValue; // jour affiché par _date

    /// <summary>--snapshot / --self-test : heure imposée (image figée).</summary>
    internal DateTime? SampleTime { get; set; }

    public AnalogClockFace(TileDefinition definition, TileSize size) : base(definition, size)
    {
        bool large = size != TileSize.S;
        var (columns, rows) = size.Span();
        double width = columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap;
        double height = rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap;
        double side = Math.Min(width, height);
        double r = side / 2 - LiveTiles.DialInset;
        double cx = width / 2, cy = height / 2;
        double k = large ? 1 : LiveTiles.SmallDial; // traits plus fins sur le petit cadran
        var canvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false };

        // Cadran : un disque de verre à peine plus clair que la tuile, liseré fin.
        var dial = new Ellipse { Width = r * 2, Height = r * 2, StrokeThickness = Stroke.Hairline };
        dial.SetResourceReference(Shape.FillProperty, Paint.Key(InkRole.Card));
        dial.SetResourceReference(Shape.StrokeProperty, Paint.Key(InkRole.CardBorder));
        Canvas.SetLeft(dial, cx - r);
        Canvas.SetTop(dial, cy - r);
        canvas.Children.Add(dial);

        // Repères : 12 heures (les quarts plus longs), et les minutes en L. Deux géométries figées.
        var major = new StreamGeometry();
        var minor = new StreamGeometry();
        using (var majorContext = major.Open())
        using (var minorContext = minor.Open())
        {
            for (int i = 0; i < 60; i++)
            {
                bool hour = i % 5 == 0;
                if (!hour && !large) continue;
                double length = hour ? LiveTiles.TickMajor * (i % 15 == 0 ? LiveTiles.TickQuarter : 1) : LiveTiles.TickMinor;
                double angle = i * Math.PI / 30;
                double outer = r - Space.S2 * k, inner = outer - length * r;
                var context = hour ? majorContext : minorContext;
                context.BeginFigure(new Point(cx + Math.Sin(angle) * inner, cy - Math.Cos(angle) * inner), false, false);
                context.LineTo(new Point(cx + Math.Sin(angle) * outer, cy - Math.Cos(angle) * outer), true, false);
            }
        }
        major.Freeze();
        minor.Freeze();
        canvas.Children.Add(Ticks(major, LiveTiles.TickMajorWidth * k, InkRole.TextSecondary));
        if (large) canvas.Children.Add(Ticks(minor, LiveTiles.TickMinorWidth, InkRole.TextFaint));

        if (large)
        {
            // Chiffres des heures (police des chiffres, fins) et date sous l'axe.
            for (int hour = 1; hour <= 12; hour++)
            {
                double angle = hour * Math.PI / 6;
                var number = Numerals.Apply(new TextBlock { Text = hour.ToString(Loc.Culture) }, TypeRamp.Caption);
                number.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(hour % 3 == 0 ? InkRole.TextPrimary : InkRole.TextTertiary));
                number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(number, cx + Math.Sin(angle) * r * LiveTiles.Numbers - number.DesiredSize.Width / 2);
                Canvas.SetTop(number, cy - Math.Cos(angle) * r * LiveTiles.Numbers - number.DesiredSize.Height / 2);
                canvas.Children.Add(number);
            }
            _date = new TextBlock { FontSize = TypeRamp.Caption2, FontWeight = FontWeights.SemiBold, Width = r, TextAlignment = TextAlignment.Center };
            _date.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
            System.Windows.Documents.Typography.SetNumeralAlignment(_date, FontNumeralAlignment.Tabular);
            Canvas.SetLeft(_date, cx - r / 2);
            Canvas.SetTop(_date, cy + r * LiveTiles.DateOffset - TypeRamp.Caption2 * TileGridShape.LineHeight / 2);
            canvas.Children.Add(_date);
        }

        // Aiguilles : un trait arrondi chacune, tournée autour du centre (rien n'est redessiné).
        canvas.Children.Add(Hand(cx, cy, 0, LiveTiles.HourHand * r, LiveTiles.HourWidth * k, Paint.Key(InkRole.TextPrimary), out _hour));
        canvas.Children.Add(Hand(cx, cy, 0, LiveTiles.MinuteHand * r, LiveTiles.MinuteWidth * k, Paint.Key(InkRole.TextPrimary), out _minute));
        canvas.Children.Add(Hub(cx, cy, LiveTiles.HubSize * k, Paint.Key(InkRole.TextPrimary)));
        var second = new Canvas { IsHitTestVisible = false };
        second.Children.Add(Hand(cx, cy, LiveTiles.SecondTail * r, LiveTiles.SecondHand * r, LiveTiles.SecondWidth * Math.Max(k, LiveTiles.SecondMinScale),
            AppearanceService.AccentBrushKey, out _second));
        second.Children.Add(Hub(cx, cy, LiveTiles.HubSecond, AppearanceService.AccentBrushKey));
        _secondHand = second;
        canvas.Children.Add(second);

        SetContent(canvas, new Thickness(0));
    }

    private static Path Ticks(Geometry geometry, double width, InkRole role)
    {
        var path = new Path { Data = geometry, StrokeThickness = width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(Shape.StrokeProperty, Paint.Key(role));
        return path;
    }

    private static Line Hand(double cx, double cy, double tail, double length, double width, string brushKey, out RotateTransform rotation)
    {
        rotation = new RotateTransform(0, cx, cy);
        var line = new Line
        {
            X1 = cx, Y1 = cy + tail, X2 = cx, Y2 = cy - length, StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = rotation,
        };
        line.SetResourceReference(Shape.StrokeProperty, brushKey);
        return line;
    }

    private static Ellipse Hub(double cx, double cy, double size, string brushKey)
    {
        var hub = new Ellipse { Width = size, Height = size };
        hub.SetResourceReference(Shape.FillProperty, brushKey);
        Canvas.SetLeft(hub, cx - size / 2);
        Canvas.SetTop(hub, cy - size / 2);
        return hub;
    }

    // ------------------------------------------------------------------ temps

    protected override bool WantsAnimation => true; // la trotteuse

    protected override void Update()
    {
        bool smooth = LiveTicker.Allowed || SampleTime != null;
        _secondHand.Visibility = smooth ? Visibility.Visible : Visibility.Collapsed;
        Show(SampleTime ?? DateTime.Now);
    }

    protected override void Animate(double seconds) => Show(SampleTime ?? DateTime.Now);

    /// <summary>Angles des aiguilles (calcul pur, --self-test) : heures, minutes, secondes en degrés depuis midi.</summary>
    internal static (double Hour, double Minute, double Second) Angles(DateTime time)
    {
        double second = time.Second + time.Millisecond / 1000.0; // continu : la trotteuse glisse
        double minute = time.Minute + second / 60;
        double hour = time.Hour % 12 + minute / 60;
        return (hour * 30, minute * 6, second * 6);
    }

    private void Show(DateTime time)
    {
        var (hour, minute, second) = Angles(time);
        _hour.Angle = hour;
        _minute.Angle = minute;
        _second.Angle = second;
        if (_date == null) return;
        // La trotteuse tourne à 30 i/s : la date n'est reformatée (chaînes allouées) que quand le jour change.
        if (time.Date == _dateShown) return;
        _dateShown = time.Date;
        _date.Text = time.ToString(Loc.T("tiles.clock.date_format"), Loc.Culture).ToUpper(Loc.Culture);
    }

    /// <summary>--self-test : angles affichés.</summary>
    internal (double Hour, double Minute, double Second) ShownAngles => (_hour.Angle, _minute.Angle, _second.Angle);
    internal bool ShowsSecondHand => _secondHand.Visibility == Visibility.Visible;
}
