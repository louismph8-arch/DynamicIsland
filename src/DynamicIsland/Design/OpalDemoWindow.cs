using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>
/// Démo du « verre opalin » (refonte O1) : les trois intensités côte à côte, chacune avec une île (pilule + île ouverte),
/// des tuiles S / W / L, un bouton, un interrupteur et un dock d'exemple, sur le fond d'écran de l'utilisateur. L'utilisateur
/// choisit son intensité (réglage Appearance.OpalStyle), appliquée à toute l'app à l'étape suivante.
/// Flou : la vraie île est une fenêtre « layered » qui ne peut pas flouter ce qu'il y a derrière elle ; ici il est SIMULÉ avec une
/// copie réduite et floutée du décor, découpée à la forme de chaque surface (même image, même place que le décor net).
/// Rien ne tourne au repos : la minuterie des reflets ne part que quand la souris bouge et s'arrête une fois arrivée.
/// </summary>
public sealed class OpalDemoWindow : Window
{
    public enum Decor { Current, Light, Dark, Colorful }

    private static OpalDemoWindow? _instance;

    private sealed class Column
    {
        public required OpalStyle Style { get; init; }
        public required OpalPaint Paint { get; init; }
        public required Image BlurLayer { get; init; }
        public List<OpalSurface> Surfaces { get; } = [];
        public List<OpalSurface> Tiles { get; } = [];
        public OpalSurface? Island { get; set; }
    }

    private readonly bool _snapshot;
    private readonly Grid _root = new() { ClipToBounds = true };
    private readonly Image _decor = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly List<Column> _columns = [];
    private readonly Dictionary<Decor, SquircleBorder> _decorChips = [];
    private readonly DispatcherTimer _follow;
    private Decor _current;
    private Point _light = new(0.5, 0.5), _target = new(0.5, 0.5);
    private int _decorRequest;

    public static void Open()
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }
        _instance = new OpalDemoWindow(snapshot: false);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
    }

    private OpalDemoWindow(bool snapshot)
    {
        _snapshot = snapshot;
        Title = Loc.T("opal.demo.window");
        Width = Opal.DemoWidth;
        Height = Opal.DemoHeight;
        MinWidth = Opal.DemoWidth * 0.6;
        MinHeight = Opal.DemoHeight * 0.6;
        Background = Paint.Frozen(Swatch.Black);
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;
        UseLayoutRounding = true;
        if (snapshot)
        {
            // Rendu hors écran, sans cadre (l'image = le contenu).
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -30000;
            Top = 0;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = Opal.FollowTick };
        _follow.Tick += (_, _) => FollowStep();

        var grain = OpalPalette.NoiseImage();
        _root.Children.Add(_decor);
        RenderOptions.SetBitmapScalingMode(_decor, BitmapScalingMode.HighQuality);
        foreach (var style in new[] { OpalStyle.Discreet, OpalStyle.Balanced, OpalStyle.Intense })
        {
            var layer = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(layer, BitmapScalingMode.HighQuality);
            _root.Children.Add(layer);
            _columns.Add(new Column { Style = style, Paint = new OpalPaint(Opal.For(style), grain), BlurLayer = layer });
        }
        _root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = BuildCanvas() });
        Content = _root;

        _root.SizeChanged += (_, _) => ScheduleClips();
        MouseMove += OnMouseMove;
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            DockNative.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DockNative.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };
        Closed += (_, _) =>
        {
            _follow.Stop();
            MouseMove -= OnMouseMove;
        };
        if (!snapshot)
        {
            Loaded += async (_, _) =>
            {
                await ShowDecorAsync(WallpaperService.CurrentPath() != null ? Decor.Current : Decor.Colorful);
                ReplaySweeps(); // l'île d'exemple « s'ouvre » : un balayage la traverse
            };
        }
        ScheduleClips();
    }

    // ------------------------------------------------------------------ mise en page

    private Grid BuildCanvas()
    {
        var canvas = new Grid { Width = Opal.DemoCanvasWidth, Height = Opal.DemoCanvasHeight };
        canvas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvas.RowDefinitions.Add(new RowDefinition());
        canvas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        canvas.Children.Add(Header());

        var columns = new Grid { Margin = new Thickness(0, Space.S8, 0, Space.S4) };
        for (int i = 0; i < _columns.Count; i++)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var built = BuildColumn(_columns[i]);
            Grid.SetColumn(built, i);
            columns.Children.Add(built);
        }
        Grid.SetRow(columns, 1);
        canvas.Children.Add(columns);

        var note = new SquircleBorder
        {
            Background = Opal.Plate, CornerRadius = Themes.Corners.Pill, HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(Space.S8, Space.S2, Space.S8, Space.S2),
            Child = new TextBlock { Text = Loc.T("opal.demo.note"), FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary },
        };
        Grid.SetRow(note, 2);
        canvas.Children.Add(note);
        return canvas;
    }

    private FrameworkElement Header()
    {
        var plate = new SquircleBorder
        {
            Background = Opal.Plate, CornerRadius = Themes.Corners.XXL, Margin = new Thickness(Space.S12, 0, Space.S12, 0),
            Padding = new Thickness(Space.S7, Space.S5, Space.S7, Space.S5),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = OpalineLogo.Get((int)Opal.Logo * 2), Width = Opal.Logo, Height = Opal.Logo, VerticalAlignment = VerticalAlignment.Center });

        var texts = new StackPanel { Margin = new Thickness(Space.S6, 0, Space.S6, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = Loc.T("opal.demo.title"), FontFamily = FontStack.Display, FontSize = TypeRamp.Title2, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = Loc.T("opal.demo.subtitle"), FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(new TextBlock { Text = Loc.T("opal.demo.decor"), FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S4, 0) });
        foreach (var (decor, label) in new[]
                 {
                     (Decor.Current, Loc.T("opal.demo.decor.current")), (Decor.Light, Loc.T("opal.demo.decor.light")),
                     (Decor.Dark, Loc.T("opal.demo.decor.dark")), (Decor.Colorful, Loc.T("opal.demo.decor.colorful")),
                 })
        {
            var chip = Chip(label, null, () => _ = ShowDecorAsync(decor));
            chip.Margin = new Thickness(0, 0, Space.S2, 0);
            _decorChips[decor] = chip;
            actions.Children.Add(chip);
        }
        var replay = Chip(Loc.T("opal.demo.replay"), Icons.Sparkles, ReplaySweeps);
        replay.Margin = new Thickness(Space.S6, 0, 0, 0);
        actions.Children.Add(replay);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        plate.Child = grid;
        return plate;
    }

    /// <summary>Petit bouton neutre de l'en-tête (pas en verre opalin : ce n'est pas ce qu'on compare).</summary>
    private static SquircleBorder Chip(string text, string? icon, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null)
            content.Children.Add(new TextBlock { Text = icon, FontFamily = FontStack.Icons, FontSize = IconSize.M, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S3, 0) });
        content.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Footnote, VerticalAlignment = VerticalAlignment.Center });
        var chip = new SquircleBorder
        {
            CornerRadius = Themes.Corners.Pill, Background = Paint.Control, Cursor = Cursors.Hand,
            Padding = new Thickness(Space.S6, Space.S3, Space.S6, Space.S3), Child = content,
        };
        chip.MouseEnter += (_, _) => chip.Opacity = 0.85;
        chip.MouseLeave += (_, _) => chip.Opacity = 1;
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return chip;
    }

    private FrameworkElement BuildColumn(Column column)
    {
        var paint = column.Paint;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = Opal.ColumnWidth };

        // Île : pilule réduite puis île ouverte
        var pill = Add(column, new OpalSurface(paint, Opal.Pill.Width, Opal.Pill.Height, Radius.Pill, PillContent()));
        pill.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(pill);
        var island = Add(column, new OpalSurface(paint, Opal.IslandOpen.Width, Opal.IslandOpen.Height, Opal.PanelRadius, IslandContent()));
        island.HorizontalAlignment = HorizontalAlignment.Center;
        island.Margin = new Thickness(0, Space.S6, 0, 0);
        column.Island = island;
        stack.Children.Add(island);

        // Tuiles « bento » : Moniteur L (2 × 2), Musique W (2 × 1), Météo et Horloge S
        double u = Opal.TileUnit, g = Opal.TileGap, w2 = 2 * u + g;
        var tiles = new Grid { Width = 4 * u + 3 * g, Height = w2, Margin = new Thickness(0, Space.S8, 0, 0) };
        PlaceTile(column, tiles, new OpalSurface(paint, w2, w2, Opal.TileRadius, MonitorTile(), Swatch.Green), 0, 0);
        PlaceTile(column, tiles, new OpalSurface(paint, w2, u, Opal.TileRadius, MusicTile(), AppearanceService.AccentStart), u * 2 + g * 2, 0);
        PlaceTile(column, tiles, new OpalSurface(paint, u, u, Opal.TileRadius, WeatherTile(), Swatch.Teal), u * 2 + g * 2, u + g);
        PlaceTile(column, tiles, new OpalSurface(paint, u, u, Opal.TileRadius, ClockTile(), AppearanceService.AccentEnd), u * 3 + g * 3, u + g);
        stack.Children.Add(tiles);

        // Bouton + interrupteur
        var controls = new Grid { Width = tiles.Width, Margin = new Thickness(0, Space.S7, 0, 0) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        var button = Add(column, new OpalSurface(paint, 120, 36, Opal.ButtonRadius,
            new TextBlock { Text = Loc.T("opal.demo.button"), FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            AppearanceService.AccentMid));
        button.Cursor = Cursors.Hand;
        button.MouseLeftButtonUp += (_, _) => button.PlaySweep();
        controls.Children.Add(button);
        var switchRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        switchRow.Children.Add(new TextBlock { Text = Loc.T("opal.demo.switch"), FontSize = TypeRamp.Body, Foreground = Paint.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S5, 0) });
        switchRow.Children.Add(OpalSwitch(paint));
        var switchPlate = Add(column, new OpalSurface(paint, 168, 36, Opal.ButtonRadius,
            new Border { Padding = new Thickness(Space.S6, 0, Space.S4, 0), Child = switchRow }));
        switchPlate.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(switchPlate, 1);
        controls.Children.Add(switchPlate);
        stack.Children.Add(controls);

        // Dock
        var dock = Add(column, new OpalSurface(paint, Opal.Dock.Width, Opal.Dock.Height, Opal.DockRadius, DockContent()));
        dock.HorizontalAlignment = HorizontalAlignment.Center;
        dock.Margin = new Thickness(0, Space.S7, 0, 0);
        stack.Children.Add(dock);

        // Nom de l'intensité + « Choisir ce style »
        var footer = Add(column, new OpalSurface(paint, Opal.IslandOpen.Width, 84, Opal.PanelRadius, FooterContent(column)));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        footer.Margin = new Thickness(0, Space.S8, 0, 0);
        stack.Children.Add(footer);
        return stack;
    }

    private static OpalSurface Add(Column column, OpalSurface surface)
    {
        column.Surfaces.Add(surface);
        return surface;
    }

    private static void PlaceTile(Column column, Grid grid, OpalSurface tile, double x, double y)
    {
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Margin = new Thickness(x, y, 0, 0);
        column.Surfaces.Add(tile);
        column.Tiles.Add(tile);
        grid.Children.Add(tile);
    }

    // ------------------------------------------------------------------ contenus d'exemple

    private static TextBlock Text(string text, double size, Brush? brush = null, FontWeight? weight = null, FontFamily? family = null) => new()
    {
        Text = text, FontSize = size, Foreground = brush ?? Paint.TextPrimary, FontWeight = weight ?? FontWeights.Normal,
        FontFamily = family ?? FontStack.Ui, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock Glyph(string icon, double size, Brush? brush = null) => new()
    {
        Text = icon, FontFamily = FontStack.Icons, FontSize = size, Foreground = brush ?? Paint.TextPrimary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Brush AccentGradient => Paint.Gradient(AppearanceService.AccentStart, AppearanceService.AccentEnd, 45);

    private static UIElement PillContent()
    {
        var grid = new Grid { Margin = new Thickness(Space.S8, 0, Space.S8, 0) };
        var time = Text("14:32", TypeRamp.Headline, weight: FontWeights.SemiBold, family: FontStack.Display);
        time.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(time);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (double h in new[] { 8.0, 14, 10, 16, 7 })
            bars.Children.Add(new Rectangle { Width = 3, Height = h, RadiusX = 1.5, RadiusY = 1.5, Fill = AccentGradient, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(bars);
        return grid;
    }

    private static UIElement IslandContent()
    {
        var grid = new Grid { Margin = new Thickness(Space.S9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new SquircleBorder
        {
            Width = Opal.Cover, Height = Opal.Cover, CornerRadius = new CornerRadius(Opal.InnerRadius), Background = AccentGradient,
            Child = Glyph(Icons.Music, IconSize.Huge, Paint.Frozen(Swatch.WhiteA(0xD0))),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ((TextBlock)cover.Child).HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(cover);

        var right = new StackPanel { Margin = new Thickness(Space.S8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Text(Loc.T("opal.demo.sample.title"), TypeRamp.Title3, weight: FontWeights.SemiBold));
        right.Children.Add(Text(Loc.T("opal.demo.sample.artist"), TypeRamp.Callout, Paint.TextSecondary));
        var track = new Grid { Height = Sizes.Bar, Margin = new Thickness(0, Space.S6, 0, Space.S2) };
        track.Children.Add(new Rectangle { Fill = Paint.Track, RadiusX = 2, RadiusY = 2 });
        track.Children.Add(new Rectangle { Fill = AccentGradient, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, Width = 82 });
        right.Children.Add(track);
        var times = new Grid();
        times.Children.Add(Text("1:12", TypeRamp.Caption2, Paint.TextTertiary));
        var total = Text("3:45", TypeRamp.Caption2, Paint.TextTertiary);
        total.HorizontalAlignment = HorizontalAlignment.Right;
        times.Children.Add(total);
        right.Children.Add(times);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S1, 0, 0) };
        foreach (var (icon, size) in new[] { (Icons.Previous, IconSize.XL), (Icons.Play, IconSize.XXL), (Icons.Next, IconSize.XL) })
        {
            var glyph = Glyph(icon, size);
            glyph.Margin = new Thickness(Space.S6, 0, Space.S6, 0);
            buttons.Children.Add(glyph);
        }
        right.Children.Add(buttons);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static UIElement TileHeader(string icon, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(icon, IconSize.S, Paint.TextSecondary));
        var text = Text(title, TypeRamp.Caption, Paint.TextSecondary, FontWeights.SemiBold);
        text.Margin = new Thickness(Space.S2, 0, 0, 0);
        row.Children.Add(text);
        return row;
    }

    private static UIElement MonitorTile()
    {
        var stack = new StackPanel { Margin = new Thickness(Space.S7) };
        stack.Children.Add(TileHeader(Icons.Activity, Loc.T("opal.demo.tile.monitor")));
        var rings = new Grid { Margin = new Thickness(0, Space.S6, 0, 0) };
        rings.ColumnDefinitions.Add(new ColumnDefinition());
        rings.ColumnDefinitions.Add(new ColumnDefinition());
        var cpu = RingWithLabel(0.34, Swatch.Green, Loc.T("opal.demo.tile.cpu"));
        var ram = RingWithLabel(0.58, AppearanceService.AccentEnd, Loc.T("opal.demo.tile.ram"));
        Grid.SetColumn(ram, 1);
        rings.Children.Add(cpu);
        rings.Children.Add(ram);
        stack.Children.Add(rings);
        var network = Text(Loc.T("opal.demo.tile.network"), TypeRamp.Caption2, Paint.TextTertiary);
        network.Margin = new Thickness(0, Space.S5, 0, 0);
        network.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(network);
        return stack;
    }

    private static UIElement RingWithLabel(double value, Color color, string label)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ring = new Grid { Width = Opal.Ring, Height = Opal.Ring };
        double r = (Opal.Ring - Opal.RingStroke) / 2, c = Opal.Ring / 2;
        ring.Children.Add(new Ellipse { Stroke = Paint.Track, StrokeThickness = Opal.RingStroke, Margin = new Thickness(0) });
        double angle = value * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise, true));
        ring.Children.Add(new Path
        {
            Data = new PathGeometry([figure]), Stroke = Paint.Gradient(color, OpalPalette.Mix(color, Swatch.White, 0.35), 90),
            StrokeThickness = Opal.RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        var percent = Text(Loc.F("opal.demo.percent", (int)Math.Round(value * 100)), TypeRamp.Callout, weight: FontWeights.SemiBold, family: FontStack.Display);
        percent.HorizontalAlignment = HorizontalAlignment.Center;
        percent.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(percent);
        stack.Children.Add(ring);
        var caption = Text(label, TypeRamp.Caption2, Paint.TextSecondary);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, Space.S2, 0, 0);
        stack.Children.Add(caption);
        return stack;
    }

    private static UIElement MusicTile()
    {
        var grid = new Grid { Margin = new Thickness(Space.S6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new SquircleBorder
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(Opal.InnerRadius - 2), Background = AccentGradient,
            Child = new TextBlock { Text = Icons.Music, FontFamily = FontStack.Icons, FontSize = IconSize.XL, Foreground = Paint.Frozen(Swatch.WhiteA(0xD0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(cover);
        var texts = new StackPanel { Margin = new Thickness(Space.S5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(Loc.T("opal.demo.sample.title"), TypeRamp.Callout, weight: FontWeights.SemiBold));
        texts.Children.Add(Text(Loc.T("opal.demo.sample.artist"), TypeRamp.Caption, Paint.TextSecondary));
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S3, 0, 0), Height = 12 };
        foreach (double h in new[] { 5.0, 10, 7, 12, 6, 9, 4 })
            bars.Children.Add(new Rectangle { Width = 2.5, Height = h, RadiusX = 1.2, RadiusY = 1.2, Fill = AccentGradient, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom });
        texts.Children.Add(bars);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }

    private static UIElement WeatherTile()
    {
        var stack = new StackPanel { Margin = new Thickness(Space.S6), VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(Icons.WeatherPartly, IconSize.XL, Paint.Frozen(Swatch.Teal)));
        var degrees = Text("18°", TypeRamp.Title1, weight: FontWeights.Light, family: FontStack.Display);
        degrees.Margin = new Thickness(Space.S2, 0, 0, 0);
        row.Children.Add(degrees);
        stack.Children.Add(row);
        stack.Children.Add(Text(Loc.T("opal.demo.tile.weather"), TypeRamp.Caption2, Paint.TextSecondary));
        return stack;
    }

    private static UIElement ClockTile()
    {
        var stack = new StackPanel { Margin = new Thickness(Space.S6), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Glyph(Icons.Clock, IconSize.L, Paint.TextSecondary));
        stack.Children.Add(Text("14:32", TypeRamp.Title2, weight: FontWeights.SemiBold, family: FontStack.Display));
        stack.Children.Add(Text(Loc.T("opal.demo.tile.day"), TypeRamp.Caption2, Paint.TextSecondary));
        return stack;
    }

    private static UIElement DockContent()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var apps = new (string Icon, Color Color, bool Open)[]
        {
            (Icons.Folder, Swatch.Blue, true), (Icons.Globe, Swatch.Teal, true), (Icons.Music, Swatch.Pink, false),
            (Icons.Message, Swatch.Green, false), (Icons.Calendar, Swatch.Orange, false), (Icons.Settings, Swatch.Purple, true),
        };
        foreach (var (icon, color, open) in apps)
        {
            var cell = new StackPanel { Margin = new Thickness(Space.S3, Space.S2, Space.S3, 0) };
            cell.Children.Add(new SquircleBorder
            {
                Width = Opal.DockIcon, Height = Opal.DockIcon, CornerRadius = new CornerRadius(Opal.InnerRadius - 1),
                Background = Paint.Gradient(OpalPalette.Mix(color, Swatch.White, 0.18), OpalPalette.Mix(color, Swatch.Black, 0.25), 90),
                Child = new TextBlock { Text = icon, FontFamily = FontStack.Icons, FontSize = IconSize.XL, Foreground = Paint.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            cell.Children.Add(new Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 3, 0, 0), Fill = open ? Paint.Indicator : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center });
            row.Children.Add(cell);
        }
        return row;
    }

    private static UIElement OpalSwitch(OpalPaint paint)
    {
        var knobMove = new TranslateTransform(Opal.Switch.Width - Opal.SwitchKnob - 6, 0);
        var track = new SquircleBorder
        {
            Width = Opal.Switch.Width, Height = Opal.Switch.Height, CornerRadius = Themes.Corners.Pill, Background = AccentGradient,
            BorderBrush = paint.Rim, BorderThickness = new Thickness(Stroke.Hairline), Cursor = Cursors.Hand,
            Child = new Ellipse
            {
                Width = Opal.SwitchKnob, Height = Opal.SwitchKnob, Fill = Paint.White, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransform = knobMove,
                Effect = Shadows.Menu.Create(),
            },
            ClipContent = false,
        };
        bool on = true;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            track.Background = on ? AccentGradient : Paint.ToggleOff;
            knobMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? Opal.Switch.Width - Opal.SwitchKnob - 6 : 0, Motion.Medium)
            {
                EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut },
            });
        };
        return track;
    }

    private UIElement FooterContent(Column column)
    {
        var grid = new Grid { Margin = new Thickness(Space.S10, 0, Space.S8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (name, description) = column.Style switch
        {
            OpalStyle.Discreet => (Loc.T("opal.style.discreet"), Loc.T("opal.style.discreet.description")),
            OpalStyle.Intense => (Loc.T("opal.style.intense"), Loc.T("opal.style.intense.description")),
            _ => (Loc.T("opal.style.balanced"), Loc.T("opal.style.balanced.description")),
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(name, TypeRamp.Title3, weight: FontWeights.SemiBold, family: FontStack.Display));
        var desc = Text(description, TypeRamp.Caption, Paint.TextSecondary);
        desc.TextTrimming = TextTrimming.None;
        desc.TextWrapping = TextWrapping.Wrap;
        texts.Children.Add(desc);
        grid.Children.Add(texts);

        var choose = new SquircleBorder
        {
            CornerRadius = new CornerRadius(Opal.ButtonRadius), Background = AccentGradient, BorderBrush = column.Paint.Rim,
            BorderThickness = new Thickness(Stroke.Hairline), Padding = new Thickness(Space.S7, Space.S4, Space.S7, Space.S4),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S5, 0, 0, 0),
            Child = new TextBlock { Text = Loc.T("opal.demo.choose"), FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, Foreground = Paint.White },
        };
        choose.MouseEnter += (_, _) => choose.Opacity = 0.88;
        choose.MouseLeave += (_, _) => choose.Opacity = 1;
        choose.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Choose(column.Style);
        };
        Grid.SetColumn(choose, 1);
        grid.Children.Add(choose);
        return grid;
    }

    private void Choose(OpalStyle style)
    {
        if (_snapshot) return;
        SettingsStore.Update(s => s.Appearance.OpalStyle = style);
        Log.Info($"Verre opalin : style « {style} » choisi dans la démo");
        Close();
    }

    // ------------------------------------------------------------------ décor + flou simulé

    private async Task ShowDecorAsync(Decor decor)
    {
        int request = ++_decorRequest;
        _current = decor;
        foreach (var (key, chip) in _decorChips) chip.Background = key == decor ? Paint.SelectedStrong : Paint.Control;
        var looks = _columns.Select(c => c.Paint.Look).ToArray();
        string? path = decor == Decor.Current && !_snapshot ? WallpaperService.CurrentPath() : null;
        (BitmapSource Sharp, BitmapSource[] Blurred)? images = null;
        try
        {
            images = await Task.Run(() =>
            {
                var sharp = LoadDecor(decor, path);
                return (sharp, looks.Select(l => OpalPalette.Blurred(sharp, l.Blur, l.Vibrancy)).ToArray());
            });
        }
        catch (Exception ex)
        {
            Log.Error("Démo du verre opalin : décor", ex);
        }
        if (images is not { } done || request != _decorRequest) return;
        _decor.Source = done.Sharp;
        for (int i = 0; i < _columns.Count; i++) _columns[i].BlurLayer.Source = done.Blurred[i];
        ScheduleClips();
    }

    /// <summary>Image du décor (hors du thread de l'interface) : fond d'écran actuel, sinon un fond du pack.</summary>
    private static BitmapSource LoadDecor(Decor decor, string? path)
    {
        if (decor == Decor.Current && path != null)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(System.IO.Path.GetFullPath(path));
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = 1920;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                Log.Info($"Démo du verre opalin : fond d'écran illisible ({ex.Message}), fond du pack à la place");
            }
        }
        return decor switch
        {
            Decor.Light => WallpaperArt.Render(WallpaperDesign.Vagues, Opal.LightMood, 1600, 900),
            Decor.Dark => WallpaperArt.Render(WallpaperDesign.Nebuleuse, Wallpapers.Night, 1600, 900),
            Decor.Colorful => WallpaperArt.Render(WallpaperDesign.Prisme, Wallpapers.Sunset, 1600, 900),
            _ => WallpaperArt.Render(WallpaperDesign.Aurore, Wallpapers.Night, 1600, 900),
        };
    }

    private void ScheduleClips() => Dispatcher.BeginInvoke(UpdateClips, DispatcherPriority.Loaded);

    /// <summary>La couche floutée de chaque intensité n'est visible que sous ses surfaces (même forme, même place).</summary>
    private void UpdateClips()
    {
        foreach (var column in _columns)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var surface in column.Surfaces)
            {
                if (surface.ActualWidth <= 0 || !surface.IsDescendantOf(_root)) continue;
                // La forme est gelée : on la place dans un groupe qui, lui, porte la position.
                var shape = new GeometryGroup { Children = { SquircleGeometry.Create(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), surface.Corners, Radius.Smoothing) } };
                var place = surface.Body.TransformToVisual(_root);
                if (place is Transform transform) shape.Transform = transform;
                else
                {
                    var bounds = place.TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
                    shape.Transform = new MatrixTransform(bounds.Width / surface.ActualWidth, 0, 0, bounds.Height / surface.ActualHeight, bounds.X, bounds.Y);
                }
                group.Children.Add(shape);
            }
            group.Freeze();
            column.BlurLayer.Clip = group;
        }
    }

    // ------------------------------------------------------------------ reflets qui suivent la souris, balayage

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_root.ActualWidth <= 0) return;
        var p = e.GetPosition(_root);
        _target = new Point(Math.Clamp(p.X / _root.ActualWidth, 0, 1), Math.Clamp(p.Y / _root.ActualHeight, 0, 1));
        if (!_follow.IsEnabled) _follow.Start();
    }

    private void FollowStep()
    {
        _light = new Point(_light.X + (_target.X - _light.X) * Opal.Follow, _light.Y + (_target.Y - _light.Y) * Opal.Follow);
        bool arrived = Math.Abs(_target.X - _light.X) < Opal.FollowDone && Math.Abs(_target.Y - _light.Y) < Opal.FollowDone;
        if (arrived) _light = _target;
        foreach (var column in _columns) column.Paint.SetLight(_light.X, _light.Y);
        if (arrived) _follow.Stop(); // la souris ne bouge plus : plus rien ne tourne
    }

    private void ReplaySweeps()
    {
        foreach (var column in _columns)
        {
            column.Island?.PlaySweep();
            foreach (var tile in column.Tiles) tile.PlaySweep();
        }
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu de la démo sur des fonds du pack (jamais le vrai fond de l'utilisateur) : 16-demo-opaline*.png.</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var window = new OpalDemoWindow(snapshot: true);
        window.Show();
        foreach (var (decor, name) in new[] { (Decor.Colorful, "16-demo-opaline.png"), (Decor.Light, "16b-demo-opaline-clair.png") })
        {
            await window.ShowDecorAsync(decor);
            foreach (var column in window._columns)
            {
                column.Paint.SetLight(0.72, 0.3);
                column.Tiles[1].ShowGlow(); // la tuile Musique « survolée »
                column.Island?.ShowSweepAt(0.42);
            }
            window.UpdateLayout();
            window.UpdateClips();
            await Task.Delay(200);
            await SnapshotRenderer.SaveAsync(window, System.IO.Path.Combine(folder, name));
        }
        window.Close();
    }
}
