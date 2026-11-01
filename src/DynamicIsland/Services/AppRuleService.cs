using System.Runtime.InteropServices;
using System.Text;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Priorité 2, point 13 : règles par application (« Chrome = se cache, Spotify = toujours visible ») pour l'île et le dock, et
/// la fenêtre qui se trouve juste SOUS l'île (mode Intelligent, vidéo plein écran même quand une autre fenêtre a le focus).
/// Lecture seule, quelques appels Win32 légers, seulement quand l'île ou le dock le demandent.
/// </summary>
public static class AppRuleService
{
    private const uint GW_HWNDNEXT = 2;
    private const uint GA_ROOTOWNER = 3;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    public static bool HasRules => SettingsStore.Current.AppRules.Count > 0;

    /// <summary>Règle de l'appli de cette fenêtre (Default s'il n'y en a pas).</summary>
    public static AppBehavior ForWindow(IntPtr window, bool dock)
    {
        if (window == IntPtr.Zero || !HasRules) return AppBehavior.Default;
        var rule = AppRule.For(SettingsStore.Current.AppRules, GamingMode.ProcessName(window));
        return rule == null ? AppBehavior.Default : dock ? rule.Dock : rule.Island;
    }

    /// <summary>Règle de l'appli au premier plan.</summary>
    public static AppBehavior ForForeground(bool dock) => ForWindow(ForegroundRoot(), dock);

    /// <summary>Deux règles qui s'appliquent en même temps : « toujours visible » l'emporte, puis « se cache ».</summary>
    public static AppBehavior Combine(AppBehavior a, AppBehavior b) =>
        a == AppBehavior.Show || b == AppBehavior.Show ? AppBehavior.Show
        : a == AppBehavior.Hide || b == AppBehavior.Hide ? AppBehavior.Hide
        : AppBehavior.Default;

    /// <summary>Appli « toujours visible » pour l'île ou le dock : jamais prise pour un jeu (le mode Gaming les cacherait).</summary>
    public static bool AlwaysShown(string? process) =>
        process != null && AppRule.For(SettingsStore.Current.AppRules, process) is { } rule
        && (rule.Island == AppBehavior.Show || rule.Dock == AppBehavior.Show);

    public static IntPtr ForegroundRoot()
    {
        var foreground = GetForegroundWindow();
        return foreground == IntPtr.Zero ? IntPtr.Zero : GetAncestor(foreground, GA_ROOTOWNER);
    }

    /// <summary>
    /// Première fenêtre d'une autre appli SOUS <paramref name="self"/> au point donné (pixels de l'écran), en suivant l'ordre
    /// d'empilement : ni cachée, ni réduite, ni masquée par DWM, ni transparente aux clics. Le bureau = aucune (IntPtr.Zero).
    /// <paramref name="coversMonitor"/> : elle couvre tout son écran sans être agrandie = plein écran (vidéo, jeu).
    /// </summary>
    public static IntPtr WindowUnder(IntPtr self, int x, int y, out bool coversMonitor)
    {
        coversMonitor = false;
        for (var window = DockNative.GetWindow(self, GW_HWNDNEXT); window != IntPtr.Zero; window = DockNative.GetWindow(window, GW_HWNDNEXT))
        {
            if (!DockNative.IsWindowVisible(window) || DockNative.IsIconic(window) || DockNative.IsCloaked(window)) continue;
            if ((GetWindowLong(window, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0 || ScreenInfo.IsOverlay(window)) continue; // calques (overlays NVIDIA, Discord…)
            if (!GetWindowRect(window, out var rect) || x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) continue;
            var className = new StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW") return IntPtr.Zero; // le bureau
            if (!ScreenInfo.IsOtherAppWindow(window)) continue; // Opaline elle-même, barre des tâches
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!IsZoomed(window) && GetMonitorInfo(MonitorFromWindow(window, 2), ref info))
            {
                var screen = info.rcMonitor;
                coversMonitor = rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
            }
            return window;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
}
