using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Garde-fou des modes développeur (--snapshot, --self-test, --glass-bench, --perf-bench…) : leurs fenêtres sont rendues HORS
/// écran et ne doivent jamais apparaître sur le bureau de l'utilisateur. Bug « double dock » (1.34.0) : le dock d'une mesure
/// --glass-bench, replacé par <c>PositionWindow</c> au premier changement de réglage, s'affichait en vrai en bas de l'écran
/// principal (sans les icônes d'arrière-plan : pas de zone de notification en mode développeur) à côté du vrai dock.
/// Toute fenêtre WPF de ce processus qui touche un écran est donc masquée par DWM (DWMWA_CLOAK : WPF continue de la dessiner,
/// les rendus RenderTargetBitmap ne changent pas) et notée une fois dans le journal de développement.
/// </summary>
internal static class DeveloperWindowGuard
{
    private static DispatcherTimer? _timer;
    private static readonly HashSet<IntPtr> Reported = [];

    public static void Start()
    {
        if (_timer != null) return;
        // Dès qu'une fenêtre est affichée (avant sa première image visible si elle a été créée sur un écran)…
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window) Check(window);
        }));
        // … puis régulièrement : une fenêtre peut être déplacée plus tard (réglage changé, écran, DPI).
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            foreach (Window window in Application.Current.Windows) Check(window);
        };
        _timer.Start();
    }

    private static void Check(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !window.IsVisible || IsCloaked(hwnd)) return;
            if (MonitorFromWindow(hwnd, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero) return; // hors de tout écran : normal
            SetCloaked(hwnd, true);
            if (Reported.Add(hwnd))
            {
                GetWindowRect(hwnd, out var r);
                Log.Info($"Mode développeur : fenêtre « {window.Title} » ({window.GetType().Name}) sur un écran en "
                         + $"({r.Left}, {r.Top})–({r.Right}, {r.Bottom}) → masquée");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Mode développeur : garde-fou des fenêtres", ex);
        }
    }

    /// <summary>Pour l'auto-test : vrai si la fenêtre est sur un écran ET visible (= ce que le garde-fou empêche).</summary>
    internal static bool ShownOnScreen(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && window.IsVisible && !IsCloaked(hwnd) && MonitorFromWindow(hwnd, 0) != IntPtr.Zero;
    }
}
