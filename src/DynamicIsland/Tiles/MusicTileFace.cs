using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuile Musique vivante (O6) : la pochette floutée en fond (calculée une fois par pochette, <see cref="CoverBlur"/>), un voile,
/// titre et artiste en blanc par-dessus, un mini visualiseur (barres pilotées par le vumètre de l'île, comme son égaliseur) et,
/// en W et L, précédent / lecture-pause / suivant. Un clic ailleurs ouvre l'outil Musique. Sans musique : un état vide calme.
/// <para>Le visualiseur ne tourne (≤ 30 i/s) que si un morceau joue, tuile à l'écran, animations permises ; sinon barres figées.</para>
/// </summary>
public sealed class MusicTileFace : LiveTileFace
{
    private readonly TileContext _context;
    private readonly Image _backdrop = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly Rectangle _scrim = new() { IsHitTestVisible = false };
    private readonly Grid _playing = new();
    private readonly Grid _empty = new();
    private readonly SquircleBorder _cover = new();
    private readonly IconChip _placeholder;
    private readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyHint;
    private readonly Button? _previous;
    private readonly Button? _play;
    private readonly Button? _next;
    private readonly double[] _levels = new double[LiveTiles.Bars];
    private BitmapSource? _shownCover;
    private BitmapSource? _blurFor;
    private bool? _onArt;
    private double _lastTick = double.NaN;
    private bool _subscribed;

    /// <summary>--snapshot : niveau du son imposé (barres d'exemple) ; null = vumètre réel.</summary>
    internal double? SampleLevel { get; set; }

    /// <summary>--snapshot / --self-test : état « rien en lecture » imposé.</summary>
    internal bool SampleEmpty { get; set; }

    public MusicTileFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size)
    {
        _context = context;
        bool large = size == TileSize.L;
        bool small = size == TileSize.S;
        var root = new Grid();
        root.Children.Add(_backdrop);
        _scrim.Fill = Scrim();
        root.Children.Add(_scrim);

        // Pochette nette (ou icône quand le lecteur n'en donne pas).
        double coverSize = large ? LiveTiles.CoverL : small ? LiveTiles.CoverS : LiveTiles.CoverW;
        _cover.Width = _cover.Height = coverSize;
        _cover.CornerRadius = small ? Corners.XS : large ? Corners.L : Corners.M;
        _cover.BorderBrush = LiveTiles.CoverRim; // liseré clair : la pochette se détache de son propre flou
        _cover.BorderThickness = Borders.Hairline;
        _cover.HorizontalAlignment = HorizontalAlignment.Left;
        _cover.VerticalAlignment = VerticalAlignment.Top;
        RenderOptions.SetBitmapScalingMode(_cover, BitmapScalingMode.HighQuality);
        _placeholder = new IconChip
        {
            Glyph = Icons.Music, GlyphSize = small ? IconSize.L : IconSize.XXL, Width = coverSize, Height = coverSize,
            CornerRadius = _cover.CornerRadius, IsActive = true, Tint = Interaction.ActiveTintLight,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };

        // Visualiseur : barres ancrées en bas, hauteur réglée par une échelle (rien n'est remesuré).
        double barHeight = small ? LiveTiles.BarHeightSmall : LiveTiles.BarHeight;
        _bars.Height = barHeight;
        for (int i = 0; i < LiveTiles.Bars; i++)
        {
            var bar = new Rectangle
            {
                Width = LiveTiles.BarWidth, Height = barHeight, RadiusX = LiveTiles.BarWidth / 2, RadiusY = LiveTiles.BarWidth / 2,
                Margin = new Thickness(i == 0 ? 0 : LiveTiles.BarGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, LiveTiles.BarRest),
            };
            _bars.Children.Add(bar);
            _levels[i] = LiveTiles.BarRest;
        }

        _title = Text(large ? TypeRamp.Body : TypeRamp.Caption, FontWeights.SemiBold);
        _artist = Text(large ? TypeRamp.Caption : TypeRamp.Caption2, FontWeights.Normal);
        _artist.Margin = new Thickness(0, Space.Px, 0, 0);
        if (!small)
        {
            double button = large ? LiveTiles.ControlButton : LiveTiles.ControlButtonSmall;
            double play = large ? LiveTiles.PlayButton : LiveTiles.PlayButtonSmall;
            _previous = ControlButton(Icons.Previous, button, "tiles.music.previous", () => _context.Media.Previous());
            _play = ControlButton(Icons.Play, play, "tiles.music.play_pause", () => _context.Media.TogglePlayPause());
            _next = ControlButton(Icons.Next, button, "tiles.music.next", () => _context.Media.Next());
            if (TryFindResource("PlayButton") is Style style) _play.Style = style;
            _play.Width = _play.Height = play;
            _play.FontSize = large ? IconSize.L : IconSize.M;
        }
        BuildPlaying(size);
        root.Children.Add(_playing);

        // État vide : une pastille d'accent et une phrase, rien d'autre.
        _emptyTitle = Text(large ? TypeRamp.Body : TypeRamp.Caption, FontWeights.SemiBold, lines: 2);
        _emptyTitle.Text = Loc.T("tiles.music.idle");
        _emptyHint = Text(TypeRamp.Caption2, FontWeights.Normal, lines: 2);
        _emptyHint.Text = Loc.T("tiles.music.empty_hint");
        _emptyHint.Margin = new Thickness(0, Space.Px, 0, 0);
        Ink(_emptyTitle, false, InkRole.TextPrimary);
        Ink(_emptyHint, false, InkRole.TextSecondary);
        BuildEmpty(size);
        root.Children.Add(_empty);

        SetContent(root, new Thickness(0));
    }

    private Button ControlButton(string glyph, double size, string nameKey, Action action)
    {
        var button = new Button { Content = glyph, Width = size, Height = size, FontSize = IconSize.M, Focusable = false };
        if (TryFindResource("IconButton") is Style style) button.Style = style;
        button.Width = button.Height = size;
        button.FontSize = IconSize.M;
        System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(nameKey));
        button.ToolTip = Loc.T(nameKey);
        button.Click += (_, e) =>
        {
            e.Handled = true; // le clic sur un bouton n'ouvre pas l'outil
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Tuile Musique : commande du lecteur", ex);
            }
        };
        return button;
    }

    private void BuildPlaying(TileSize size)
    {
        var content = _playing;
        switch (size)
        {
            case TileSize.S:
                // Pochette en haut à gauche, visualiseur en haut à droite, titre et artiste en bas.
                content.Margin = LiveTiles.PaddingSmall;
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.Children.Add(_cover);
                content.Children.Add(_placeholder);
                _bars.HorizontalAlignment = HorizontalAlignment.Right;
                content.Children.Add(_bars);
                Grid.SetRow(_title, 1);
                Grid.SetRow(_artist, 2);
                content.Children.Add(_title);
                content.Children.Add(_artist);
                break;
            case TileSize.W:
                // Pochette à gauche ; à droite le titre, l'artiste, puis les boutons et le visualiseur.
                content.Margin = LiveTiles.Padding;
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.Children.Add(_cover);
                content.Children.Add(_placeholder);
                var column = new Grid { Margin = new Thickness(Space.S5, 0, 0, 0) };
                Grid.SetColumn(column, 1);
                column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                column.Children.Add(_title);
                Grid.SetRow(_artist, 1);
                column.Children.Add(_artist);
                var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
                Grid.SetRow(row, 2);
                row.Children.Add(Controls(Orientation.Horizontal, HorizontalAlignment.Left));
                _bars.HorizontalAlignment = HorizontalAlignment.Right;
                _bars.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(_bars);
                column.Children.Add(row);
                content.Children.Add(column);
                break;
            default:
                // Pochette en haut à gauche et visualiseur en haut à droite ; titre, artiste, puis les boutons centrés.
                content.Margin = LiveTiles.PaddingLarge;
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.Children.Add(_cover);
                content.Children.Add(_placeholder);
                _bars.HorizontalAlignment = HorizontalAlignment.Right;
                content.Children.Add(_bars);
                Grid.SetRow(_title, 2);
                Grid.SetRow(_artist, 3);
                content.Children.Add(_title);
                content.Children.Add(_artist);
                var controls = Controls(Orientation.Horizontal, HorizontalAlignment.Center);
                controls.Margin = new Thickness(0, Space.S4, 0, 0);
                Grid.SetRow(controls, 4);
                content.Children.Add(controls);
                break;
        }
    }

    private StackPanel Controls(Orientation orientation, HorizontalAlignment alignment)
    {
        var panel = new StackPanel { Orientation = orientation, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center };
        double gap = TileSize == TileSize.L ? Space.S4 : Space.S1;
        _play!.Margin = new Thickness(gap, 0, gap, 0);
        panel.Children.Add(_previous!);
        panel.Children.Add(_play);
        panel.Children.Add(_next!);
        return panel;
    }

    private void BuildEmpty(TileSize size)
    {
        var chip = new IconChip
        {
            Glyph = Icons.Music, IsActive = true, Tint = Interaction.ActiveTintLight,
            GlyphSize = size == TileSize.L ? IconSize.XXL : IconSize.L,
            Width = size == TileSize.L ? LiveTiles.EmptyChip : Sizes.IconChipLarge,
            Height = size == TileSize.L ? LiveTiles.EmptyChip : Sizes.IconChipLarge,
            CornerRadius = size == TileSize.L ? Corners.L : Corners.IconChip,
            HorizontalAlignment = size == TileSize.L ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var texts = new StackPanel();
        texts.Children.Add(_emptyTitle);
        texts.Children.Add(_emptyHint);
        switch (size)
        {
            case TileSize.S:
                _empty.Margin = LiveTiles.PaddingSmall;
                _empty.Children.Add(chip);
                _emptyHint.Visibility = Visibility.Collapsed; // case étroite : le titre suffit
                texts.VerticalAlignment = VerticalAlignment.Bottom;
                _empty.Children.Add(texts);
                break;
            case TileSize.W:
                _empty.Margin = LiveTiles.Padding;
                _empty.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _empty.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                chip.VerticalAlignment = VerticalAlignment.Center;
                _empty.Children.Add(chip);
                texts.Margin = new Thickness(Space.S5, 0, 0, 0);
                texts.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(texts, 1);
                _empty.Children.Add(texts);
                break;
            default:
                _empty.Margin = LiveTiles.PaddingLarge;
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(chip);
                texts.Margin = new Thickness(0, Space.S6, 0, 0);
                _emptyTitle.TextAlignment = _emptyHint.TextAlignment = TextAlignment.Center;
                stack.Children.Add(texts);
                _empty.Children.Add(stack);
                break;
        }
    }

    // ------------------------------------------------------------------ données

    private bool HasMusic => !SampleEmpty && _context.Media.HasSession && !string.IsNullOrWhiteSpace(_context.Media.Title);

    protected override bool WantsAnimation => HasMusic && _context.Media.IsPlaying && (_context.MeterLevel != null || SampleLevel != null);

    protected override void OnShown(bool shown)
    {
        // Lecture / pause changée ailleurs (clavier, appli) : la tuile suit tout de suite tant qu'elle est à l'écran.
        if (shown == _subscribed) return;
        _subscribed = shown;
        if (shown) _context.Media.Changed += OnMediaChanged;
        else _context.Media.Changed -= OnMediaChanged;
        if (!shown) _lastTick = double.NaN;
    }

    private void OnMediaChanged()
    {
        if (!Dispatcher.CheckAccess() || !IsLive) return;
        Refresh();
    }

    protected override void Update()
    {
        var media = _context.Media;
        bool music = HasMusic;
        _playing.Visibility = music ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = music ? Visibility.Collapsed : Visibility.Visible;
        if (!music)
        {
            ShowArt(null);
            Design.OpalTile.SetGlowColor(this, null);
            return;
        }
        _title.Text = media.Title;
        _artist.Text = string.IsNullOrWhiteSpace(media.Artist) ? media.AppName : media.Artist;
        _artist.Visibility = string.IsNullOrWhiteSpace(_artist.Text) ? Visibility.Collapsed : Visibility.Visible;
        var cover = media.Cover;
        if (!ReferenceEquals(cover, _shownCover))
        {
            _shownCover = cover;
            _cover.Background = cover == null ? null : new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
        }
        _cover.Visibility = cover != null ? Visibility.Visible : Visibility.Collapsed;
        _placeholder.Visibility = cover == null ? Visibility.Visible : Visibility.Collapsed;
        ShowArt(cover);
        Design.OpalTile.SetGlowColor(this, cover != null ? media.Accent : null); // la lueur au survol prend la couleur de la pochette
        if (_play != null) _play.Content = media.IsPlaying ? Icons.Pause : Icons.Play;
        if (!LiveTicker.Allowed || !media.IsPlaying || !IsLive) RestBars(media.IsPlaying);
    }

    /// <summary>Fond : la pochette floutée (dès qu'elle est prête) avec le voile, sinon le verre de la tuile.</summary>
    private void ShowArt(BitmapSource? cover)
    {
        BitmapSource? blurred = null;
        if (cover != null)
        {
            blurred = CoverBlur.Get(cover, () =>
            {
                if (ReferenceEquals(_blurFor, cover)) Refresh(); // protégé : une erreur n'arrête que cette tuile
            });
            _blurFor = cover;
        }
        bool onArt = blurred != null;
        if (!ReferenceEquals(_backdrop.Source, blurred)) _backdrop.Source = blurred;
        _backdrop.Visibility = _scrim.Visibility = onArt ? Visibility.Visible : Visibility.Collapsed;
        if (_onArt == onArt) return;
        _onArt = onArt;
        Ink(_title, onArt, InkRole.TextPrimary);
        Ink(_artist, onArt, InkRole.TextSecondary);
        foreach (Rectangle bar in _bars.Children)
        {
            if (onArt) bar.Fill = LiveTiles.OnArt;
            else bar.SetResourceReference(Shape.FillProperty, Services.AppearanceService.AccentBrushKey);
        }
        foreach (var button in new[] { _previous, _next })
        {
            if (button == null) continue;
            if (onArt) button.Foreground = LiveTiles.OnArt;
            else button.SetResourceReference(Control.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
        }
    }

    /// <summary>Barres figées : petite rangée calme si ça joue (animations coupées), ligne basse en pause.</summary>
    private void RestBars(bool playing)
    {
        int i = 0;
        foreach (Rectangle bar in _bars.Children)
        {
            _levels[i] = playing ? LiveTiles.BarPause[i % LiveTiles.BarPause.Length] : LiveTiles.BarRest;
            ((ScaleTransform)bar.RenderTransform).ScaleY = _levels[i];
            i++;
        }
    }

    /// <summary>Visualiseur : niveau du vumètre + léger décalage par barre (même mouvement que l'égaliseur de l'île).</summary>
    protected override void Animate(double seconds)
    {
        if (!HasMusic) return;
        double dt = double.IsNaN(_lastTick) ? LiveTiles.Frame.TotalSeconds : Math.Clamp(seconds - _lastTick, 0, 0.1);
        _lastTick = seconds;
        double raw = SampleLevel ?? _context.MeterLevel?.Invoke() ?? 0;
        double level = Math.Pow(Math.Clamp(raw, 0, 1), 0.6);
        // Lissage indépendant de la cadence (monte vite, redescend doucement) ; un rendu figé prend la cible tout de suite.
        double up = SampleLevel != null ? 1 : 1 - Math.Pow(0.4, dt * Motion.DecorativeFrameRate);
        double down = SampleLevel != null ? 1 : 1 - Math.Pow(0.75, dt * Motion.DecorativeFrameRate);
        int i = 0;
        foreach (Rectangle bar in _bars.Children)
        {
            double wobble = 0.6 + 0.4 * Math.Sin(seconds * (7 + i * 2.3) + i * 1.7);
            double target = Math.Clamp(LiveTiles.BarRest + (1 - LiveTiles.BarRest) * level * wobble, LiveTiles.BarRest, 1);
            _levels[i] += (target - _levels[i]) * (target > _levels[i] ? up : down);
            ((ScaleTransform)bar.RenderTransform).ScaleY = _levels[i];
            i++;
        }
    }

    /// <summary>--self-test : hauteurs des barres (0–1).</summary>
    internal IReadOnlyList<double> BarLevels => _levels;

    /// <summary>--self-test : la tuile montre la pochette floutée (texte blanc) ou l'état vide.</summary>
    internal bool ShowsArt => _backdrop.Visibility == Visibility.Visible;
    internal bool ShowsEmpty => _empty.Visibility == Visibility.Visible;
}
