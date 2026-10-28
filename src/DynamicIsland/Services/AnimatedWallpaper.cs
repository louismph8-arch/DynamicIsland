using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;
using DockNative = DynamicIsland.Interop.DockNative;

namespace DynamicIsland.Services;

/// <summary>
/// Option « Fond animé » : aurore animée derrière les icônes du bureau, sur chaque écran. Comme Wallpaper Engine,
/// une fenêtre enfant par écran est posée dans le « WorkerW » du bureau, sous les icônes (Windows 11 24H2 : enfant
/// de Progman ; avant : fenêtre à part juste derrière celle des icônes). Si Wallpaper Engine tourne, notre fond
/// passe devant le sien. Tout se dessine sur un thread à part (« Fond animé ») : un ralentissement du fond ne gêne
/// ni l'île ni le dock. En pause (dernière image figée, rien n'est redessiné) en jeu, quand une appli est en plein
/// écran, sur batterie, quand la session est verrouillée et après quelques minutes sans clavier ni souris ; et écran
/// par écran (7A) quand une fenêtre agrandie le cache entièrement. Rien à remettre dans Windows : les fenêtres
/// disparaissent avec l'app.
/// </summary>
public static class AnimatedWallpaper
{
    private static readonly DispatcherTimer Watch = new() { Interval = Wallpapers.PauseCheck };
    private static Thread? _thread;
    private static Dispatcher? _render;      // dispatcher du thread du fond
    private static readonly List<Host> Hosts = []; // uniquement sur le thread du fond
    private static DispatcherTimer? _frame;  // idem
    private static double _time;
    private static bool _locked, _paused;
    private static int _watchTicks;
    private static HashSet<IntPtr> _hidden = []; // écrans cachés par une fenêtre (lu par le thread du fond)

    public static bool IsWallpaperEngineRunning => IsRunning("wallpaper32") || IsRunning("wallpaper64");

    private static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    public static void Start()
    {
        Watch.Tick += (_, _) => OnWatch();
        SettingsStore.Changed += Apply;
        GamingMode.Changed += UpdatePause;
        DynamicWallpaperService.MoodChanged += PushMood;
        SystemEvents.DisplaySettingsChanged += (_, _) => Rebuild();
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
            {
                _locked = e.Reason == SessionSwitchReason.SessionLock;
                UpdatePause();
            }
        };
        SystemEvents.PowerModeChanged += (_, _) => UpdatePause();
        Apply();
    }

    /// <summary>Fermeture de l'app : on retire les fenêtres du bureau.</summary>
    public static void Stop()
    {
        Watch.Stop();
        var render = _render;
        if (render == null) return;
        try
        {
            // Délai maximal : un fond bloqué ne doit jamais bloquer l'île et le dock.
            render.Invoke(() =>
            {
                _frame?.Stop();
                foreach (var host in Hosts) host.Dispose();
                Hosts.Clear();
            }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
            render.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Log.Error("Fond animé : arrêt", ex);
        }
        _render = null;
        _thread = null;
    }

    private static void Apply()
    {
        var settings = SettingsStore.Current.Wallpaper;
        bool on = settings.Animated && !SettingsStore.IsReadOnly && AnimatedAuroraEffect.IsAvailable && !ModuleGuard.IsDisabled(ModuleGuard.Wallpapers);
        if (!on)
        {
            Stop();
            return;
        }
        try
        {
            bool created = EnsureThread();
            bool allScreens = settings.AnimatedAllScreens;
            Post(() =>
            {
                if (created || Hosts.Count == 0 || Hosts[0].AllScreens != allScreens) CreateHosts(allScreens);
            });
            PushMood();
            Watch.Start();
            UpdatePause();
        }
        catch (Exception ex)
        {
            Log.Error("Fond animé", ex);
        }
    }

    /// <summary>Écrans changés (ajout, résolution, échelle) : on refait les fenêtres.</summary>
    private static void Rebuild()
    {
        if (_render == null) return;
        bool allScreens = SettingsStore.Current.Wallpaper.AnimatedAllScreens;
        Post(() => CreateHosts(allScreens));
    }

    private static void OnWatch()
    {
        UpdatePause();
        if (++_watchTicks % 60 == 0) PushMood(); // les couleurs du moment avancent doucement
        // L'Explorateur a redémarré (le WorkerW et nos fenêtres ont disparu) : on les recrée.
        Post(() =>
        {
            if (Hosts.Count == 0 || Hosts.Any(h => !DockNative.IsWindow(h.Handle)))
                CreateHosts(SettingsStore.Current.Wallpaper.AnimatedAllScreens);
        });
    }

    private static void PushMood()
    {
        if (_render == null) return;
        var mood = SettingsStore.Current.Wallpaper.Dynamic ? DynamicWallpaperService.CurrentMood() : Wallpapers.Night;
        Post(() =>
        {
            foreach (var host in Hosts) host.Effect.SetMood(mood);
        });
    }

    private static void UpdatePause()
    {
        if (_render == null) return;
        bool paused = GamingMode.IsActive || ScreenInfo.CoveredMonitor(GetForegroundWindow()) != IntPtr.Zero || OnBattery() || _locked
                      || IdleFor() >= Wallpapers.IdlePause;
        var hidden = paused ? _hidden : ScreenInfo.HiddenMonitors();
        if (paused == _paused && hidden.SetEquals(_hidden)) return;
        _paused = paused;
        _hidden = hidden;
        Post(() =>
        {
            // Seulement les écrans qu'on voit ; plus aucun : la minuterie s'arrête (rien n'est redessiné).
            foreach (var host in Hosts) host.Visible = !hidden.Contains(host.Monitor);
            if (paused || Hosts.All(h => !h.Visible)) _frame?.Stop();
            else _frame?.Start();
        });
    }

    /// <summary>Temps depuis la dernière action au clavier ou à la souris (sur tout le PC).</summary>
    private static TimeSpan IdleFor()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    /// <summary>Sur batterie (seulement si le PC en a une : un PC fixe n'est jamais « sur batterie »).</summary>
    private static bool OnBattery() =>
        GetSystemPowerStatus(out var status) && status.ACLineStatus == 0 && status.BatteryFlag is not (128 or 255);

    // ------------------------------------------------------------------ thread du fond

    private static bool EnsureThread()
    {
        if (_render != null) return false;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _render = Dispatcher.CurrentDispatcher;
            _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = Wallpapers.FrameInterval };
            var clock = Stopwatch.StartNew();
            var last = TimeSpan.Zero;
            _frame.Tick += (_, _) =>
            {
                var now = clock.Elapsed;
                _time += Math.Min((now - last).TotalSeconds, 0.1) * Wallpapers.AnimationSpeed; // pas de saut après une pause
                last = now;
                foreach (var host in Hosts)
                    if (host.Visible) host.Advance(_time);
            };
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "Fond animé" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _paused = false;
        _frame!.Dispatcher.BeginInvoke(() => _frame.Start());
        return true;
    }

    private static void Post(Action action) => _render?.BeginInvoke(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Fond animé", ex);
        }
    });

    private static void CreateHosts(bool allScreens)
    {
        foreach (var host in Hosts) host.Dispose();
        Hosts.Clear();
        var workerW = FindDesktopLayer();
        if (workerW == IntPtr.Zero)
        {
            Log.Info("Fond animé : couche du bureau (WorkerW) introuvable");
            return;
        }
        GetWindowRect(workerW, out var origin);
        var screens = System.Windows.Forms.Screen.AllScreens.Where(s => allScreens || s.Primary);
        var mood = SettingsStore.Current.Wallpaper.Dynamic ? DynamicWallpaperService.CurrentMood() : Wallpapers.Night;
        foreach (var screen in screens)
        {
            var b = screen.Bounds; // pixels physiques (l'app gère le DPI par écran)
            var monitor = MonitorFromPoint(new POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 }, MONITOR_DEFAULTTONEAREST);
            Hosts.Add(new Host(workerW, b.X - origin.Left, b.Y - origin.Top, b.Width, b.Height, allScreens, mood, _time)
            {
                Monitor = monitor,
                Visible = !_hidden.Contains(monitor),
            });
        }
    }

    /// <summary>
    /// Fenêtre « WorkerW » où se dessine le fond, sous les icônes. Le message 0x052C demande à Progman de la créer
    /// (sans effet si elle existe déjà ; Wallpaper Engine envoie le même).
    /// </summary>
    private static IntPtr FindDesktopLayer()
    {
        var progman = DockNative.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);

        // Windows 11 24H2 et suivants : le WorkerW est un enfant de Progman, sous la vue des icônes.
        var child = DockNative.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (child != IntPtr.Zero) return child;

        // Avant : WorkerW de premier niveau placé juste après la fenêtre qui contient les icônes.
        IntPtr found = IntPtr.Zero;
        DockNative.EnumWindows((window, _) =>
        {
            if (DockNative.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            found = DockNative.FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Une fenêtre de fond (un écran) : rectangle noir + shader, dans le WorkerW.</summary>
    private sealed class Host : IDisposable
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        private readonly HwndSource _source;
        private readonly double _aspect;

        public Host(IntPtr parent, int x, int y, int width, int height, bool allScreens, Mood mood, double time)
        {
            AllScreens = allScreens;
            _aspect = (double)width / Math.Max(1, height);
            _source = new HwndSource(new HwndSourceParameters("Dynamic Island — Fond animé")
            {
                ParentWindow = parent,
                WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                ExtendedWindowStyle = WS_EX_NOACTIVATE,
                PositionX = x,
                PositionY = y,
                Width = width,
                Height = height,
            });
            Effect.SetMood(mood);
            Effect.SetFrame(_aspect, time);
            _source.RootVisual = new Rectangle { Fill = Brushes.Black, Effect = Effect };
            if (_source.CompositionTarget != null) _source.CompositionTarget.BackgroundColor = Colors.Black;
            // Devant les autres fonds du WorkerW (Wallpaper Engine) ; le WorkerW reste sous les icônes.
            SetWindowPos(_source.Handle, IntPtr.Zero, x, y, width, height, SWP_NOACTIVATE);
        }

        public AnimatedAuroraEffect Effect { get; } = new();
        public bool AllScreens { get; }
        public IntPtr Handle => _source.Handle;
        /// <summary>Écran de cette fenêtre (pour savoir s'il est caché par une fenêtre agrandie).</summary>
        public IntPtr Monitor { get; init; }
        /// <summary>Faux : écran caché, l'image reste figée.</summary>
        public bool Visible { get; set; } = true;

        public void Advance(double time) => Effect.SetFrame(_aspect, time);

        public void Dispose() => _source.Dispose();
    }

    // ------------------------------------------------------------------ interop

    private const uint SMTO_NORMAL = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
