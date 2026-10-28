using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using DynamicIsland.Dock;
using DynamicIsland.Services;
using DynamicIsland.Services.Remote;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;

namespace DynamicIsland.Settings;

public enum SettingsPage
{
    General,
    Appearance,
    Wallpaper,
    Island,
    Dock,
    Widgets,
    Profiles,
    Sharing,
    Connected,
    About,
}

/// <summary>
/// Fenêtre de paramètres (une seule instance). Chaque contrôle modifie <see cref="SettingsStore"/>,
/// qui prévient aussitôt les modules : rien à redémarrer.
/// </summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private bool _loading = true;

    private SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ApplyDarkRoundedFrame(new WindowInteropHelper(this).Handle);
            ApplyBackdrop();
        };
        BuildSwatches();
        HeaderLogo.Source = OpalineLogo.Get(60); // dessiné net (2× pour les écrans à 200 %)
        AboutLogo.Source = OpalineLogo.Get(96);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = Loc.F("settings.about.version", version?.ToString(3), Environment.Version.Major);
        SettingsPathText.Text = AppPaths.Settings;

        LoadValues();
        NavGeneral.IsChecked = true;
        SettingsStore.Changed += OnStoreChanged;
        GlobalHotkey.StateChanged += ShowHotkey;
        UpdateService.Changed += ShowUpdateState;
        DiscordVoice.Changed += ShowDiscordState;
        RemoteControl.Changed += OnRemoteChanged;
        Services.Rgb.RgbService.Changed += ShowRgb;
        Services.Friends.FriendsService.Changed += OnFriendsChanged;
        Closed += (_, _) =>
        {
            RemoteControl.Changed -= OnRemoteChanged;
            Services.Rgb.RgbService.Changed -= ShowRgb;
            Services.Friends.FriendsService.Changed -= OnFriendsChanged;
            WatchFriends(false);
            ClosePairingOnExit();
            DiscordVoice.Changed -= ShowDiscordState;
            SettingsStore.Changed -= OnStoreChanged;
            GlobalHotkey.StateChanged -= ShowHotkey;
            UpdateService.Changed -= ShowUpdateState;
        };
    }

    /// <summary>Un réglage a changé ailleurs (profil choisi dans l'île ou le dock…) : la page Profils suit.</summary>
    private void OnStoreChanged()
    {
        ShowHotkey();
        ShowAutoColors(); // couleurs du fond d'écran lues en arrière-plan, coordonnées de la ville trouvées…
        ShowWallpaperChoice();
        ShowPlacement(); // île ou dock déplacés en les faisant glisser
        if (ProfilesPage.Visibility == Visibility.Visible) LoadProfiles();
    }

    /// <summary>Ouvre la fenêtre (ou la ramène devant), éventuellement sur une section précise.</summary>
    public static void Open(SettingsPage? page = null)
    {
        if (_instance == null)
        {
            _instance = new SettingsWindow();
            _instance.Closed += (_, _) => _instance = null;
        }
        if (page is { } target) _instance.ShowPage(target);
        if (!_instance.IsVisible) _instance.Show();
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private void ShowPage(SettingsPage page)
    {
        var nav = page switch
        {
            SettingsPage.Appearance => NavAppearance,
            SettingsPage.Wallpaper => NavWallpaper,
            SettingsPage.Island => NavIsland,
            SettingsPage.Dock => NavDock,
            SettingsPage.Widgets => NavWidgets,
            SettingsPage.Profiles => NavProfiles,
            SettingsPage.Sharing => NavSharing,
            SettingsPage.Connected => NavConnected,
            SettingsPage.About => NavAbout,
            _ => NavGeneral,
        };
        nav.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        (RadioButton Nav, FrameworkElement Page)[] pages =
        [
            (NavGeneral, GeneralPage), (NavAppearance, AppearancePage), (NavWallpaper, WallpaperPage), (NavIsland, IslandPage), (NavDock, DockPage),
            (NavWidgets, WidgetsPage), (NavProfiles, ProfilesPage), (NavSharing, SharingPage), (NavConnected, ConnectedPage), (NavAbout, AboutPage),
        ];
        foreach (var (nav, page) in pages)
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavProfiles.IsChecked == true) LoadProfiles();
        if (NavIsland.IsChecked == true) ShowClaude();
        if (NavAbout.IsChecked == true) ShowSafety();
        if (NavWallpaper.IsChecked == true) _ = LoadWallpaperPageAsync();
        if (NavSharing.IsChecked == true) LoadSharing();
        if (NavConnected.IsChecked == true) ShowRemote();
        WatchFriends(NavConnected.IsChecked == true); // liste relue plus souvent tant que la page est ouverte
        Scroller.ScrollToTop();
    }

    // ------------------------------------------------------------------ valeurs

    private void LoadValues()
    {
        _loading = true;
        var settings = SettingsStore.Current;

        StartupToggle.IsChecked = StartupService.IsEnabled;
        IslandEnabledToggle.IsChecked = settings.Island.Enabled;
        DockEnabledToggle.IsChecked = settings.Dock.Enabled;
        PaletteToggle.IsChecked = settings.Palette.Enabled;
        ShowHotkey();

        ExpandDelaySlider.Value = settings.Island.ExpandDelayMs;
        CollapseDelaySlider.Value = settings.Island.CollapseDelayMs;
        IslandFullscreenToggle.IsChecked = settings.Island.HideInFullscreen;
        IslandGameLineToggle.IsChecked = settings.Island.GameLine;
        VisualizerToggle.IsChecked = settings.Island.Visualizer;
        AlbumGlowToggle.IsChecked = settings.Island.AlbumGlow;
        PrivacyToggle.IsChecked = settings.Island.PrivacyDots;
        VolumeHudToggle.IsChecked = settings.Island.VolumeHud;
        FollowMouseToggle.IsChecked = settings.Island.FollowMouse;
        DiscordToggle.IsChecked = settings.Discord.Enabled;
        ClaudeToggle.IsChecked = settings.Claude.Enabled;
        DiscordIdBox.Text = settings.Discord.ClientId;
        ShowDiscordState();
        FocusSlider.Value = settings.Island.PomodoroFocusMinutes;
        BreakSlider.Value = settings.Island.PomodoroBreakMinutes;

        WeatherToggle.IsChecked = settings.Widgets.Weather;
        CityBox.Text = settings.Widgets.City;
        CpuToggle.IsChecked = settings.Widgets.Cpu;
        RamToggle.IsChecked = settings.Widgets.Ram;
        GpuToggle.IsChecked = settings.Widgets.Gpu;
        NetworkToggle.IsChecked = settings.Widgets.Network;

        (settings.Dock.Mode switch
        {
            DockMode.Smart => SmartMode,
            DockMode.Peek => PeekMode,
            _ => FixedMode,
        }).IsChecked = true;
        (settings.Dock.Layout == DockLayout.Bar ? BarLayout : FloatingLayout).IsChecked = true;
        HideTaskbarToggle.IsChecked = settings.Dock.HideTaskbar;
        TrayIconsToggle.IsChecked = settings.Dock.ShowTrayIcons;
        MagnificationSlider.Value = settings.Dock.Magnification;
        MinimizeEffectToggle.IsChecked = settings.Dock.MinimizeEffect;
        GamingToggle.IsChecked = settings.Gaming.Auto;
        AutoUpdateToggle.IsChecked = settings.Updates.CheckAutomatically;

        var look = settings.Appearance;
        GlowSlider.Value = look.GlowIntensity;
        OpacitySlider.Value = look.Opacity;
        DockOpacitySlider.Value = look.DockOpacity;
        BarBlurToggle.IsChecked = look.DockBlur;
        BarBlurToggle.IsEnabled = DockBackdrop.IsSupported;
        RoundnessSlider.Value = look.Roundness;
        DockSizeSlider.Value = look.DockIconSize;
        SpeedSlider.Value = look.AnimationSpeed;
        BackdropToggle.IsChecked = look.SystemBackdrop;
        GlassToggle.IsChecked = look.LiquidGlass && Controls.LiquidGlassEffect.IsAvailable;
        GlassToggle.IsEnabled = Controls.LiquidGlassEffect.IsAvailable;
        if (!Controls.LiquidGlassEffect.IsAvailable) GlassDescription.Text = Loc.T("settings.glass.unavailable");
        GlassIntensitySlider.Value = look.GlassIntensity;
        GlassFollowToggle.IsChecked = look.GlassFollowMouse;
        WallpaperAccentToggle.IsChecked = look.AccentFromWallpaper;
        WallpaperTintToggle.IsChecked = look.TintFromWallpaper;
        WindowsAccentToggle.IsChecked = look.SyncWindowsAccent;
        AutoThemeToggle.IsChecked = look.AutoTheme;
        CursorToggle.IsChecked = look.ThemedCursor;
        HarmonizedIconsToggle.IsChecked = settings.Dock.HarmonizedIcons;
        TintIconsToggle.IsChecked = settings.Dock.TintIcons;
        MenuBarToggle.IsChecked = settings.MenuBar.Enabled;
        MenuBarReserveToggle.IsChecked = settings.MenuBar.ReserveSpace;
        MenuBarBlurToggle.IsChecked = settings.MenuBar.Blur;
        MenuBarBlurToggle.IsEnabled = DockBackdrop.IsSupported;
        DesktopWidgetsToggle.IsChecked = settings.DesktopWidgets.Enabled;
        LockWidgetsToggle.IsChecked = settings.DesktopWidgets.Locked;
        ClockWidgetToggle.IsChecked = settings.DesktopWidgets[DesktopWidgetKind.Clock].Visible;
        WeatherWidgetToggle.IsChecked = settings.DesktopWidgets[DesktopWidgetKind.Weather].Visible;
        MusicWidgetToggle.IsChecked = settings.DesktopWidgets[DesktopWidgetKind.Music].Visible;
        CalendarWidgetToggle.IsChecked = settings.DesktopWidgets[DesktopWidgetKind.Calendar].Visible;
        StatsWidgetToggle.IsChecked = settings.DesktopWidgets[DesktopWidgetKind.Stats].Visible;
        DynamicWallpaperToggle.IsChecked = settings.Wallpaper.Dynamic;
        AnimatedWallpaperToggle.IsChecked = settings.Wallpaper.Animated;
        AnimatedAllScreensToggle.IsChecked = settings.Wallpaper.AnimatedAllScreens;
        ShowAutoColors();
        BackdropToggle.IsEnabled = BackdropSupported;
        BackdropDescription.Text = BackdropSupported
            ? Loc.T("settings.mica.description")
            : Loc.T("settings.mica.unavailable");
        ShowAccent();
        ShowPlacement();
        ShowLanguage();
        ShowStatusItems();
        ShowIslandExtras();
        ShowRemote();
        ShowRgb();
        ShowFriends();
        SeasonsToggle.IsChecked = settings.Appearance.Seasons;

        UpdateValueTexts();
        _loading = false;
    }

    /// <summary>Un contrôle a changé : on recopie tout dans les réglages (simple et sans oubli).</summary>
    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateValueTexts();
        SettingsStore.Update(settings =>
        {
            settings.Island.Enabled = IslandEnabledToggle.IsChecked == true;
            settings.Island.ExpandDelayMs = RoundTo(ExpandDelaySlider.Value, 10);
            settings.Island.CollapseDelayMs = RoundTo(CollapseDelaySlider.Value, 10);
            settings.Island.HideInFullscreen = IslandFullscreenToggle.IsChecked == true;
            settings.Island.GameLine = IslandGameLineToggle.IsChecked == true;
            settings.Island.Visualizer = VisualizerToggle.IsChecked == true;
            settings.Island.AlbumGlow = AlbumGlowToggle.IsChecked == true;
            settings.Island.PrivacyDots = PrivacyToggle.IsChecked == true;
            settings.Island.VolumeHud = VolumeHudToggle.IsChecked == true;
            settings.Island.FollowMouse = FollowMouseToggle.IsChecked == true;
            settings.Discord.Enabled = DiscordToggle.IsChecked == true;
            settings.Claude.Enabled = ClaudeToggle.IsChecked == true;
            settings.Island.PomodoroFocusMinutes = RoundTo(FocusSlider.Value, 5);
            settings.Island.PomodoroBreakMinutes = RoundTo(BreakSlider.Value, 1);

            settings.Widgets.Weather = WeatherToggle.IsChecked == true;
            settings.Widgets.Cpu = CpuToggle.IsChecked == true;
            settings.Widgets.Ram = RamToggle.IsChecked == true;
            settings.Widgets.Gpu = GpuToggle.IsChecked == true;
            settings.Widgets.Network = NetworkToggle.IsChecked == true;

            settings.Dock.Enabled = DockEnabledToggle.IsChecked == true;
            settings.Palette.Enabled = PaletteToggle.IsChecked == true;
            settings.Dock.Mode = SmartMode.IsChecked == true ? DockMode.Smart
                : PeekMode.IsChecked == true ? DockMode.Peek
                : DockMode.Fixed;
            settings.Dock.Layout = FloatingLayout.IsChecked == true ? DockLayout.Floating : DockLayout.Bar;
            settings.Dock.HideTaskbar = HideTaskbarToggle.IsChecked == true;
            settings.Dock.ShowTrayIcons = TrayIconsToggle.IsChecked == true;
            settings.Dock.Magnification = Math.Round(MagnificationSlider.Value, 2);
            settings.Dock.MinimizeEffect = MinimizeEffectToggle.IsChecked == true;
            settings.Gaming.Auto = GamingToggle.IsChecked == true;
            settings.Updates.CheckAutomatically = AutoUpdateToggle.IsChecked == true;

            settings.Appearance.GlowIntensity = Math.Round(GlowSlider.Value, 2);
            settings.Appearance.Opacity = Math.Round(OpacitySlider.Value, 2);
            settings.Appearance.DockOpacity = Math.Round(DockOpacitySlider.Value, 2);
            settings.Appearance.DockBlur = BarBlurToggle.IsChecked == true;
            settings.Appearance.Roundness = Math.Round(RoundnessSlider.Value, 2);
            settings.Appearance.DockIconSize = RoundTo(DockSizeSlider.Value, 2);
            settings.Appearance.AnimationSpeed = Math.Round(SpeedSlider.Value, 2);
            settings.Appearance.SystemBackdrop = BackdropToggle.IsChecked == true;
            settings.Appearance.LiquidGlass = GlassToggle.IsChecked == true;
            settings.Appearance.GlassIntensity = Math.Round(GlassIntensitySlider.Value, 2);
            settings.Appearance.GlassFollowMouse = GlassFollowToggle.IsChecked == true;
            settings.Appearance.AccentFromWallpaper = WallpaperAccentToggle.IsChecked == true;
            settings.Appearance.TintFromWallpaper = WallpaperTintToggle.IsChecked == true;
            settings.Appearance.SyncWindowsAccent = WindowsAccentToggle.IsChecked == true;
            settings.Appearance.AutoTheme = AutoThemeToggle.IsChecked == true;
            settings.Appearance.ThemedCursor = CursorToggle.IsChecked == true;
            settings.Dock.HarmonizedIcons = HarmonizedIconsToggle.IsChecked == true;
            settings.Dock.TintIcons = TintIconsToggle.IsChecked == true;
            settings.MenuBar.Enabled = MenuBarToggle.IsChecked == true;
            settings.MenuBar.ReserveSpace = MenuBarReserveToggle.IsChecked == true;
            settings.MenuBar.Blur = MenuBarBlurToggle.IsChecked == true;
            settings.DesktopWidgets.Enabled = DesktopWidgetsToggle.IsChecked == true;
            settings.DesktopWidgets.Locked = LockWidgetsToggle.IsChecked == true;
            settings.DesktopWidgets[DesktopWidgetKind.Clock].Visible = ClockWidgetToggle.IsChecked == true;
            settings.DesktopWidgets[DesktopWidgetKind.Weather].Visible = WeatherWidgetToggle.IsChecked == true;
            settings.DesktopWidgets[DesktopWidgetKind.Music].Visible = MusicWidgetToggle.IsChecked == true;
            settings.DesktopWidgets[DesktopWidgetKind.Calendar].Visible = CalendarWidgetToggle.IsChecked == true;
            settings.DesktopWidgets[DesktopWidgetKind.Stats].Visible = StatsWidgetToggle.IsChecked == true;
            settings.Wallpaper.Dynamic = DynamicWallpaperToggle.IsChecked == true;
            settings.Wallpaper.Animated = AnimatedWallpaperToggle.IsChecked == true;
            settings.Wallpaper.AnimatedAllScreens = AnimatedAllScreensToggle.IsChecked == true;
        });
        ApplyBackdrop();
        ShowAutoColors();
    }

    /// <summary>Couleurs extraites du fond d'écran (pastilles) et horaires du thème automatique.</summary>
    private void ShowAutoColors()
    {
        var look = SettingsStore.Current.Appearance;
        bool on = look.AccentFromWallpaper;
        WallpaperTintToggle.IsEnabled = on;
        WallpaperColorsRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && AppearanceService.UsesWallpaperColors)
        {
            AppearanceColors.TryParse(look.WallpaperAccentStart, out var start);
            AppearanceColors.TryParse(look.WallpaperAccentEnd, out var end);
            WallpaperStartDot.Fill = Paint.Frozen(start);
            WallpaperEndDot.Fill = Paint.Frozen(end);
            WallpaperSurfaceDot.Background = Paint.Frozen(AppearanceService.SurfaceBase);
            WallpaperColorsText.Text = Loc.F("settings.wallpaper_colors.found", look.WallpaperAccentStart, look.WallpaperAccentEnd);
        }
        else if (on)
        {
            WallpaperColorsText.Text = Loc.T("settings.wallpaper_colors.reading");
        }
        AutoThemeDescription.Text = AutoThemeService.Describe() + " " + Loc.T("settings.auto_theme.restore");
        ShowCursorPreview();
        MarkSelectedSwatch(); // pas les champs : l'utilisateur est peut-être en train d'y taper
    }

    /// <summary>Aperçu des trois curseurs aux couleurs d'accent actuelles (redessiné si elles changent).</summary>
    private void ShowCursorPreview()
    {
        var accent = (AppearanceService.AccentStart, AppearanceService.AccentEnd);
        if (CursorPreview.Tag is ValueTuple<Color, Color> shown && shown == accent) return;
        CursorPreview.Tag = accent;
        CursorPreview.Children.Clear();
        foreach (var shape in new[] { CursorTheme.Shape.Arrow, CursorTheme.Shape.Hand, CursorTheme.Shape.IBeam })
        {
            CursorPreview.Children.Add(new Image
            {
                Source = CursorTheme.Render(shape, accent.AccentStart, accent.AccentEnd, CursorArt.PreviewSize),
                Width = CursorArt.PreviewSize / 1.5,
                Height = CursorArt.PreviewSize / 1.5,
                Margin = new Thickness(Space.S1, 0, Space.S1, 0),
            });
        }
    }

    private void UpdateValueTexts()
    {
        ExpandDelayText.Text = $"{RoundTo(ExpandDelaySlider.Value, 10)} ms";
        CollapseDelayText.Text = $"{RoundTo(CollapseDelaySlider.Value, 10)} ms";
        MagnificationText.Text = $"×{MagnificationSlider.Value:0.00}";
        FocusText.Text = $"{RoundTo(FocusSlider.Value, 5)} min";
        BreakText.Text = $"{RoundTo(BreakSlider.Value, 1)} min";
        GlowText.Text = Loc.F("format.percent", Math.Round(GlowSlider.Value * 100));
        OpacityText.Text = Loc.F("format.percent", Math.Round(OpacitySlider.Value * 100));
        DockOpacityText.Text = Loc.F("format.percent", Math.Round(DockOpacitySlider.Value * 100));
        RoundnessText.Text = Loc.F("format.percent", Math.Round(RoundnessSlider.Value * 100));
        DockSizeText.Text = $"{RoundTo(DockSizeSlider.Value, 2)} px";
        SpeedText.Text = $"×{SpeedSlider.Value:0.0}";
        GlassIntensityText.Text = Loc.F("format.percent", Math.Round(GlassIntensitySlider.Value * 100));
        foreach (var box in new[] { ClockWidgetToggle, WeatherWidgetToggle, MusicWidgetToggle, CalendarWidgetToggle, StatsWidgetToggle, LockWidgetsToggle })
            box.IsEnabled = DesktopWidgetsToggle.IsChecked == true;
        GlassIntensitySlider.IsEnabled = GlassFollowToggle.IsEnabled = GlassToggle.IsChecked == true;
    }

    private static int RoundTo(double value, int step) => (int)Math.Round(value / step) * step;

    // ------------------------------------------------------------------ couleur d'accent

    /// <summary>Pastilles d'accent (tokens) en codes « #RRGGBB », la forme enregistrée dans les réglages.</summary>
    private static readonly (string Name, string Start, string End)[] AccentPresets =
        Swatch.AccentPresets.Select(p => (Loc.T(p.Name), Swatch.Hex(p.Start), Swatch.Hex(p.End))).ToArray();

    private void BuildSwatches()
    {
        foreach (var (name, start, end) in AccentPresets)
        {
            AppearanceColors.TryParse(start, out var a);
            AppearanceColors.TryParse(end, out var b);
            var swatch = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Width = Sizes.Swatch, Height = Sizes.Swatch, Margin = new Thickness(0, 0, Space.S4, 0), ToolTip = name, Tag = (start, end),
                Content = new Ellipse { Width = Sizes.SwatchDot, Height = Sizes.SwatchDot, Fill = Paint.Gradient(a, b, 45) },
            };
            swatch.Click += (_, _) => SetAccent(start, end);
            AccentSwatches.Children.Add(swatch);
        }
    }

    private void SetAccent(string start, string end)
    {
        SettingsStore.Update(s =>
        {
            s.Appearance.AccentFromWallpaper = false; // choisir une couleur = revenir à la couleur du profil
            s.Appearance.AccentStart = start;
            s.Appearance.AccentEnd = end;
        });
        ShowAccent();
    }

    /// <summary>Entoure la pastille choisie et recopie les codes dans les champs.</summary>
    private void ShowAccent()
    {
        var look = SettingsStore.Current.Appearance;
        AccentStartBox.Text = look.AccentStart;
        AccentEndBox.Text = look.AccentEnd;
        MarkSelectedSwatch();
    }

    private void MarkSelectedSwatch()
    {
        var look = SettingsStore.Current.Appearance;
        foreach (Button swatch in AccentSwatches.Children)
        {
            var (start, end) = ((string, string))swatch.Tag;
            bool selected = !look.AccentFromWallpaper // c'est alors le fond d'écran qui choisit
                            && start.Equals(look.AccentStart, StringComparison.OrdinalIgnoreCase) && end.Equals(look.AccentEnd, StringComparison.OrdinalIgnoreCase);
            swatch.Background = selected ? Paint.SurfaceHover : Brushes.Transparent;
        }
    }

    private void AccentBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyAccentBoxes();
    }

    private void AccentBox_LostFocus(object sender, RoutedEventArgs e) => ApplyAccentBoxes();

    private void ApplyAccentBoxes()
    {
        if (!AppearanceColors.TryParse(AccentStartBox.Text, out var start)) { ShowAccent(); return; }
        var end = AppearanceColors.TryParse(AccentEndBox.Text, out var parsed) ? parsed : start;
        SetAccent(AppearanceColors.ToHex(start), AppearanceColors.ToHex(end));
    }

    // ------------------------------------------------------------------ Mica

    /// <summary>Windows 11 22H2+ avec les effets de transparence activés.</summary>
    private static bool BackdropSupported =>
        Environment.OSVersion.Version.Build >= 22621 &&
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is not 0;

    /// <summary>Mica derrière la fenêtre (fond transparent) ou repli sur un fond uni.</summary>
    private void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool mica = BackdropSupported && SettingsStore.Current.Appearance.SystemBackdrop;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = mica ? Colors.Transparent : Swatch.Window;
        Background = mica ? Brushes.Transparent : Paint.Window;
        NavPanel.Background = mica ? Paint.SidebarOverMica : Paint.Sidebar;

        var margins = new MARGINS { Left = mica ? -1 : 0, Right = mica ? -1 : 0, Top = mica ? -1 : 0, Bottom = mica ? -1 : 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int type = mica ? 2 : 1; // DWMSBT_MAINWINDOW (Mica) / DWMSBT_NONE
        DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    // ------------------------------------------------------------------ ville de la météo

    private void CityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyCity();
    }

    private void CityBox_LostFocus(object sender, RoutedEventArgs e) => ApplyCity();

    private void ApplyCity()
    {
        var city = CityBox.Text.Trim();
        if (city.Length == 0 || city == SettingsStore.Current.Widgets.City) return;
        SettingsStore.Update(s => s.Widgets.City = city);
    }

    // ------------------------------------------------------------------ Discord

    private void ShowDiscordState()
    {
        DiscordStatusText.Text = DiscordVoice.InVoice
            ? Loc.F("settings.discord.in_channel", DiscordVoice.Status, DiscordVoice.Channel, DiscordVoice.Members.Count)
            : DiscordVoice.Status;
    }

    private void DiscordIdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyDiscordId();
    }

    private void DiscordIdBox_LostFocus(object sender, RoutedEventArgs e) => ApplyDiscordId();

    private void ApplyDiscordId()
    {
        var id = DiscordIdBox.Text.Trim();
        if (id == SettingsStore.Current.Discord.ClientId) return;
        SettingsStore.Update(s =>
        {
            s.Discord.ClientId = id;
            if (id.Length > 0) s.Discord.Enabled = true; // coller l'ID = vouloir l'utiliser
        });
        DiscordToggle.IsChecked = SettingsStore.Current.Discord.Enabled;
    }

    private void DiscordConnect_Click(object sender, RoutedEventArgs e)
    {
        ApplyDiscordId();
        DiscordVoice.Reconnect();
    }

    private void DiscordForget_Click(object sender, RoutedEventArgs e) => DiscordVoice.Forget();

    // ------------------------------------------------------------------ raccourci de la palette

    private void ShowHotkey()
    {
        var settings = SettingsStore.Current;
        if (!HotkeyBox.IsKeyboardFocused) HotkeyBox.Text = Hotkeys.Display(settings.Palette.Hotkey);
        bool error = settings.Palette.Enabled && GlobalHotkey.Error != null;
        HotkeyStatus.Text = error ? GlobalHotkey.Error : Loc.T("settings.hotkey.help");
        HotkeyStatus.Foreground = error ? Paint.Timer : Paint.TextSecondary;

        if (!RevealHotkeyBox.IsKeyboardFocused) RevealHotkeyBox.Text = Hotkeys.Display(settings.Dock.RevealHotkey);
        bool revealError = settings.Dock.Enabled && GlobalHotkey.RevealError != null;
        RevealHotkeyStatus.Text = revealError ? GlobalHotkey.RevealError
            : Loc.T("settings.reveal_hotkey.help");
        RevealHotkeyStatus.Foreground = revealError ? Paint.Timer : Paint.TextSecondary;
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        GlobalHotkey.Suspend(true); // sinon le raccourci actuel se déclencherait au lieu d'être saisi
        ((TextBox)sender).Text = Loc.T("settings.hotkey.press");
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        GlobalHotkey.Suspend(false);
        ShowHotkey();
    }

    /// <summary>Saisie d'un raccourci (palette ou « Afficher le dock », selon la case).</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var modifiers = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= ModifierKeys.Windows;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            Keyboard.ClearFocus(); // annule : l'ancien raccourci revient
            return;
        }
        if (Hotkeys.IsModifier(key))
        {
            box.Text = Hotkeys.DisplayPartial(modifiers);
            return;
        }
        if (!Hotkeys.HasMainModifier(modifiers))
        {
            box.Text = Loc.T("settings.hotkey.add_modifier");
            return;
        }
        var gesture = Hotkeys.Format(modifiers, key);
        if (box == RevealHotkeyBox) SettingsStore.Update(s => s.Dock.RevealHotkey = gesture);
        else SettingsStore.Update(s => s.Palette.Hotkey = gesture);
        Keyboard.ClearFocus(); // le nouveau raccourci est enregistré en quittant la case
    }

    private void DefaultHotkey_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Palette.Hotkey = Hotkeys.Default);

    private void ResetDesktopWidgets_Click(object sender, RoutedEventArgs e) => DesktopWidgets.DesktopWidgetsService.ResetPlaces();

    private void DefaultRevealHotkey_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Dock.RevealHotkey = Hotkeys.DefaultReveal);

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        StartupService.SetEnabled(StartupToggle.IsChecked == true);
        StartupToggle.IsChecked = StartupService.IsEnabled; // reflète ce qui a vraiment été écrit
    }

    // ------------------------------------------------------------------ À propos

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDataFolder();
        Start("explorer.exe", $"\"{AppPaths.DataFolder}\"");
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Log.FilePath)) Start("notepad.exe", $"\"{Log.FilePath}\"");
        else MessageBox.Show(this, Loc.T("settings.log.empty"), Loc.T("settings.log.title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    /// <summary>Mode --snapshot : rend chaque section dans un PNG, hors écran.</summary>
    internal static async Task RenderSnapshotsAsync(string folder)
    {
        var window = new SettingsWindow { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0 };
        ((Grid)window.Content).Background = window.Background; // le fond de la fenêtre n'est pas dans le contenu
        window.Show();
        foreach (var page in new[] { SettingsPage.General, SettingsPage.Appearance, SettingsPage.Wallpaper, SettingsPage.Island, SettingsPage.Dock, SettingsPage.Widgets, SettingsPage.Profiles, SettingsPage.Sharing, SettingsPage.Connected, SettingsPage.About })
        {
            window.ShowPage(page);
            if (page == SettingsPage.Connected)
            {
                window.ShowRemoteSample(); // QR et téléphone d'exemple (rien n'écoute)
                window.ShowFriendsSample();
                if (window.RemoteQr.Source is System.Windows.Media.Imaging.BitmapSource qr)
                    File.WriteAllBytes(Path.Combine(folder, "7-qr-telecommande.png"), OpalineLogo.Png(qr)); // à décoder pour vérifier
            }
            if (page == SettingsPage.Wallpaper) await window.LoadWallpaperPageAsync(); // vignettes dessinées
            await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, $"7-parametres-{page.ToString().ToLowerInvariant()}.png"));
        }
        window.Close();
    }

    private static void Start(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Paramètres : ouverture de {file}", ex);
        }
    }
}
