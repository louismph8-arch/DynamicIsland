using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;
using DockNative = DynamicIsland.Interop.DockNative;

namespace DynamicIsland.DesktopWidgets;

/// <summary>
/// Widgets du bureau (5E). Chaque widget est une fenêtre rangée juste au-dessus du bureau : sous toutes les applis,
/// mais au-dessus du bureau même quand Windows le fait passer devant (Win+D). Quand le bureau est au premier plan
/// (clic sur le bureau, Win+D), ils sont nets ; dès qu'une appli est active, ils s'estompent. Cachés en mode
/// Gaming (et les mesures s'arrêtent). Données : la musique de l'île, une météo et des mesures à eux, une horloge
/// relue toutes les 20 s.
/// </summary>
public static class DesktopWidgetsService
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private static readonly Dictionary<DesktopWidgetKind, DesktopWidgetWindow> Windows = [];
    private static WeatherService Weather => WeatherService.Shared;
    private static readonly SystemStatsService Stats = new();
    private static readonly DispatcherTimer Clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private static readonly DispatcherTimer StatsTimer = new() { Interval = Desk.StatsInterval };
    private static MediaService? _media;
    private static IntPtr _hook;
    private static DockNative.WinEventProc? _hookProc; // gardé en vie : Windows l'appelle
    private static bool? _faded;

    public static void Start(MediaService media)
    {
        _media = media;
        media.Changed += () => Refresh(DesktopWidgetKind.Music);
        Weather.Changed += () => Application.Current.Dispatcher.BeginInvoke(() => Refresh(DesktopWidgetKind.Weather));
        Clock.Tick += (_, _) =>
        {
            Refresh(DesktopWidgetKind.Clock);
            Refresh(DesktopWidgetKind.Calendar);
            if (Windows.ContainsKey(DesktopWidgetKind.Weather)) Weather.RefreshIfNeeded(SettingsStore.Current.Widgets.City);
            KeepAboveDesktop(); // filet : la place se perd parfois (Explorateur redémarré…)
        };
        StatsTimer.Tick += (_, _) =>
        {
            if (Windows.TryGetValue(DesktopWidgetKind.Stats, out var window)) ((StatsWidget)window.Widget).Show(Stats.Sample());
        };
        SettingsStore.Changed += Apply;
        GamingMode.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) DockNative.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        foreach (var window in Windows.Values.ToList()) Close(window);
        Windows.Clear();
        Clock.Stop();
        StatsTimer.Stop();
    }

    private static void Apply()
    {
        try
        {
            var settings = SettingsStore.Current.DesktopWidgets;
            bool on = settings.Enabled && !SettingsStore.IsReadOnly && _media != null && !ModuleGuard.IsDisabled(ModuleGuard.Widgets);
            foreach (var item in settings.Items)
            {
                bool wanted = on && item.Visible;
                bool exists = Windows.TryGetValue(item.Kind, out var window);
                if (wanted && !exists) window = Create(item);
                else if (!wanted && exists) { Close(window!); Windows.Remove(item.Kind); continue; }
                if (window == null) continue;
                window.Widget.ApplyLook();
                // Remis en place depuis les paramètres (position effacée) ou changé ailleurs : on suit les réglages.
                var (left, top) = Position(item);
                if (Math.Abs(window.Left - left) > 1 || Math.Abs(window.Top - top) > 1 || Math.Abs(window.Scale - item.Scale) > 0.01)
                    window.Place(left, top, item.Scale);
            }

            bool gaming = GamingMode.IsActive;
            foreach (var window in Windows.Values) window.Visibility = gaming ? Visibility.Hidden : Visibility.Visible;
            bool running = Windows.Count > 0 && !gaming;
            if (running)
            {
                Clock.Start();
                if (Windows.ContainsKey(DesktopWidgetKind.Stats)) StartStats();
                else StatsTimer.Stop();
                HookForeground();
                _faded = null;
                UpdateFocus();
            }
            else
            {
                Clock.Stop();
                StatsTimer.Stop();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Widgets du bureau", ex);
        }
    }

    private static DesktopWidgetWindow Create(DesktopWidgetPlacement item)
    {
        DesktopWidget widget = item.Kind switch
        {
            DesktopWidgetKind.Weather => new WeatherWidget(Weather),
            DesktopWidgetKind.Music => new MusicWidget(_media!),
            DesktopWidgetKind.Calendar => new CalendarWidget(),
            DesktopWidgetKind.Stats => new StatsWidget(),
            _ => new ClockWidget(),
        };
        var window = new DesktopWidgetWindow(widget);
        var (left, top) = Position(item);
        window.Place(left, top, item.Scale);
        window.Placed += Save;
        window.ContextMenu = Menu(item.Kind);
        window.Show();
        Windows[item.Kind] = window;
        if (item.Kind == DesktopWidgetKind.Weather) Weather.RefreshIfNeeded(SettingsStore.Current.Widgets.City);
        return window;
    }

    private static void Close(DesktopWidgetWindow window)
    {
        window.Widget.Detach();
        window.Close();
    }

    private static void StartStats()
    {
        if (StatsTimer.IsEnabled) return;
        Stats.Reset();
        Stats.Sample(); // référence pour les débits et le processeur
        StatsTimer.Start();
    }

    private static void Refresh(DesktopWidgetKind kind)
    {
        if (Windows.TryGetValue(kind, out var window)) window.Widget.Refresh();
    }

    /// <summary>Fin d'un glisser / d'un redimensionnement : place enregistrée (en DIP).</summary>
    private static void Save(DesktopWidgetWindow window)
    {
        double left = window.Left, top = window.Top, scale = Math.Round(window.Scale, 2);
        SettingsStore.Update(s =>
        {
            var item = s.DesktopWidgets[window.Kind];
            item.Left = Math.Round(left);
            item.Top = Math.Round(top);
            item.Scale = scale;
        });
        KeepAboveDesktop();
    }

    /// <summary>Place enregistrée (si elle tombe sur un écran), sinon place par défaut sur l'écran principal.</summary>
    private static (double Left, double Top) Position(DesktopWidgetPlacement item)
    {
        var (_, work, scale) = ScreenInfo.Primary();
        if (item.Left is double left && item.Top is double top && OnSomeScreen(left, top, scale)) return (left, top);

        double x0 = work.Left / scale + Desk.Margin, y0 = work.Top / scale + Desk.Margin + IslandShape.Compact.Height;
        double right = work.Right / scale - Desk.Margin, bottom = work.Bottom / scale - Desk.Margin;
        double Width(Size size) => size.Width * item.Scale;
        double Height(Size size) => size.Height * item.Scale;
        return item.Kind switch
        {
            DesktopWidgetKind.Clock => (x0, y0),
            DesktopWidgetKind.Weather => (x0, y0 + Height(Desk.Clock) + Desk.Grid),
            DesktopWidgetKind.Stats => (x0, bottom - Height(Desk.Stats)),
            DesktopWidgetKind.Calendar => (right - Width(Desk.Calendar), y0),
            _ => (right - Width(Desk.Music), y0 + Height(Desk.Calendar) + Desk.Grid), // Music
        };
    }

    private static bool OnSomeScreen(double left, double top, double scale) =>
        System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains((int)((left + Desk.Grid) * scale), (int)((top + Desk.Grid) * scale)));

    private static ContextMenu Menu(DesktopWidgetKind kind)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.T("widget.menu.hide"), () => SettingsStore.Update(s => s.DesktopWidgets[kind].Visible = false)));
        menu.Items.Add(new Separator());
        var locked = new MenuItem { Header = Loc.T("settings.verrouiller_widgets"), IsCheckable = true };
        menu.Opened += (_, _) => locked.IsChecked = SettingsStore.Current.DesktopWidgets.Locked;
        locked.Click += (_, _) => SettingsStore.Update(s => s.DesktopWidgets.Locked = locked.IsChecked);
        menu.Items.Add(locked);
        menu.Items.Add(Entry(Loc.T("settings.remettre_place"), ResetPlaces));
        menu.Items.Add(Entry(Loc.T("widget.menu.settings"), () => SettingsWindow.Open(SettingsPage.Widgets)));
        return menu;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }

    /// <summary>Tous les widgets reviennent à leur place et à leur taille par défaut.</summary>
    public static void ResetPlaces() => SettingsStore.Update(s =>
    {
        foreach (var item in s.DesktopWidgets.Items)
        {
            item.Left = item.Top = null;
            item.Scale = 1;
        }
    });

    // ------------------------------------------------------------------ net / estompé, place au-dessus du bureau

    private static void HookForeground()
    {
        if (_hook != IntPtr.Zero) return;
        _hookProc = (_, _, _, _, _, _, _) => UpdateFocus();
        _hook = DockNative.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, DockNative.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Bureau au premier plan → widgets nets ; une appli → estompés. Puis place au-dessus du bureau.</summary>
    private static void UpdateFocus()
    {
        if (Windows.Count == 0) return;
        bool faded = !IsDesktop(GetForegroundWindow());
        if (faded != _faded)
        {
            _faded = faded;
            foreach (var window in Windows.Values) window.SetFaded(faded);
        }
        KeepAboveDesktop();
    }

    private static bool IsDesktop(IntPtr window)
    {
        if (window == IntPtr.Zero) return true;
        var name = new StringBuilder(32);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    /// <summary>
    /// Range chaque widget juste au-dessus de la fenêtre du bureau (celle qui contient les icônes) : sous les applis,
    /// mais visible quand Windows fait passer le bureau devant (Win+D).
    /// </summary>
    private static void KeepAboveDesktop()
    {
        var desktop = DesktopWindow();
        if (desktop == IntPtr.Zero) return;
        // Déjà en place (les widgets sont les fenêtres juste au-dessus du bureau) : on ne touche à rien.
        var ours = Windows.Values.Select(w => w.Handle).ToHashSet();
        var chain = new HashSet<IntPtr>();
        for (var h = DockNative.GetWindow(desktop, GW_HWNDPREV); h != IntPtr.Zero && ours.Contains(h); h = DockNative.GetWindow(h, GW_HWNDPREV))
            chain.Add(h);
        if (chain.Count == ours.Count) return;
        foreach (var window in Windows.Values)
        {
            if (window.Handle == IntPtr.Zero) continue;
            var above = DockNative.GetWindow(desktop, GW_HWNDPREV);
            if (above == window.Handle) continue; // déjà juste au-dessus
            // Ne jamais s'insérer après une fenêtre « toujours au premier plan » (on le deviendrait).
            bool topmost = above != IntPtr.Zero && (GetWindowLong(above, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            SetWindowPos(window.Handle, above == IntPtr.Zero || topmost ? IntPtr.Zero : above, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static IntPtr DesktopWindow()
    {
        var progman = DockNative.FindWindow("Progman", null);
        if (progman != IntPtr.Zero && DockNative.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
        IntPtr found = IntPtr.Zero;
        DockNative.EnumWindows((window, _) =>
        {
            if (DockNative.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private const uint GW_HWNDPREV = 3;
    private const int WS_EX_TOPMOST = 0x00000008;

    /// <summary>Mode --snapshot : chaque widget rendu dans un PNG (données réelles : météo, mesures, musique).</summary>
    internal static async Task RenderSnapshotsAsync(string folder, MediaService media)
    {
        Weather.RefreshIfNeeded(SettingsStore.Current.Widgets.City);
        Stats.Sample();
        await Task.Delay(2500); // météo (réseau) + 2e mesure
        var stats = Stats.Sample();
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>())
        {
            DesktopWidget widget = kind switch
            {
                DesktopWidgetKind.Weather => new WeatherWidget(Weather),
                DesktopWidgetKind.Music => new MusicWidget(media),
                DesktopWidgetKind.Calendar => new CalendarWidget(),
                DesktopWidgetKind.Stats => new StatsWidget(),
                _ => new ClockWidget(),
            };
            if (widget is StatsWidget statsWidget) statsWidget.Show(stats);
            widget.Refresh();
            var window = new DesktopWidgetWindow(widget) { Left = -30000, Top = 0 };
            window.Place(-30000, 0, 1);
            window.Show();
            await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, $"10-widget-{kind.ToString().ToLowerInvariant()}.png"));
            widget.Detach();
            window.Close();
        }
    }
}
