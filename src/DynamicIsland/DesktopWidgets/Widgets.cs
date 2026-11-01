using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.DesktopWidgets;

/// <summary>
/// Contenu d'un widget du bureau, dessiné à sa taille de base (<see cref="BaseSize"/>, échelle 1) : la fenêtre le
/// met à l'échelle. Les cartes ont le fond de l'île (plus transparent) et le verre liquide.
/// </summary>
public abstract class DesktopWidget : Grid
{
    private readonly SquircleBorder? _card;
    private readonly LiquidGlass? _glass;

    protected DesktopWidget(Size baseSize, bool card)
    {
        BaseSize = baseSize;
        Width = baseSize.Width;
        Height = baseSize.Height;
        if (!card) return;
        _card = new SquircleBorder { CornerRadius = Corners.Tile, BorderThickness = Borders.Hairline, BorderBrush = Paint.PopupBorder };
        Children.Add(_card);
        _glass = new LiquidGlass(_card, () => _card.CornerRadius.TopLeft);
        ApplyLook();
    }

    public abstract DesktopWidgetKind Kind { get; }
    public Size BaseSize { get; }

    /// <summary>Contenu posé dans la carte, avec la marge intérieure.</summary>
    protected T Inside<T>(T content) where T : FrameworkElement
    {
        content.Margin = new Thickness(Desk.CardPadding);
        Children.Add(content);
        return content;
    }

    /// <summary>Fond de la carte : teinte de l'île (fond d'écran compris si l'option est cochée), plus transparente.</summary>
    public void ApplyLook()
    {
        if (_card != null) _card.Background = Paint.Frozen(Swatch.WithAlpha(AppearanceService.SurfaceBase, Desk.CardAlpha));
    }

    /// <summary>Données à jour (appelé par le service quand elles changent).</summary>
    public virtual void Refresh()
    {
    }

    public void Detach() => _glass?.Detach();

    protected static TextBlock Text(string text, double size, Brush brush, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    protected static TextBlock Glyph(string glyph, double size, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = FontStack.Icons,
        FontSize = size,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>Horloge géante façon « MONDAY » : le jour en capitales espacées, la date et l'heure dessous.</summary>
public sealed class ClockWidget : DesktopWidget
{
    private readonly StackPanel _day = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _line = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(Space.Px * 4, Space.S2, 0, 0) };
    private string _shown = "";

    public ClockWidget() : base(Desk.Clock, card: false)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new Viewbox { Child = _day, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(_line);
        // Ombre douce : lisible sur un fond d'écran clair comme sur un sombre.
        stack.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45, Direction = Shadows.Direction, RenderingBias = RenderingBias.Performance };
        Children.Add(stack);
        Refresh();
    }

    public override DesktopWidgetKind Kind => DesktopWidgetKind.Clock;

    public override void Refresh()
    {
        var now = DateTime.Now;
        var day = now.ToString("dddd", Loc.Culture).ToUpper(Loc.Culture);
        var line = $"{now.ToString("d MMMM", Loc.Culture).ToUpper(Loc.Culture)} · {now:HH:mm}";
        if (day + line == _shown) return;
        _shown = day + line;
        Fill(_day, day, Desk.ClockDay, FontWeights.Bold, Desk.ClockDayTracking, Paint.White, FontStack.Display);
        // Date + heure en police des chiffres (O4) : une lettre par TextBlock → les chiffres DOIVENT être à largeur fixe
        Fill(_line, line, Desk.ClockLine, NumberWeight.Text, Desk.ClockLineTracking, ClockLineBrush, FontStack.Numbers); // posé sur le fond d'écran : blanc, même en opale blanche
    }

    private static readonly Brush ClockLineBrush = Paint.Frozen(InkPalette.Dark[(int)InkRole.TextMuted]);

    /// <summary>WPF n'a pas d'espacement des lettres : une lettre par TextBlock.</summary>
    private static void Fill(Panel panel, string text, double size, FontWeight weight, double tracking, Brush brush, FontFamily family)
    {
        panel.Children.Clear();
        foreach (char c in text)
        {
            var letter = new TextBlock
            {
                Text = c.ToString(),
                FontFamily = family,
                FontSize = size,
                FontWeight = weight,
                Foreground = brush,
                Margin = new Thickness(0, 0, tracking, 0),
            };
            System.Windows.Documents.Typography.SetNumeralAlignment(letter, FontNumeralAlignment.Tabular);
            panel.Children.Add(letter);
        }
    }
}

/// <summary>Météo de la ville choisie (même service que l'onglet Widgets de l'île).</summary>
public sealed class WeatherWidget : DesktopWidget
{
    private readonly WeatherService _weather;
    private readonly TextBlock _city = Text("", TypeRamp.Footnote, Paint.TextSecondary, FontWeights.SemiBold);
    private readonly TextBlock _symbol = Glyph(Icons.WeatherCloud, IconSize.Huge, Paint.TextPrimary);
    private readonly TextBlock _temperature = Text("—", Desk.WeatherTemperature, Paint.TextPrimary, FontWeights.Light);
    private readonly TextBlock _description = Text("", TypeRamp.Body, Paint.TextPrimary, FontWeights.SemiBold);
    private readonly TextBlock _range = Text("", TypeRamp.Footnote, Paint.TextSecondary);

    public WeatherWidget(WeatherService weather) : base(Desk.Weather, card: true)
    {
        _weather = weather;
        var grid = Inside(new Grid());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var place = new StackPanel { Orientation = Orientation.Horizontal };
        place.Children.Add(Glyph(Icons.MapPin, IconSize.S, Paint.TextSecondary));
        _city.Margin = new Thickness(Space.S2, 0, 0, 0);
        place.Children.Add(_city);
        grid.Children.Add(place);

        var main = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        main.Children.Add(_symbol);
        Numerals.Apply(_temperature, Desk.WeatherTemperature);
        _temperature.Margin = new Thickness(Space.S5, 0, 0, 0);
        main.Children.Add(_temperature);
        Grid.SetRow(main, 1);
        grid.Children.Add(main);

        Grid.SetRow(_description, 2);
        grid.Children.Add(_description);
        _range.Margin = new Thickness(0, Space.S1, 0, 0);
        Grid.SetRow(_range, 3);
        grid.Children.Add(_range);
        Refresh();
    }

    public override DesktopWidgetKind Kind => DesktopWidgetKind.Weather;

    /// <summary>« 4° » (jamais « -0° »).</summary>
    private static string Degrees(double value) => $"{(int)Math.Round(value)}°";

    public override void Refresh()
    {
        if (_weather.Current is { } report)
        {
            _city.Text = report.City;
            _symbol.Text = report.Symbol;
            _temperature.Text = Degrees(report.Temperature);
            _description.Text = report.Description;
            _range.Text = $"↓ {Degrees(report.Min)}   ↑ {Degrees(report.Max)}";
        }
        else
        {
            _city.Text = SettingsStore.Current.Widgets.City;
            _description.Text = _weather.Error ?? Loc.T("text.loading");
        }
    }
}

/// <summary>Musique en cours (pochette, titre, artiste) + précédent / lecture-pause / suivant.</summary>
public sealed class MusicWidget : DesktopWidget
{
    private readonly MediaService _media;
    private readonly SquircleBorder _cover = new() { Width = Desk.MusicCover, Height = Desk.MusicCover, CornerRadius = Corners.XL, Background = Paint.Surface };
    private readonly TextBlock _placeholder = Glyph(Icons.Music, IconSize.XXL, Paint.TextTertiary);
    private readonly TextBlock _title = Text("", TypeRamp.Title3, Paint.TextPrimary, FontWeights.SemiBold);
    private readonly TextBlock _artist = Text("", TypeRamp.Footnote, Paint.TextSecondary);
    private readonly Button _play;
    private readonly Button _mute;
    private readonly LineSlider _volume = new() { Width = Desk.MusicVolume, WheelStep = 0.02, VerticalAlignment = VerticalAlignment.Center };
    private readonly AppVolumeService _appVolume = new();
    private bool _muted;

    public MusicWidget(MediaService media) : base(Desk.Music, card: true)
    {
        _media = media;
        var grid = Inside(new Grid());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.Child = _placeholder;
        RenderOptions.SetBitmapScalingMode(_cover, BitmapScalingMode.HighQuality);
        grid.Children.Add(_cover);

        var right = new StackPanel { Margin = new Thickness(Space.S8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_title);
        _artist.Margin = new Thickness(0, Space.Px, 0, 0);
        right.Children.Add(_artist);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S5, 0, 0) };
        controls.Children.Add(Control(Icons.Previous, () => _media.Previous()));
        _play = Control(Icons.Play, () => _media.TogglePlayPause());
        _play.Margin = new Thickness(Space.S4, 0, Space.S4, 0);
        controls.Children.Add(_play);
        controls.Children.Add(Control(Icons.Next, () => _media.Next()));
        // Volume de l'appli qui joue (Spotify, navigateur…), sans toucher au volume de Windows : clic sur l'icône = muet.
        _mute = new Button
        {
            Style = (Style)Application.Current.FindResource("SmallIconButton"),
            Content = Icons.VolumeHigh,
            Margin = new Thickness(Space.S4, 0, Space.S1, 0),
        };
        _mute.Click += (_, _) =>
        {
            if (App.SnapshotDemo || !_media.HasSession) return;
            if (_appVolume.SetMute(_media.SourceAppId, !_muted)) RefreshVolume();
        };
        controls.Children.Add(_mute);
        _volume.UserChanged += value =>
        {
            if (App.SnapshotDemo) return;
            _appVolume.SetLevel(_media.SourceAppId, value);
            ShowVolume(value, false);
        };
        controls.Children.Add(_volume);
        MouseEnter += (_, _) => RefreshVolume(); // suit un changement fait ailleurs (île, mélangeur de Windows)
        right.Children.Add(controls);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Refresh();
    }

    public override DesktopWidgetKind Kind => DesktopWidgetKind.Music;

    private static Button Control(string glyph, Action action)
    {
        var button = new Button { Style = (Style)Application.Current.FindResource("IconButton"), Content = glyph };
        button.Click += (_, _) => action();
        return button;
    }

    public override void Refresh()
    {
        bool playing = _media.HasSession && _media.Title.Length > 0;
        _title.Text = playing ? _media.Title : Loc.T("widget.music.none");
        _artist.Text = playing ? string.Join(" · ", new[] { _media.Artist, _media.AppName }.Where(s => !string.IsNullOrWhiteSpace(s))) : Loc.T("widget.music.hint");
        _cover.Background = _media.Cover != null ? new ImageBrush(_media.Cover) { Stretch = Stretch.UniformToFill } : Paint.Surface;
        _placeholder.Visibility = _media.Cover != null ? Visibility.Collapsed : Visibility.Visible;
        _play.Content = _media.IsPlaying ? Icons.Pause : Icons.Play;
        _play.IsEnabled = _media.HasSession;
        RefreshVolume();
    }

    private void RefreshVolume()
    {
        if (_volume.IsDragging) return;
        double level = 0;
        bool muted = false;
        bool found = _media.HasSession && (App.SnapshotDemo ? (level = 0.72) > 0 : _appVolume.TryRead(_media.SourceAppId, out level, out muted));
        _mute.IsEnabled = _volume.IsEnabled = found;
        _volume.ToolTip = found ? null : Loc.F("music.volume.missing", _media.AppName.Length > 0 ? _media.AppName : Loc.T("music.volume.this_app"));
        if (!found)
        {
            _mute.Content = Icons.VolumeMute;
            _volume.Value = 0;
            return;
        }
        _volume.Value = muted ? 0 : level;
        ShowVolume(level, muted);
    }

    private void ShowVolume(double level, bool muted)
    {
        _muted = muted;
        _mute.Content = muted || level < 0.005 ? Icons.VolumeMute : level < 0.34 ? Icons.VolumeLow : level < 0.67 ? Icons.VolumeMedium : Icons.VolumeHigh;
        _mute.ToolTip = muted ? Loc.T("volume.muted") : Loc.F("format.percent", Math.Round(level * 100));
    }
}

/// <summary>Mois en cours, aujourd'hui entouré de la couleur d'accent.</summary>
public sealed class CalendarWidget : DesktopWidget
{
    private readonly TextBlock _month = Text("", TypeRamp.Headline, Paint.TextPrimary, FontWeights.SemiBold);
    private readonly UniformGrid _days = new() { Columns = 7, Rows = 6 };
    private DateTime _shown;

    public CalendarWidget() : base(Desk.Calendar, card: true)
    {
        var stack = Inside(new StackPanel());
        stack.Children.Add(_month);
        var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, Space.S4, 0, Space.S1) };
        foreach (var name in Loc.T("calendar.weekdays").Split(','))
        {
            var label = Text(name, TypeRamp.Caption2, Paint.TextTertiary, FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            names.Children.Add(label);
        }
        stack.Children.Add(names);
        stack.Children.Add(_days);
        Refresh();
    }

    public override DesktopWidgetKind Kind => DesktopWidgetKind.Calendar;

    public override void Refresh()
    {
        var today = DateTime.Today;
        if (today == _shown) return;
        _shown = today;
        var month = today.ToString("MMMM yyyy", Loc.Culture);
        _month.Text = char.ToUpper(month[0], Loc.Culture) + month[1..];
        _days.Children.Clear();
        var first = new DateTime(today.Year, today.Month, 1);
        int offset = ((int)first.DayOfWeek + 6) % 7; // semaine commençant le lundi
        for (int i = 0; i < 42; i++)
        {
            var day = first.AddDays(i - offset);
            bool isToday = day == today, inMonth = day.Month == today.Month;
            var number = Text(day.Day.ToString(Loc.Culture), TypeRamp.Footnote,
                isToday ? Paint.White : inMonth ? Paint.TextPrimary : Paint.TextFaint, isToday ? FontWeights.Bold : FontWeights.Normal);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center;
            var cell = new SquircleBorder { Width = Desk.CalendarCell, Height = Desk.CalendarCell * 0.8, CornerRadius = Corners.Pill, Child = number };
            if (isToday) cell.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
            _days.Children.Add(cell);
        }
    }
}

/// <summary>Processeur, mémoire, carte graphique (barres à la couleur d'accent) et réseau.</summary>
public sealed class StatsWidget : DesktopWidget
{
    private readonly (TextBlock Value, Border Fill, Border Track)[] _bars = new (TextBlock, Border, Border)[3];
    private readonly TextBlock _download = Text("…", TypeRamp.Footnote, Paint.TextSecondary);
    private readonly TextBlock _upload = Text("…", TypeRamp.Footnote, Paint.TextSecondary);

    public StatsWidget() : base(Desk.Stats, card: true)
    {
        var stack = Inside(new StackPanel { VerticalAlignment = VerticalAlignment.Center });
        string[] labels = [Loc.T("widget.stats.cpu"), Loc.T("widget.stats.memory"), Loc.T("widget.stats.gpu")];
        string[] glyphs = [Icons.Cpu, Icons.Memory, Icons.Monitor];
        for (int i = 0; i < 3; i++)
        {
            var header = new Grid { Margin = new Thickness(0, i == 0 ? 0 : Space.S5, 0, Space.S2) };
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Glyph(glyphs[i], IconSize.S, Paint.TextSecondary));
            var label = Text(labels[i], TypeRamp.Footnote, Paint.TextPrimary, FontWeights.SemiBold);
            label.Margin = new Thickness(Space.S3, 0, 0, 0);
            name.Children.Add(label);
            header.Children.Add(name);
            var value = Numerals.Apply(Text("…", TypeRamp.Footnote, Paint.TextSecondary), TypeRamp.Footnote);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(value);
            stack.Children.Add(header);

            var track = new SquircleBorder { Height = Sizes.Bar, CornerRadius = Corners.Pill, Background = Paint.Track };
            var fill = new SquircleBorder { Height = Sizes.Bar, CornerRadius = Corners.Pill, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            fill.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
            var bar = new Grid();
            bar.Children.Add(track);
            bar.Children.Add(fill);
            stack.Children.Add(bar);
            _bars[i] = (value, fill, track);
        }
        // Réseau (correctif 17) : « Débit descendant » et « Débit ascendant » en toutes lettres, valeur à droite.
        stack.Children.Add(RateRow(Icons.ArrowDown, Loc.T("widgets.download"), _download, Space.S6));
        stack.Children.Add(RateRow(Icons.ArrowUp, Loc.T("widgets.upload"), _upload, Space.S2));
    }

    private static Grid RateRow(string glyph, string label, TextBlock value, double top)
    {
        var row = new Grid { Margin = new Thickness(0, top, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(Glyph(glyph, IconSize.S, Paint.TextTertiary));
        var text = Text(label, TypeRamp.Footnote, Paint.TextSecondary);
        text.Margin = new Thickness(Space.S3, 0, Space.S4, 0);
        name.Children.Add(text);
        row.Children.Add(name);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    public override DesktopWidgetKind Kind => DesktopWidgetKind.Stats;

    public void Show(SystemStats stats)
    {
        double?[] values = [stats.Cpu, stats.Ram, stats.Gpu];
        double width = BaseSize.Width - 2 * Desk.CardPadding;
        for (int i = 0; i < 3; i++)
        {
            _bars[i].Value.Text = values[i] is double v ? Loc.F("format.percent", Math.Round(v)) : "—";
            _bars[i].Fill.Width = width * Math.Clamp((values[i] ?? 0) / 100, 0, 1);
        }
        _download.Text = SystemStatsService.FormatRate(stats.Download);
        _upload.Text = SystemStatsService.FormatRate(stats.Upload);
    }
}
