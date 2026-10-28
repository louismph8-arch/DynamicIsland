using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.MenuBar;

/// <summary>
/// Barre de menus façon macOS (5F, option) : fine bande en haut de l'écran principal. À gauche, le menu de l'app
/// (paramètres, verrouiller, veille, redémarrer, éteindre) puis l'appli au premier plan (icône, nom, titre ; clic =
/// réduire / agrandir / fermer). Au centre, rien : l'île est là. À droite, météo, volume (clic = muet, molette),
/// réseau, batterie (seulement si le PC en a une), date et heure (clic = centre de notifications, Win+N).
/// Fenêtre outil, toujours devant, jamais active ; l'espace est réservé comme une barre des tâches (appbar,
/// libérée à la fermeture et par le gardien). Cachée en plein écran et en mode Gaming. Vrai flou acrylique dessous.
/// </summary>
public sealed class MenuBarWindow : Window
{
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_NAMECHANGE = 0x800C;

    private readonly DispatcherTimer _refresh = new() { Interval = MenuBarShape.Refresh };
    private readonly VolumeService _volume = new();
    private readonly Image _appIcon = new() { Width = MenuBarShape.AppIcon, Height = MenuBarShape.AppIcon };
    private readonly TextBlock _appName = new() { FontWeight = FontWeights.Bold, FontSize = TypeRamp.Body, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _windowTitle = new()
    {
        FontSize = TypeRamp.Body, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = MenuBarShape.TitleMaxWidth, Margin = new Thickness(Space.S4, 0, 0, 0),
    };
    private readonly Button _logoButton, _appButton, _weather, _volumeButton, _network, _battery, _clock;
    private readonly List<IntPtr> _hooks = [];
    private readonly DockNative.WinEventProc _hookProc;
    private IntPtr _hwnd, _foreground;
    private AppBar? _appBar;
    private DockBackdrop? _backdrop;
    private uint _taskbarCreated;
    private bool _hiddenForFullscreen;
    private RECT _bounds; // place de la barre (pixels)

    public MenuBarWindow(bool snapshot = false)
    {
        IsSnapshot = snapshot;
        Title = "Dynamic Island — Barre de menus";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;
        Height = MenuBarShape.Height;

        var bar = new Border { BorderThickness = new Thickness(0, 0, 0, Stroke.Hairline), BorderBrush = Paint.Divider };
        bar.SetResourceReference(Border.BackgroundProperty, AppearanceService.DockBrushKey);
        var grid = new Grid { Margin = new Thickness(MenuBarShape.SidePadding, 0, MenuBarShape.SidePadding, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Child = grid;
        Content = bar;

        // ---- gauche : menu de l'app, appli au premier plan
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Image { Source = OpalineLogo.Get((int)MenuBarShape.AppIcon * 2), Width = MenuBarShape.AppIcon, Height = MenuBarShape.AppIcon };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        _logoButton = Item(logo, AppInfo.Name, ShowSystemMenu);
        left.Children.Add(_logoButton);
        var appContent = new StackPanel { Orientation = Orientation.Horizontal };
        RenderOptions.SetBitmapScalingMode(_appIcon, BitmapScalingMode.HighQuality);
        appContent.Children.Add(_appIcon);
        _appName.Margin = new Thickness(Space.S3, 0, 0, 0);
        appContent.Children.Add(_appName);
        _appButton = Item(appContent, Loc.T("menubar.window"), ShowWindowMenu);
        left.Children.Add(_appButton);
        left.Children.Add(_windowTitle);
        grid.Children.Add(left);

        // ---- droite : état + heure
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 2);
        _weather = Item(LabeledGlyph(Icons.WeatherCloud, ""), Loc.T("settings.meteo"), null);
        _volumeButton = Item(Glyph(Icons.VolumeHigh), Loc.T("menubar.volume"), () => { _volume.ToggleMute(); UpdateStatus(); });
        _volumeButton.PreviewMouseWheel += (sender, e) =>
        {
            if (_volume.TryRead(out double level, out _)) _volume.SetLevel(level + Math.Sign(e.Delta) * MenuBarShape.VolumeWheelStep);
            UpdateStatus();
            e.Handled = true;
        };
        _network = Item(Glyph(Icons.Wifi), Loc.T("settings.reseau"), () => Open("ms-settings:network"));
        _battery = Item(LabeledGlyph(Icons.Battery, ""), Loc.T("menubar.battery"), () => Open("ms-settings:batterysaver"));
        _clock = Item(new TextBlock { FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            Loc.T("menubar.notifications"), OpenNotificationCenter);
        foreach (var item in new[] { _weather, _volumeButton, _network, _battery, _clock }) right.Children.Add(item);
        grid.Children.Add(right);

        _hookProc = OnWinEvent;
        _refresh.Tick += (_, _) => UpdateStatus();
        WeatherService.Shared.Changed += UpdateWeather;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        SettingsStore.Changed += ApplySettings;
        GamingMode.Changed += UpdateVisibility;
        SourceInitialized += (_, _) => Setup();
        Closed += (_, _) => Cleanup();

        UpdateForeground(GetForegroundWindow());
        UpdateStatus();
        UpdateNetwork();
        UpdateWeather();
    }

    public bool IsSnapshot { get; }

    // ------------------------------------------------------------------ fenêtre, espace réservé

    private void Setup()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(_hwnd, GWL_EXSTYLE, (GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
        if (IsSnapshot)
        {
            Left = -30000;
            Width = 1920;
            return;
        }
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        _appBar = new AppBar(_hwnd, AppBar.EdgeTop, AppPaths.MenuBarHandle, "DynamicIsland.MenuBar.AppBar");
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _hooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT));
        _hooks.Add(SetWinEventHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT));
        _refresh.Start();
        ApplySettings();
    }

    private void Cleanup()
    {
        _refresh.Stop();
        foreach (var hook in _hooks.Where(h => h != IntPtr.Zero)) UnhookWinEvent(hook);
        _hooks.Clear();
        WeatherService.Shared.Changed -= UpdateWeather;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        SettingsStore.Changed -= ApplySettings;
        GamingMode.Changed -= UpdateVisibility;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        ReleaseSpace();
        _backdrop?.Close();
    }

    /// <summary>Rend l'espace réservé en haut de l'écran (fermeture, option décochée).</summary>
    public void ReleaseSpace() => _appBar?.Release();

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplySettings);

    private void ApplySettings()
    {
        if (_hwnd == IntPtr.Zero || IsSnapshot) return;
        var settings = SettingsStore.Current.MenuBar;
        var (bounds, _, scale) = ScreenInfo.Primary();
        int height = (int)Math.Ceiling(MenuBarShape.Height * scale);
        if (settings.ReserveSpace) _bounds = _appBar!.ReserveRect(height);
        else
        {
            _appBar!.Release();
            _bounds = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Top + height };
        }
        Left = _bounds.Left / scale;
        Top = _bounds.Top / scale;
        Width = (_bounds.Right - _bounds.Left) / scale;
        Height = MenuBarShape.Height;
        UpdateVisibility();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBar != null && msg == _appBar.CallbackMessage && wParam.ToInt64() == ABN_POSCHANGED)
            Dispatcher.BeginInvoke(ApplySettings); // barre des tâches ou dock déplacés
        else if (msg == _taskbarCreated && _taskbarCreated != 0)
            Dispatcher.BeginInvoke(() =>
            {
                _appBar?.Release(); // l'Explorateur a redémarré : on se réenregistre
                ApplySettings();
            });
        return IntPtr.Zero;
    }

    /// <summary>Cachée quand une appli est en plein écran ou en mode Gaming ; flou acrylique dessous sinon.</summary>
    private void UpdateVisibility()
    {
        if (IsSnapshot || _hwnd == IntPtr.Zero) return;
        bool hide = GamingMode.IsActive || ScreenInfo.IsForegroundFullscreen();
        _hiddenForFullscreen = hide;
        Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        bool blur = !hide && SettingsStore.Current.MenuBar.Blur && DockBackdrop.IsSupported;
        if (!blur)
        {
            _backdrop?.Hide();
            return;
        }
        _backdrop ??= new DockBackdrop();
        _backdrop.Place(_bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top, _hwnd);
    }

    // ------------------------------------------------------------------ appli au premier plan

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return;
        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            UpdateForeground(hWnd);
            if (_hiddenForFullscreen != (GamingMode.IsActive || ScreenInfo.IsForegroundFullscreen())) UpdateVisibility();
        }
        else if (hWnd == _foreground) _windowTitle.Text = WindowTitle(hWnd); // titre changé (onglet du navigateur…)
    }

    private void UpdateForeground(IntPtr hWnd)
    {
        var root = hWnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hWnd, GA_ROOTOWNER);
        GetWindowThreadProcessId(root, out uint processId);
        if (processId == Environment.ProcessId) return; // l'île, le dock, la barre elle-même : on garde l'appli d'avant

        var className = ClassName(root);
        if (root == IntPtr.Zero || className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            _foreground = IntPtr.Zero;
            _appName.Text = Loc.T("folders.desktop");
            _windowTitle.Text = "";
            _appIcon.Source = null;
            _appIcon.Visibility = Visibility.Collapsed;
            return;
        }
        _foreground = root;
        var title = WindowTitle(root);
        var appId = className == "ApplicationFrameWindow" ? ShellNative.GetWindowAppId(root) : null;
        var item = DockItem.FromWindow(new AppWindow(root, title, GetProcessPath(processId), appId, false));
        _appName.Text = item.Name;
        _windowTitle.Text = title == item.Name ? "" : title;
        _appIcon.Source = item.BaseIcon; // icône d'origine (le style harmonisé est pour le dock)
        _appIcon.Visibility = item.BaseIcon != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowWindowMenu()
    {
        var window = _foreground;
        if (window == IntPtr.Zero || !IsWindow(window)) return;
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.T("window.minimize"), () => WindowActions.Minimize(window)));
        bool maximized = IsZoomed(window);
        menu.Items.Add(Entry(Loc.T(maximized ? "window.restore" : "window.maximize"), () => ShowWindowAsync(window, maximized ? SW_RESTORE : SW_MAXIMIZE)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("window.close"), () => WindowActions.Close(window)));
        OpenMenu(menu, _appButton);
    }

    /// <summary>Menu de l'app : paramètres, puis verrouiller / veille / redémarrer / éteindre (comme le menu Pomme).</summary>
    private void ShowSystemMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.F("palette.settings", AppInfo.Name) + "…", () => SettingsWindow.Open()));
        menu.Items.Add(Entry(Loc.F("menubar.about", AppInfo.Name), () => SettingsWindow.Open(SettingsPage.About)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("menubar.lock"), () => LockWorkStation()));
        menu.Items.Add(Entry(Loc.T("menubar.sleep"), () => SetSuspendState(false, false, false)));
        menu.Items.Add(Entry(Loc.T("menubar.restart"), () => Confirm(Loc.T("menubar.restart.confirm"), "/r /t 0")));
        menu.Items.Add(Entry(Loc.T("menubar.shutdown"), () => Confirm(Loc.T("menubar.shutdown.confirm"), "/s /t 0")));
        OpenMenu(menu, _logoButton);
    }

    private static void Confirm(string question, string arguments)
    {
        if (MessageBox.Show(question + " " + Loc.T("menubar.apps_will_close"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { CreateNoWindow = true, UseShellExecute = false });
    }

    // ------------------------------------------------------------------ état à droite

    private void UpdateStatus()
    {
        var now = DateTime.Now;
        ((TextBlock)_clock.Content).Text = $"{now.ToString("ddd d MMM", Loc.Culture)}   {now:HH:mm}";

        if (_volume.TryRead(out double level, out bool muted))
        {
            ((TextBlock)_volumeButton.Content).Text = muted || level < 0.005 ? Icons.VolumeMute
                : level < 0.34 ? Icons.VolumeLow : level < 0.67 ? Icons.VolumeMedium : Icons.VolumeHigh;
            _volumeButton.ToolTip = muted ? Loc.T("menubar.volume.muted") : Loc.F("menubar.volume.level", Math.Round(level * 100));
        }

        // Batterie : seulement si le PC en a une (un PC fixe n'en affiche jamais).
        bool hasBattery = GetSystemPowerStatus(out var power) && power.BatteryFlag is not (128 or 255);
        _battery.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (hasBattery)
        {
            int percent = power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : 0;
            var parts = ((StackPanel)_battery.Content).Children;
            var glyph = (TextBlock)parts[0];
            var text = (TextBlock)parts[1];
            glyph.Text = power.ACLineStatus == 1 ? Icons.BatteryCharging : percent < 20 ? Icons.BatteryLow : percent < 70 ? Icons.BatteryMedium : Icons.BatteryFull;
            text.Text = Loc.F("format.percent", percent);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateNetwork);

    private void UpdateNetwork()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();
        bool online = NetworkInterface.GetIsNetworkAvailable() && up.Count > 0;
        bool wifi = up.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        ((TextBlock)_network.Content).Text = !online ? Icons.WifiOff : wifi ? Icons.Wifi : Icons.Ethernet;
        _network.ToolTip = !online ? Loc.T("menubar.offline") : wifi ? "Wi-Fi" : Loc.T("menubar.ethernet");
    }

    private void UpdateWeather()
    {
        bool show = SettingsStore.Current.Widgets.Weather;
        _weather.Visibility = show && WeatherService.Shared.Current != null ? Visibility.Visible : Visibility.Collapsed;
        if (show) WeatherService.Shared.RefreshIfNeeded(SettingsStore.Current.Widgets.City);
        if (WeatherService.Shared.Current is not { } report) return;
        var children = ((StackPanel)_weather.Content).Children;
        ((TextBlock)children[0]).Text = report.Symbol;
        ((TextBlock)children[1]).Text = $"{(int)Math.Round(report.Temperature)}°";
        _weather.ToolTip = Loc.F("weather.tooltip", report.City, report.Description);
    }

    // ------------------------------------------------------------------ éléments

    private static Button Item(UIElement content, string tooltip, Action? click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("RoundButton"),
            Background = Brushes.Transparent,
            Foreground = Paint.White,
            Height = MenuBarShape.ItemHeight,
            Padding = new Thickness(MenuBarShape.ItemPadding, 0, MenuBarShape.ItemPadding, 0),
            Margin = new Thickness(Space.Px, 0, Space.Px, 0),
            Content = content,
            ToolTip = tooltip,
            Focusable = false,
        };
        if (click != null) button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = FontStack.Icons,
        FontSize = IconSize.M,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel LabeledGlyph(string glyph, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Glyph(glyph));
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = TypeRamp.Footnote,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.S2, 0, 0, 0),
        });
        return panel;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }

    private static void OpenMenu(ContextMenu menu, FrameworkElement anchor)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void Open(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Barre de menus : ouverture de {uri}", ex);
        }
    }

    /// <summary>Centre de notifications et calendrier de Windows (comme un clic sur l'heure de la barre des tâches).</summary>
    private static void OpenNotificationCenter()
    {
        const byte VK_LWIN = 0x5B, VK_N = 0x4E;
        const uint KEYUP = 0x0002;
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_N, 0, 0, UIntPtr.Zero);
        keybd_event(VK_N, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYUP, UIntPtr.Zero);
    }

    private static string WindowTitle(IntPtr hWnd)
    {
        var text = new StringBuilder(256);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassName(IntPtr hWnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }

    // ------------------------------------------------------------------ interop

    private const uint GA_ROOTOWNER = 3;
    private const int SW_MAXIMIZE = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
}
