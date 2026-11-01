using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>Ciel dessiné par la tuile Météo, d'après le code météo WMO d'Open-Meteo.</summary>
public enum WeatherSky
{
    Clear,
    Partly,
    Cloudy,
    Fog,
    Rain,
    Snow,
    Storm,
}

/// <summary>
/// Tuile Météo vivante (O6) : un petit ciel animé d'après la VRAIE météo (<see cref="WeatherService.Shared"/> : code + jour / nuit)
/// — soleil qui rayonne doucement, nuages qui dérivent, pluie, neige, orage (éclair rare), brouillard, nuit étoilée — sur un
/// dégradé qui suit l'heure (ciel doré tôt le matin et en fin de journée). Température en chiffres fins ; min / max en W et L.
/// <para>Formes dessinées une fois (à chaque changement de ciel) ; ensuite seules leurs transformations bougent, à 30 i/s au plus,
/// page visible et île ouverte seulement (<see cref="LiveTicker"/>). Au plus <see cref="LiveTiles.MaxParticles"/> particules.</para>
/// </summary>
public sealed class WeatherTileFace : LiveTileFace
{
    private readonly TileContext _context;
    private readonly Canvas _scene = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly TextBlock _temp = new();
    private readonly TextBlock _city;
    private readonly TextBlock _description;
    private readonly TextBlock _range;
    private readonly double _width;
    private readonly double _height;
    private readonly List<Action<double>> _movers = [];
    private string _built = "";
    private bool _subscribed;

    /// <summary>--snapshot / --self-test : météo imposée (rien n'est demandé au réseau) et heure de l'exemple.</summary>
    internal WeatherReport? Sample { get; set; }
    internal DateTime? SampleTime { get; set; }

    /// <summary>Ciel dessiné en ce moment (--self-test).</summary>
    internal WeatherSky ShownSky { get; private set; }

    /// <summary>Formes animées (--self-test : jamais plus de <see cref="LiveTiles.MaxParticles"/> particules).</summary>
    internal int Particles { get; private set; }

    public WeatherTileFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size)
    {
        _context = context;
        var (columns, rows) = size.Span();
        _width = columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap;
        _height = rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap;
        bool large = size == TileSize.L;
        Numerals.Apply(_temp, size switch { TileSize.S => LiveTiles.TempS, TileSize.W => LiveTiles.TempW, _ => LiveTiles.TempL });
        _temp.Foreground = LiveTiles.OnArt;
        _temp.Margin = new Thickness(-Space.Px, -Space.S2, 0, 0); // les chiffres fins ont un blanc au-dessus : la valeur colle au coin
        _city = Text(large ? TypeRamp.Footnote : TypeRamp.Caption, FontWeights.SemiBold);
        _city.Foreground = LiveTiles.OnArt;
        _description = Text(large ? TypeRamp.Footnote : TypeRamp.Caption2, FontWeights.SemiBold);
        _description.Foreground = LiveTiles.OnArt;
        _range = Text(large ? TypeRamp.Caption : TypeRamp.Caption2, FontWeights.Normal);
        _range.Foreground = LiveTiles.OnArtSecondary;
        System.Windows.Documents.Typography.SetNumeralAlignment(_range, FontNumeralAlignment.Tabular);

        var root = new Grid();
        root.Children.Add(_scene);
        root.Children.Add(new Rectangle { Fill = Scrim(), IsHitTestVisible = false });
        root.Children.Add(BuildTexts(size));
        SetContent(root, new Thickness(0));
    }

    private Grid BuildTexts(TileSize size)
    {
        var grid = new Grid { IsHitTestVisible = false };
        switch (size)
        {
            case TileSize.S:
                // Température en haut à gauche, ville en bas.
                grid.Margin = LiveTiles.PaddingSmall;
                _temp.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(_temp);
                _city.VerticalAlignment = VerticalAlignment.Bottom;
                grid.Children.Add(_city);
                _description.Visibility = _range.Visibility = Visibility.Collapsed;
                break;
            case TileSize.W:
                // À gauche : température, ville ; à droite en bas : ciel et min / max.
                grid.Margin = LiveTiles.Padding;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _temp.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(_temp);
                _city.VerticalAlignment = VerticalAlignment.Bottom;
                grid.Children.Add(_city);
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Space.S4, 0, 0, 0) };
                _description.TextAlignment = _range.TextAlignment = TextAlignment.Right;
                right.Children.Add(_description);
                right.Children.Add(_range);
                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
                break;
            default:
                // Ville et température en haut ; ciel et min / max en bas.
                grid.Margin = LiveTiles.PaddingLarge;
                var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                top.Children.Add(_city);
                _temp.Margin = new Thickness(-Space.S1, -Space.S1, 0, 0);
                top.Children.Add(_temp);
                grid.Children.Add(top);
                var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                bottom.Children.Add(_description);
                _range.Margin = new Thickness(0, Space.Px, 0, 0);
                bottom.Children.Add(_range);
                grid.Children.Add(bottom);
                break;
        }
        return grid;
    }

    // ------------------------------------------------------------------ données

    private WeatherReport? Report => Sample ?? WeatherService.Shared.Current;

    protected override bool WantsAnimation => Report != null;

    protected override void OnShown(bool shown)
    {
        if (shown && Sample == null && !_context.Offline)
            WeatherService.Shared.RefreshIfNeeded(SettingsStore.Current.Widgets.City); // service partagé : au plus une requête toutes les 10 min
        if (shown == _subscribed) return;
        _subscribed = shown;
        if (shown) WeatherService.Shared.Changed += OnWeatherChanged;
        else WeatherService.Shared.Changed -= OnWeatherChanged;
    }

    private void OnWeatherChanged()
    {
        if (IsLive) Refresh();
    }

    protected override void Update()
    {
        var report = Report;
        var now = SampleTime ?? DateTime.Now;
        if (report == null)
        {
            // Pas encore de météo (premier lancement, hors ligne) : un ciel calme, sans animation.
            BuildScene(WeatherSky.Cloudy, night: now.Hour is < 7 or >= 19, golden: false);
            _temp.Text = "—°";
            _city.Text = string.IsNullOrWhiteSpace(SettingsStore.Current.Widgets.City) ? Loc.T("tiles.weather.name") : SettingsStore.Current.Widgets.City;
            _description.Text = WeatherService.Shared.Error != null ? Loc.T("tiles.weather.offline") : Loc.T("tiles.weather.loading");
            _range.Text = "";
            return;
        }
        var sky = SkyOf(report.Code);
        bool golden = report.IsDay && sky is WeatherSky.Clear or WeatherSky.Partly
                      && (now.Hour < LiveTiles.GoldenMorningUntil || now.Hour >= LiveTiles.GoldenEveningFrom);
        BuildScene(sky, !report.IsDay, golden);
        _temp.Text = Degrees(report.Temperature);
        _city.Text = report.City;
        _description.Text = report.Description;
        _range.Text = Loc.F("tiles.weather.range", Degrees(report.Max), Degrees(report.Min));
    }

    /// <summary>« 12° », « −3° » (vrai signe moins, dans la police des chiffres).</summary>
    internal static string Degrees(double value)
    {
        long rounded = (long)Math.Round(value, MidpointRounding.AwayFromZero);
        return (rounded < 0 ? "−" + (-rounded).ToString(Loc.Culture) : rounded.ToString(Loc.Culture)) + "°";
    }

    /// <summary>Code WMO → ciel dessiné.</summary>
    internal static WeatherSky SkyOf(int code) => code switch
    {
        0 or 1 => WeatherSky.Clear,
        2 => WeatherSky.Partly,
        45 or 48 => WeatherSky.Fog,
        >= 51 and <= 67 or >= 80 and <= 82 => WeatherSky.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherSky.Snow,
        >= 95 => WeatherSky.Storm,
        _ => WeatherSky.Cloudy,
    };

    // ------------------------------------------------------------------ ciel

    private double Scale => TileSize switch { TileSize.L => 1, TileSize.W => 0.7, _ => 0.45 };

    private void BuildScene(WeatherSky sky, bool night, bool golden)
    {
        string key = $"{sky}:{night}:{golden}";
        if (key == _built) return;
        _built = key;
        ShownSky = sky;
        _scene.Children.Clear();
        _movers.Clear();
        Particles = 0;
        _scene.Width = _width;
        _scene.Height = _height;
        var (top, bottom) = sky switch
        {
            WeatherSky.Clear or WeatherSky.Partly => night ? LiveTiles.SkyNight : golden ? LiveTiles.SkyGolden : LiveTiles.SkyClear,
            WeatherSky.Rain => night ? LiveTiles.SkyNightCloudy : LiveTiles.SkyRain,
            WeatherSky.Storm => LiveTiles.SkyStorm,
            WeatherSky.Snow => night ? LiveTiles.SkyNightCloudy : LiveTiles.SkySnow,
            WeatherSky.Fog => night ? LiveTiles.SkyNightCloudy : LiveTiles.SkyFog,
            _ => night ? LiveTiles.SkyNightCloudy : LiveTiles.SkyCloudy,
        };
        var gradient = new LinearGradientBrush(top, bottom, 90);
        gradient.Freeze();
        _scene.Background = gradient;
        var random = new Random(sky.GetHashCode() * 31 + (night ? 7 : 0) + (int)TileSize);

        bool clearish = sky is WeatherSky.Clear or WeatherSky.Partly;
        if (clearish && night) AddStars(random);
        Color cloud = sky switch
        {
            WeatherSky.Storm => LiveTiles.CloudStorm,
            _ when night => LiveTiles.CloudNight,
            WeatherSky.Partly => LiveTiles.CloudDay,
            _ => LiveTiles.CloudGrey,
        };
        int clouds = sky switch { WeatherSky.Clear => 0, WeatherSky.Partly => 2, WeatherSky.Fog => 1, WeatherSky.Snow => 2, _ => 3 };
        // Ciel dégagé de jour : un petit nuage passe loin DERRIÈRE le soleil (dessiné avant lui).
        if (sky == WeatherSky.Clear && !night) AddClouds(random, 1, cloud, far: true);
        if (clearish) AddLight(night);
        AddClouds(random, clouds, cloud, far: false);
        if (sky is WeatherSky.Rain or WeatherSky.Storm) AddRain(random, sky == WeatherSky.Storm ? 0.8 : 1);
        if (sky == WeatherSky.Snow) AddSnow(random);
        if (sky == WeatherSky.Fog) AddFog();
        if (sky == WeatherSky.Storm) AddLightning();
        Animate(LiveTicker.Now); // une première image posée (figée si les animations sont coupées)
    }

    private void AddStars(Random random)
    {
        int count = (int)Math.Round(LiveTiles.Stars * Scale);
        for (int i = 0; i < count; i++)
        {
            double size = 1.2 + random.NextDouble() * 1.2;
            var star = new Ellipse { Width = size, Height = size, Fill = Frozen(LiveTiles.Star) };
            Canvas.SetLeft(star, random.NextDouble() * _width);
            Canvas.SetTop(star, random.NextDouble() * _height * 0.62);
            _scene.Children.Add(star);
            double phase = random.NextDouble() * Math.PI * 2, speed = 0.8 + random.NextDouble() * 1.6;
            _movers.Add(t => star.Opacity = 0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(t * speed + phase)));
            Particles++;
        }
    }

    /// <summary>Soleil (halo qui respire, rayons qui tournent lentement) ou lune en croissant, en haut à droite.</summary>
    private void AddLight(bool night)
    {
        double r = TileSize switch { TileSize.L => LiveTiles.SunRadius, TileSize.W => LiveTiles.SunRadiusW, _ => LiveTiles.SunRadiusS };
        double rayWidth = TileSize == TileSize.S ? LiveTiles.RayWidthSmall : LiveTiles.RayWidth;
        double cx = _width - (TileSize == TileSize.S ? r * 0.9 : r * 2.1);
        double cy = TileSize == TileSize.S ? r * 0.9 : r * 2;
        if (night)
        {
            var glow = Disc(cx, cy, r * 2.2, new RadialGradientBrush(LiveTiles.MoonGlow, Colors.Transparent));
            _scene.Children.Add(glow);
            var crescent = new Path
            {
                Fill = Frozen(LiveTiles.Moon),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Point(cx, cy), r * 0.8, r * 0.8),
                    new EllipseGeometry(new Point(cx + r * 0.42, cy - r * 0.3), r * 0.7, r * 0.7)),
            };
            _scene.Children.Add(crescent);
            var breathe = new ScaleTransform(1, 1, cx, cy);
            glow.RenderTransform = breathe;
            _movers.Add(t => breathe.ScaleX = breathe.ScaleY = 1 + LiveTiles.SunBreath * Math.Sin(t * 0.7));
            return;
        }
        var halo = Disc(cx, cy, r * 2.6, new RadialGradientBrush(LiveTiles.SunGlow, Colors.Transparent));
        var haloScale = new ScaleTransform(1, 1, cx, cy);
        halo.RenderTransform = haloScale;
        _scene.Children.Add(halo);
        var rays = new Canvas { IsHitTestVisible = false };
        var spin = new RotateTransform(0, cx, cy);
        rays.RenderTransform = spin;
        for (int i = 0; i < LiveTiles.SunRays; i++)
        {
            double angle = i * 360.0 / LiveTiles.SunRays;
            var ray = new Rectangle
            {
                Width = rayWidth, Height = r * (i % 2 == 0 ? 0.55 : 0.38),
                RadiusX = rayWidth / 2, RadiusY = rayWidth / 2, Fill = Frozen(LiveTiles.SunRay),
                RenderTransform = new RotateTransform(angle, rayWidth / 2, r * 1.3),
            };
            Canvas.SetLeft(ray, cx - ray.Width / 2);
            Canvas.SetTop(ray, cy - r * 1.3);
            rays.Children.Add(ray);
        }
        _scene.Children.Add(rays);
        var core = Disc(cx, cy, r, new RadialGradientBrush(LiveTiles.SunCenter, LiveTiles.SunCore) { GradientOrigin = new Point(0.35, 0.3) });
        _scene.Children.Add(core);
        _movers.Add(t =>
        {
            spin.Angle = t * LiveTiles.SunSpin;
            haloScale.ScaleX = haloScale.ScaleY = 1 + LiveTiles.SunBreath * Math.Sin(t * 1.1);
        });
    }

    private void AddClouds(Random random, int count, Color color, bool far)
    {
        for (int i = 0; i < count; i++)
        {
            double width = (far ? 0.32 : 0.42 + random.NextDouble() * 0.24) * Math.Max(_width, _height) * (TileSize == TileSize.W ? 0.75 : 1);
            // Petites tuiles : les nuages passent derrière la température, plus voilés pour qu'elle reste lisible.
            double opacity = (far ? LiveTiles.CloudFar : 0.75 + 0.25 * random.NextDouble()) * (TileSize == TileSize.L ? 1 : LiveTiles.CloudSmallTile);
            var cloud = new Path { Data = CloudShape(width), Fill = Frozen(color), Opacity = opacity };
            double y = (0.04 + random.NextDouble() * 0.36) * _height;
            Canvas.SetTop(cloud, y);
            var move = new TranslateTransform();
            cloud.RenderTransform = move;
            _scene.Children.Add(cloud);
            double start = random.NextDouble() * (_width + width), speed = LiveTiles.CloudDrift * (0.6 + 0.8 * random.NextDouble());
            _movers.Add(t => move.X = Wrap(start + speed * t, _width + width) - width);
        }
    }

    /// <summary>Nuage : base arrondie + trois bosses, dessiné une fois (géométrie figée).</summary>
    private static Geometry CloudShape(double width)
    {
        double h = width * 0.42;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(new Rect(width * 0.08, h * 0.52, width * 0.84, h * 0.48), h * 0.24, h * 0.24));
        group.Children.Add(new EllipseGeometry(new Point(width * 0.32, h * 0.58), width * 0.2, h * 0.36));
        group.Children.Add(new EllipseGeometry(new Point(width * 0.56, h * 0.42), width * 0.24, h * 0.42));
        group.Children.Add(new EllipseGeometry(new Point(width * 0.76, h * 0.64), width * 0.16, h * 0.3));
        group.Freeze();
        return group;
    }

    private void AddRain(Random random, double factor)
    {
        int count = (int)Math.Round(LiveTiles.RainDrops * Scale * factor);
        var brush = Frozen(LiveTiles.Rain);
        for (int i = 0; i < count; i++)
        {
            double length = LiveTiles.RainLength * (0.7 + 0.5 * random.NextDouble());
            var drop = new Line
            {
                X1 = 0, Y1 = 0, X2 = -length * LiveTiles.RainSlant, Y2 = length,
                Stroke = brush, StrokeThickness = LiveTiles.RainWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            var move = new TranslateTransform();
            drop.RenderTransform = move;
            _scene.Children.Add(drop);
            double x0 = random.NextDouble() * (_width + _height * LiveTiles.RainSlant), y0 = random.NextDouble() * (_height + length);
            double speed = LiveTiles.RainSpeed * (0.8 + 0.4 * random.NextDouble());
            _movers.Add(t =>
            {
                double y = Wrap(y0 + speed * t, _height + length) - length;
                move.Y = y;
                move.X = x0 - y * LiveTiles.RainSlant;
            });
            Particles++;
        }
    }

    private void AddSnow(Random random)
    {
        int count = (int)Math.Round(LiveTiles.SnowFlakes * Scale);
        var brush = Frozen(LiveTiles.Snow);
        for (int i = 0; i < count; i++)
        {
            double size = 2 + random.NextDouble() * 1.8;
            var flake = new Ellipse { Width = size, Height = size, Fill = brush };
            var move = new TranslateTransform();
            flake.RenderTransform = move;
            _scene.Children.Add(flake);
            double x0 = random.NextDouble() * _width, y0 = random.NextDouble() * (_height + size);
            double speed = LiveTiles.SnowSpeed * (0.6 + 0.8 * random.NextDouble()), phase = random.NextDouble() * Math.PI * 2;
            _movers.Add(t =>
            {
                move.Y = Wrap(y0 + speed * t, _height + size) - size;
                move.X = x0 + LiveTiles.SnowSway * Math.Sin(t * 0.9 + phase);
            });
            Particles++;
        }
    }

    private void AddFog()
    {
        for (int i = 0; i < 3; i++)
        {
            // Nappe douce : fondue en haut et en bas (remplissage), et sur les côtés (masque) — pas de bord net.
            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            fade.GradientStops.Add(new GradientStop(LiveTiles.FogBand, 0.5));
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            fade.Freeze();
            var sides = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            sides.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            sides.GradientStops.Add(new GradientStop(Colors.Black, 0.3));
            sides.GradientStops.Add(new GradientStop(Colors.Black, 0.7));
            sides.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            sides.Freeze();
            double height = _height * (0.26 + 0.06 * i);
            var band = new Rectangle { Width = _width * 1.4, Height = height, Fill = fade, OpacityMask = sides };
            Canvas.SetTop(band, _height * (0.18 + 0.2 * i));
            Canvas.SetLeft(band, -_width * 0.2);
            var move = new TranslateTransform();
            band.RenderTransform = move;
            _scene.Children.Add(band);
            int index = i;
            _movers.Add(t => move.X = _width * 0.08 * Math.Sin(t * (0.18 + 0.05 * index) + index * 2.1));
        }
    }

    /// <summary>Orage : un éclair rare (lueur sur tout le ciel + zigzag), deux battements très courts.</summary>
    private void AddLightning()
    {
        var flash = new Rectangle { Width = _width, Height = _height, Fill = Frozen(Swatch.White), Opacity = 0 };
        double x = _width * 0.62, y = _height * 0.3, s = Math.Min(_width, _height) / 92.0;
        var bolt = new Path
        {
            Fill = Frozen(LiveTiles.Bolt), Opacity = 0,
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {x},{y} l {-9 * s},{18 * s} l {6 * s},0 l {-5 * s},{15 * s} l {13 * s},{-20 * s} l {-6 * s},0 l {5 * s},{-13 * s} Z")),
        };
        bolt.Data.Freeze();
        _scene.Children.Add(flash);
        _scene.Children.Add(bolt);
        double every = LiveTiles.FlashEvery.TotalSeconds, length = LiveTiles.FlashLength.TotalSeconds;
        _movers.Add(t =>
        {
            // Chaque cycle a son propre décalage (l'éclair ne tombe pas comme un métronome).
            long cycle = (long)Math.Floor(t / every);
            double offset = (Math.Abs(Math.Sin(cycle * 12.9898) * 43758.5453) % 1) * every * 0.5;
            double local = t - cycle * every - offset;
            double level = local is >= 0 and < 1 ? Pulse(local / length) : 0;
            flash.Opacity = LiveTiles.FlashOpacity * level;
            bolt.Opacity = level;
        });
    }

    /// <summary>Deux battements : 0–0,35 puis 0,55–1 (0 ailleurs).</summary>
    private static double Pulse(double p) => p switch
    {
        < 0 or >= 1 => 0,
        < 0.35 => Math.Sin(p / 0.35 * Math.PI),
        < 0.55 => 0,
        _ => 0.7 * Math.Sin((p - 0.55) / 0.45 * Math.PI),
    };

    protected override void Animate(double seconds)
    {
        foreach (var move in _movers) move(seconds);
    }

    private static Ellipse Disc(double cx, double cy, double radius, Brush fill)
    {
        if (fill.CanFreeze) fill.Freeze();
        var disc = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(disc, cx - radius);
        Canvas.SetTop(disc, cy - radius);
        return disc;
    }

    private static double Wrap(double value, double range) => range <= 0 ? 0 : (value % range + range) % range;

    private static Brush Frozen(Color color) => Paint.Frozen(color);
}
