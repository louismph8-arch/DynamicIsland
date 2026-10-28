using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Dock;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Welcome;

/// <summary>
/// Assistant de premier lancement (5H) : bienvenue, profil, style, fond d'écran, modules, astuces. Les choix « vivants »
/// (profil, couleur, verre) s'appliquent tout de suite pour qu'on voie l'île et le dock changer ; ceux qui touchent
/// Windows (fond d'écran, barre des tâches, démarrage) attendent « C'est parti ». « Passer » garde ce qui est déjà
/// choisi. Réouvrable depuis Paramètres → À propos.
/// </summary>
public sealed class WelcomeWindow : Window
{
    private static WelcomeWindow? _instance;
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Overshoot = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

    private readonly bool _snapshot;
    private readonly Grid _shell = new();
    private readonly ScaleTransform _shellScale = new(1, 1);
    private readonly DropShadowEffect _shadow = new() { RenderingBias = RenderingBias.Performance, Direction = Shadows.Direction };
    private readonly SquircleBorder _card = new();
    private readonly LiquidGlass _glass;
    private readonly Grid _pageHost = new() { ClipToBounds = true };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back;
    private readonly Button _next;
    private readonly TextBlock _nextText = new() { FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, Foreground = Paint.White };
    private readonly List<FrameworkElement> _pages = [];
    private int _page = -1;
    private bool _finished;
    private bool _building = true;

    // Page « Style »
    private readonly WrapPanel _swatches = new();
    private readonly CheckBox _wallpaperColorsToggle;
    private readonly CheckBox _glassToggle;
    private readonly SquircleBorder _preview = new();
    private readonly DropShadowEffect _previewGlow = new() { RenderingBias = RenderingBias.Performance, Direction = Shadows.Direction };

    // Page « Fond d'écran » (appliqué à la fin) : null = garder le fond actuel
    private WallpaperDesign? _wallpaper;
    private readonly WrapPanel _gallery = new();
    private readonly CheckBox _dynamicToggle;
    private static readonly Dictionary<WallpaperDesign, BitmapSource> Thumbnails = [];
    private Task? _thumbnails;

    // Page « Modules » (appliqué à la fin)
    private readonly CheckBox _islandToggle, _dockToggle, _taskbarToggle, _menuBarToggle, _widgetsToggle, _startupToggle;
    private readonly RadioButton _barLayout, _floatingLayout;

    /// <summary>Ouvre l'assistant (ou le ramène devant).</summary>
    public static void Open()
    {
        if (_instance == null)
        {
            _instance = new WelcomeWindow(snapshot: false);
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        _instance.Activate();
    }

    private WelcomeWindow(bool snapshot)
    {
        _snapshot = snapshot;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Settings/SettingsStyles.xaml") });
        Title = Loc.F("welcome.title", AppInfo.Name);
        Icon = OpalineLogo.Get(64);
        Width = WelcomeShape.Width;
        Height = WelcomeShape.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;
        UseLayoutRounding = true;

        var settings = SettingsStore.Current;
        _wallpaperColorsToggle = Switch(settings.Appearance.AccentFromWallpaper, on => SettingsStore.Update(s => s.Appearance.AccentFromWallpaper = on));
        _glassToggle = Switch(settings.Appearance.LiquidGlass && LiquidGlassEffect.IsAvailable, on => SettingsStore.Update(s => s.Appearance.LiquidGlass = on));
        _glassToggle.IsEnabled = LiquidGlassEffect.IsAvailable;
        _dynamicToggle = Switch(settings.Wallpaper.Dynamic, _ => { });
        _islandToggle = Switch(settings.Island.Enabled);
        _dockToggle = Switch(settings.Dock.Enabled);
        _taskbarToggle = Switch(settings.Dock.HideTaskbar);
        _menuBarToggle = Switch(settings.MenuBar.Enabled);
        _widgetsToggle = Switch(settings.DesktopWidgets.Enabled);
        _startupToggle = Switch(StartupService.IsEnabled);
        _barLayout = Option(Loc.T("welcome.layout.bar"), Loc.T("welcome.layout.bar.description"), "WelcomeLayout", settings.Dock.Layout == DockLayout.Bar);
        _floatingLayout = Option(Loc.T("welcome.layout.floating"), Loc.T("welcome.layout.floating.description"), "WelcomeLayout", settings.Dock.Layout == DockLayout.Floating);

        _back = new Button { Style = (Style)FindResource("SettingsButton"), Content = Loc.T("welcome.back"), Visibility = Visibility.Hidden };
        _back.Click += (_, _) => GoTo(_page - 1);
        _next = new Button
        {
            Style = (Style)FindResource("RoundButton"),
            Content = _nextText,
            Width = WelcomeShape.NextWidth,
            Height = Sizes.SoftButton,
        };
        _next.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        _next.Click += (_, _) =>
        {
            if (_page < _pages.Count - 1) GoTo(_page + 1);
            else Finish();
        };

        _pages.Add(BuildWelcome());
        _pages.Add(BuildProfiles());
        _pages.Add(BuildStyle());
        _pages.Add(BuildWallpaper());
        _pages.Add(BuildModules());
        _pages.Add(BuildDone());
        foreach (var page in _pages)
        {
            page.Visibility = Visibility.Collapsed;
            page.RenderTransform = new TranslateTransform();
            _pageHost.Children.Add(page);
        }
        for (int i = 0; i < _pages.Count; i++)
            _dots.Children.Add(new SquircleBorder { Width = WelcomeShape.Dot, Height = WelcomeShape.Dot, CornerRadius = Corners.Pill, Margin = new Thickness(0, 0, Space.S3, 0), Background = Paint.Track });

        Content = BuildFrame();
        _glass = new LiquidGlass(_card, () => _card.CornerRadius.TopLeft);
        ShowStyle();
        _building = false;

        SettingsStore.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            SettingsStore.Changed -= OnSettingsChanged;
            _glass.Detach();
            if (!_snapshot) MarkDone(); // fermé autrement (Alt+F4…) : on ne le remontre pas au prochain lancement
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Skip();
            else if (e.Key == Key.Enter && _next.IsEnabled) _next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            GoTo(0, animate: !_snapshot);
            if (_snapshot) return;
            // La carte arrive en se « déployant », comme l'île.
            _shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long) { EasingFunction = EaseOut });
            var grow = new DoubleAnimation(0.92, 1, Motion.Page) { EasingFunction = Overshoot };
            _shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
    }

    // ------------------------------------------------------------------ cadre

    private Grid BuildFrame()
    {
        _card.CornerRadius = new CornerRadius(AppearanceService.Radius(Radius.Palette));
        _card.BorderThickness = Borders.Hairline;
        _card.BorderBrush = new SolidColorBrush(AppearanceService.RimColor());
        _card.SetResourceReference(Border.BackgroundProperty, AppearanceService.IslandBrushKey);
        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && !_snapshot) DragMove(); // la carte se déplace par son fond
        };

        var skip = new Button { Style = (Style)FindResource("RoundButton"), Padding = new Thickness(Space.S6, Space.S2, Space.S6, Space.S2), Foreground = Paint.TextSecondary };
        skip.Content = new TextBlock { Text = Loc.T("welcome.skip"), FontSize = TypeRamp.Footnote };
        skip.Click += (_, _) => Skip();

        var header = new Grid { Margin = new Thickness(Space.S14, Space.S8, Space.S8, 0) };
        header.Children.Add(_dots);
        skip.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(skip);

        var footer = new Grid { Margin = new Thickness(Space.S14, Space.S6, Space.S14, Space.S10) };
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(_back);
        footer.Children.Add(_next);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_pageHost, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(header);
        layout.Children.Add(_pageHost);
        layout.Children.Add(footer);
        _card.Child = layout;

        AppearanceService.ApplyGlow(_shadow, Shadows.Window);
        _shell.Effect = _shadow;
        _shell.Margin = new Thickness(WelcomeShape.Margin);
        _shell.RenderTransformOrigin = new Point(0.5, 0.5);
        _shell.RenderTransform = _shellScale;
        _shell.Children.Add(_card);
        return new Grid { Children = { _shell } };
    }

    // ------------------------------------------------------------------ pages

    private FrameworkElement BuildWelcome()
    {
        var logo = new Image { Source = OpalineLogo.Get(WelcomeShape.Logo * 2), Width = WelcomeShape.Logo, Height = WelcomeShape.Logo };
        logo.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        var turn = new RotateTransform();
        logo.RenderTransform = new TransformGroup { Children = { scale, turn } };
        if (!_snapshot)
        {
            logo.Loaded += (_, _) =>
            {
                // Le logo s'ouvre comme une bulle d'opale : il grossit et tourne légèrement en se posant.
                var grow = new DoubleAnimation(0.55, 1, Motion.Bounce) { EasingFunction = Overshoot, BeginTime = Motion.DelayLonger };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-50, 0, Motion.Bounce) { EasingFunction = EaseOut, BeginTime = Motion.DelayLonger });
                logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long) { BeginTime = Motion.DelayLonger });
            };
        }

        var halo = new Ellipse { Width = WelcomeShape.Logo * 2, Height = WelcomeShape.Logo * 2, IsHitTestVisible = false };
        var glow = new RadialGradientBrush(Swatch.WithAlpha(LogoArt.Opal[1], 0x55), Swatch.WithAlpha(LogoArt.Opal[1], 0));
        halo.Fill = glow;
        var art = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S4, 0, 0), Height = WelcomeShape.Logo * 1.6 };
        art.Children.Add(halo);
        art.Children.Add(logo);

        var page = Page(centered: true);
        page.Children.Add(art);
        page.Children.Add(Heading(Loc.F("welcome.title", AppInfo.Name), TypeRamp.Display, centered: true));
        page.Children.Add(Paragraph(Loc.T("welcome.intro"), centered: true));
        return page;
    }

    private FrameworkElement BuildProfiles()
    {
        var page = Page();
        page.Children.Add(Heading(Loc.T("welcome.profiles.title")));
        page.Children.Add(Paragraph(Loc.T("welcome.profiles.description")));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, Space.S4, 0, 0) };
        foreach (var profile in ProfileService.All)
        {
            var (start, end) = ProfileService.AccentOf(profile);
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new SquircleBorder
            {
                Width = Sizes.ProfileChip, Height = Sizes.ProfileChip, CornerRadius = Corners.Pill, Background = Paint.Gradient(start, end, 45),
                Margin = new Thickness(0, 0, Space.S5, 0),
                Child = new TextBlock
                {
                    Text = Icons.Glyph(profile.Icon), FontFamily = FontStack.Icons, FontSize = IconSize.M, Foreground = Paint.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
            header.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center });
            var option = Option(header, DescribeProfile(profile), "WelcomeProfile", profile == ProfileService.Active);
            option.Margin = new Thickness(0, Space.S4, Space.S4, 0);
            var id = profile.Id;
            option.Checked += (_, _) =>
            {
                if (_building || _snapshot) return;
                ProfileService.Switch(id); // dock et couleur changent tout de suite
                ShowStyle();
            };
            grid.Children.Add(option);
        }
        page.Children.Add(grid);
        return page;
    }

    private static string DescribeProfile(Profile profile) => profile.Id switch
    {
        ProfileService.PersoId => Loc.T("welcome.profile.perso"),
        "gaming" => Loc.T("welcome.profile.gaming"),
        "stage" => Loc.T("welcome.profile.stage"),
        "etudes" => Loc.T("welcome.profile.etudes"),
        _ => Loc.F("welcome.profile.other", profile.Pinned.Count),
    };

    private FrameworkElement BuildStyle()
    {
        var page = Page();
        page.Children.Add(Heading(Loc.T("welcome.style.title")));
        page.Children.Add(Paragraph(Loc.T("welcome.style.description")));

        // Aperçu : une île réduite avec le halo et le dégradé d'accent.
        _preview.Width = WelcomeShape.PreviewWidth;
        _preview.Height = WelcomeShape.PreviewHeight;
        _preview.CornerRadius = Corners.Pill;
        _preview.BorderThickness = Borders.Hairline;
        _preview.SetResourceReference(Border.BackgroundProperty, AppearanceService.IslandBrushKey);
        var content = new Grid();
        content.Children.Add(new TextBlock { Text = "12:34", FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S8, 0) };
        foreach (var height in new[] { 8.0, 14, 10, 12 })
        {
            var bar = new Rectangle { Width = 3, Height = height, RadiusX = Radius.Marker, RadiusY = Radius.Marker, Margin = new Thickness(Space.Px, 0, Space.Px, 0), VerticalAlignment = VerticalAlignment.Center };
            bar.SetResourceReference(Shape.FillProperty, AppearanceService.AccentGradientKey);
            bars.Children.Add(bar);
        }
        content.Children.Add(bars);
        _preview.Child = content;
        var previewHost = new Grid { Height = WelcomeShape.PreviewHeight + 2 * Space.S12, Effect = _previewGlow };
        previewHost.Children.Add(_preview);
        page.Children.Add(previewHost);

        _swatches.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var (name, start, end) in Swatch.AccentPresets)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Width = Sizes.Swatch, Height = Sizes.Swatch, Margin = new Thickness(Space.S2, 0, Space.S2, 0), ToolTip = Loc.T(name),
                Tag = (Swatch.Hex(start), Swatch.Hex(end)),
                Content = new Ellipse { Width = Sizes.SwatchDot, Height = Sizes.SwatchDot, Fill = Paint.Gradient(start, end, 45) },
            };
            swatch.Click += (_, _) => SettingsStore.Update(s =>
            {
                s.Appearance.AccentFromWallpaper = false; // une pastille = la couleur du profil, pas celle du fond d'écran
                s.Appearance.AccentStart = Swatch.Hex(start);
                s.Appearance.AccentEnd = Swatch.Hex(end);
            });
            _swatches.Children.Add(swatch);
        }
        _swatches.Margin = new Thickness(0, 0, 0, Space.S6);
        page.Children.Add(_swatches);
        page.Children.Add(Row(Loc.T("welcome.style.wallpaper_colors"), Loc.T("welcome.style.wallpaper_colors.description"), _wallpaperColorsToggle));
        page.Children.Add(Row(Loc.T("welcome.style.glass"), Loc.T("welcome.style.glass.description"), _glassToggle));
        return page;
    }

    private FrameworkElement BuildWallpaper()
    {
        var page = Page();
        page.Children.Add(Heading(Loc.T("welcome.wallpaper.title")));
        page.Children.Add(Paragraph(Loc.T("welcome.wallpaper.description")));
        _gallery.Margin = new Thickness(0, Space.S4, 0, 0);
        page.Children.Add(_gallery);
        page.Children.Add(Row(Loc.T("welcome.wallpaper.dynamic"), Loc.T("welcome.wallpaper.dynamic.description"), _dynamicToggle));
        BuildGallery();
        _thumbnails = LoadThumbnailsAsync();
        return page;
    }

    private async Task LoadThumbnailsAsync()
    {
        if (Thumbnails.Count < WallpaperArt.Designs.Length)
        {
            int width = (int)(WelcomeShape.Thumbnail * 2), height = width * 9 / 16;
            try
            {
                var images = await Task.Run(() => WallpaperArt.Designs.ToDictionary(d => d.Design, d => WallpaperArt.Render(d.Design, Wallpapers.Night, width, height)));
                foreach (var (design, image) in images) Thumbnails[design] = image;
            }
            catch (Exception ex)
            {
                Log.Error("Bienvenue : vignettes", ex);
            }
        }
        BuildGallery();
    }

    /// <summary>« Garder le mien » + les 5 fonds du pack ; le choix est encadré à la couleur d'accent.</summary>
    private void BuildGallery()
    {
        _gallery.Children.Clear();
        _gallery.Children.Add(GalleryTile(null, Loc.T("welcome.wallpaper.keep"), null));
        foreach (var (design, name) in WallpaperArt.Designs)
            _gallery.Children.Add(GalleryTile(design, name, Thumbnails.GetValueOrDefault(design)));
        _dynamicToggle.IsEnabled = _wallpaper != null;
        if (_wallpaper == null) _dynamicToggle.IsChecked = false;
    }

    private SquircleBorder GalleryTile(WallpaperDesign? design, string name, BitmapSource? image)
    {
        double width = WelcomeShape.Thumbnail, height = width * 9 / 16;
        var picture = new SquircleBorder
        {
            Width = width, Height = height, CornerRadius = Corners.M,
            Background = image != null ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : Paint.Surface,
        };
        if (image == null && design == null)
        {
            picture.Child = new TextBlock
            {
                Text = Icons.Wallpaper, FontFamily = FontStack.Icons, FontSize = IconSize.XXL, Foreground = Paint.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        var content = new StackPanel();
        content.Children.Add(picture);
        content.Children.Add(new TextBlock { Text = name, FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S3, 0, 0) });
        var tile = new SquircleBorder
        {
            Padding = new Thickness(Space.S2), Margin = new Thickness(0, 0, Space.S4, Space.S4), CornerRadius = Corners.L,
            BorderThickness = Borders.Focus, BorderBrush = Brushes.Transparent, Background = Paint.HitTarget, Cursor = Cursors.Hand, Child = content,
        };
        if (design == _wallpaper) tile.SetResourceReference(Border.BorderBrushProperty, AppearanceService.AccentBrushKey);
        tile.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // pas de déplacement de la fenêtre
            _wallpaper = design;
            BuildGallery();
        };
        return tile;
    }

    private FrameworkElement BuildModules()
    {
        var page = Page();
        page.Children.Add(Heading(Loc.T("welcome.modules.title")));
        page.Children.Add(Paragraph(Loc.T("welcome.modules.description")));
        // Deux colonnes de cartes courtes : tout tient sans faire défiler.
        var cards = new UniformGrid { Columns = 2 };
        foreach (var (title, description, toggle) in new[]
                 {
                     (Loc.T("welcome.modules.island"), Loc.T("welcome.modules.island.description"), _islandToggle),
                     (Loc.T("welcome.modules.dock"), Loc.T("welcome.modules.dock.description"), _dockToggle),
                     (Loc.T("welcome.modules.taskbar"), Loc.T("welcome.modules.taskbar.description"), _taskbarToggle),
                     (Loc.T("welcome.modules.menubar"), Loc.T("welcome.modules.menubar.description"), _menuBarToggle),
                     (Loc.T("welcome.modules.widgets"), Loc.T("welcome.modules.widgets.description"), _widgetsToggle),
                     (Loc.T("welcome.modules.startup"), Loc.T("welcome.modules.startup.description"), _startupToggle),
                 })
        {
            var card = Row(title, description, toggle);
            card.Margin = cards.Children.Count % 2 == 0 ? new Thickness(0, 0, Space.S2, Space.S4) : new Thickness(Space.S2, 0, 0, Space.S4);
            cards.Children.Add(card);
        }
        page.Children.Add(cards);
        page.Children.Add(new TextBlock { Text = Loc.T("welcome.modules.layout"), Style = (Style)FindResource("GroupTitle"), Margin = new Thickness(Space.S2, Space.S3, 0, Space.S4) });
        var layouts = new UniformGrid { Columns = 2 };
        _barLayout.Margin = new Thickness(0, 0, Space.S2, 0);
        _floatingLayout.Margin = new Thickness(Space.S2, 0, 0, 0);
        layouts.Children.Add(_barLayout);
        layouts.Children.Add(_floatingLayout);
        page.Children.Add(layouts);
        return page;
    }

    private FrameworkElement BuildDone()
    {
        var page = Page();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S4, 0, 0) };
        top.Children.Add(new Image { Source = OpalineLogo.Get(WelcomeShape.SmallLogo * 2), Width = WelcomeShape.SmallLogo, Height = WelcomeShape.SmallLogo, Margin = new Thickness(0, 0, Space.S8, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Heading(Loc.T("welcome.done.title"), margin: false));
        titles.Children.Add(Paragraph(Loc.T("welcome.done.description"), margin: false));
        top.Children.Add(titles);
        page.Children.Add(top);

        var settings = SettingsStore.Current;
        page.Children.Add(Tip(Icons.Move, Loc.T("welcome.tip.move")));
        page.Children.Add(Tip(Icons.Search, Loc.F("welcome.tip.palette", Hotkeys.Display(settings.Palette.Hotkey))));
        page.Children.Add(Tip(Icons.Dock, Loc.F("welcome.tip.dock", Hotkeys.Display(settings.Dock.RevealHotkey))));
        page.Children.Add(Tip(Icons.Settings, Loc.F("welcome.tip.settings", AppInfo.Name)));
        return page;
    }

    // ------------------------------------------------------------------ navigation

    private void GoTo(int index, bool animate = true)
    {
        if (index < 0 || index >= _pages.Count || index == _page) return;
        int direction = index > _page ? 1 : -1;
        var previous = _page >= 0 ? _pages[_page] : null;
        var next = _pages[index];
        _page = index;

        if (previous != null)
        {
            if (animate)
            {
                var leave = new DoubleAnimation(0, Motion.Base) { EasingFunction = EaseOut };
                leave.Completed += (_, _) =>
                {
                    if (_pages.IndexOf(previous) != _page) previous.Visibility = Visibility.Collapsed;
                };
                previous.BeginAnimation(OpacityProperty, leave);
                ((TranslateTransform)previous.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-direction * WelcomeShape.Slide, Motion.Base) { EasingFunction = EaseOut });
            }
            else
            {
                previous.Visibility = Visibility.Collapsed;
            }
        }

        next.Visibility = Visibility.Visible;
        var shift = (TranslateTransform)next.RenderTransform;
        if (animate)
        {
            next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Page) { EasingFunction = EaseOut, BeginTime = Motion.Delay });
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * WelcomeShape.Slide, 0, Motion.Page) { EasingFunction = EaseOut, BeginTime = Motion.Delay });
        }
        else
        {
            next.BeginAnimation(OpacityProperty, null);
            next.Opacity = 1;
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = 0;
        }

        _back.Visibility = index > 0 && index < _pages.Count - 1 ? Visibility.Visible : Visibility.Hidden;
        _nextText.Text = Loc.T(index == 0 ? "welcome.start" : index == _pages.Count - 1 ? "welcome.finish" : "welcome.next");
        for (int i = 0; i < _dots.Children.Count; i++)
        {
            var dot = (SquircleBorder)_dots.Children[i];
            double width = i == index ? WelcomeShape.ActiveDot : WelcomeShape.Dot;
            if (animate) dot.BeginAnimation(WidthProperty, new DoubleAnimation(width, Motion.Page) { EasingFunction = EaseOut });
            else dot.Width = width;
            if (i == index) dot.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
            else dot.Background = i < index ? Paint.ScrollThumbHover : Paint.Track;
        }
    }

    /// <summary>« C'est parti » : modules, fond d'écran et démarrage appliqués, puis l'assistant se referme.</summary>
    private async void Finish()
    {
        if (_finished) return;
        _finished = true;
        _next.IsEnabled = false;
        try
        {
            var design = _wallpaper;
            bool dynamic = design != null && _dynamicToggle.IsChecked == true;
            SettingsStore.Update(s =>
            {
                s.Island.Enabled = _islandToggle.IsChecked == true;
                s.Dock.Enabled = _dockToggle.IsChecked == true;
                s.Dock.Layout = _floatingLayout.IsChecked == true ? DockLayout.Floating : DockLayout.Bar;
                s.Dock.HideTaskbar = _taskbarToggle.IsChecked == true;
                s.MenuBar.Enabled = _menuBarToggle.IsChecked == true;
                s.DesktopWidgets.Enabled = _widgetsToggle.IsChecked == true;
                if (design is { } chosen)
                {
                    s.Wallpaper.Design = chosen;
                    s.Wallpaper.Dynamic = dynamic; // le fond dynamique sauve d'abord le fond d'origine
                }
                s.WelcomeDone = true;
            });
            bool startup = _startupToggle.IsChecked == true;
            if (startup != StartupService.IsEnabled) StartupService.SetEnabled(startup);
            if (design is { } picked && !dynamic && ProfileService.Active is { } profile)
            {
                _nextText.Text = Loc.T("welcome.drawing");
                var path = await Task.Run(() => WallpaperArt.RenderForScreen(picked));
                ProfileService.SetWallpaper(profile, path); // le fond d'origine est sauvé avant le premier changement
            }
        }
        catch (Exception ex)
        {
            Log.Error("Bienvenue : application des choix", ex);
        }
        CloseAnimated();
    }

    private void Skip()
    {
        MarkDone();
        CloseAnimated();
    }

    private static void MarkDone()
    {
        if (!SettingsStore.Current.WelcomeDone) SettingsStore.Update(s => s.WelcomeDone = true);
    }

    private void CloseAnimated()
    {
        if (_snapshot)
        {
            Close();
            return;
        }
        var fade = new DoubleAnimation(0, Motion.Swift) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => Close();
        _shell.BeginAnimation(OpacityProperty, fade);
        var shrink = new DoubleAnimation(0.96, Motion.Swift) { EasingFunction = EaseOut };
        _shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    // ------------------------------------------------------------------ couleur

    private void OnSettingsChanged() => ShowStyle();

    /// <summary>Pastille choisie, interrupteurs, aperçu de l'île et halo de la carte à la couleur actuelle.</summary>
    private void ShowStyle()
    {
        var look = SettingsStore.Current.Appearance;
        foreach (Button swatch in _swatches.Children)
        {
            var (start, end) = ((string, string))swatch.Tag;
            bool selected = !look.AccentFromWallpaper && start.Equals(look.AccentStart, StringComparison.OrdinalIgnoreCase)
                            && end.Equals(look.AccentEnd, StringComparison.OrdinalIgnoreCase);
            swatch.Background = selected ? Paint.SurfaceHover : Brushes.Transparent;
        }
        _wallpaperColorsToggle.IsChecked = look.AccentFromWallpaper;
        _preview.BorderBrush = new SolidColorBrush(AppearanceService.RimColor());
        AppearanceService.ApplyGlow(_previewGlow, Shadows.Island);
        AppearanceService.ApplyGlow(_shadow, Shadows.Window);
        _card.BorderBrush = new SolidColorBrush(AppearanceService.RimColor());
        // Le profil choisi a pu changer ailleurs (pastilles de l'île) : on le coche.
        if (_pages.Count > 1 && ((FrameworkElement)_pages[1]) is StackPanel profiles && profiles.Children.OfType<UniformGrid>().FirstOrDefault() is { } grid)
        {
            int i = 0;
            foreach (var profile in ProfileService.All)
            {
                if (i < grid.Children.Count && grid.Children[i] is RadioButton option && option.IsChecked != (profile == ProfileService.Active))
                {
                    bool building = _building;
                    _building = true;
                    option.IsChecked = profile == ProfileService.Active;
                    _building = building;
                }
                i++;
            }
        }
    }

    // ------------------------------------------------------------------ petits éléments

    private static StackPanel Page(bool centered = false) => new()
    {
        Margin = new Thickness(Space.S14, Space.S6, Space.S14, 0),
        VerticalAlignment = centered ? VerticalAlignment.Center : VerticalAlignment.Top,
    };

    private static TextBlock Heading(string text, double size = TypeRamp.LargeTitle, bool centered = false, bool margin = true) => new()
    {
        Text = text,
        FontFamily = FontStack.Display,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        Margin = margin ? new Thickness(0, Space.S4, 0, Space.S3) : new Thickness(0),
    };

    private static TextBlock Paragraph(string text, bool centered = false, bool margin = true) => new()
    {
        Text = text,
        FontSize = TypeRamp.Body,
        Foreground = Paint.TextSecondary,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        Margin = margin ? new Thickness(centered ? Space.S18 : 0, 0, centered ? Space.S18 : 0, Space.S4) : new Thickness(0, Space.S1, 0, 0),
    };

    /// <summary>Carte : titre + description à gauche, interrupteur à droite.</summary>
    private SquircleBorder Row(string title, string description, CheckBox toggle)
    {
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        texts.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(texts);
        Grid.SetColumn(toggle, 1);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(Space.S6, 0, 0, 0);
        grid.Children.Add(toggle);
        return new SquircleBorder { Style = (Style)FindResource("SettingCard"), Child = grid };
    }

    private Grid Tip(string glyph, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, Space.S8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new SquircleBorder
        {
            Width = WelcomeShape.TipIcon, Height = WelcomeShape.TipIcon, CornerRadius = Corners.Pill,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.L, Foreground = Paint.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        icon.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        var label = new TextBlock { Text = text, FontSize = TypeRamp.Body, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S7, 0, 0, 0) };
        Grid.SetColumn(label, 1);
        grid.Children.Add(icon);
        grid.Children.Add(label);
        return grid;
    }

    private CheckBox Switch(bool on, Action<bool>? live = null)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = on };
        if (live != null)
            toggle.Click += (_, _) =>
            {
                if (!_snapshot) live(toggle.IsChecked == true);
            };
        return toggle;
    }

    private RadioButton Option(object content, string description, string group, bool selected) => new()
    {
        Style = (Style)FindResource("OptionCard"),
        Content = content,
        Tag = description,
        GroupName = group,
        IsChecked = selected,
    };

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Mode --snapshot : les 6 pages en PNG, hors écran, sans rien appliquer.</summary>
    internal static async Task RenderSnapshotsAsync(string folder)
    {
        var window = new WelcomeWindow(snapshot: true) { Left = -30000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        if (window._thumbnails != null) await window._thumbnails;
        window._wallpaper = WallpaperDesign.Aurore; // montre un choix dans la galerie
        window.BuildGallery();
        for (int i = 0; i < window._pages.Count; i++)
        {
            window.GoTo(i, animate: false);
            await SnapshotRenderer.SaveAsync(window, System.IO.Path.Combine(folder, $"13-bienvenue-{i + 1}.png"));
        }
        window.Close();
    }
}
