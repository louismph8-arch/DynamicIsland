using System.Runtime.InteropServices;
using System.Text;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>Géométrie des écrans (en pixels physiques) et détection du plein écran.</summary>
internal static class ScreenInfo
{
    /// <summary>
    /// Un écran : nom pour Windows (« \\.\DISPLAY2 », parfois « DISPLAY23 » : à ne pas afficher), limites et zone de
    /// travail en pixels, échelle (1 = 96 dpi), nom affiché d'après sa place (« Écran de gauche »).
    /// </summary>
    public readonly record struct Monitor(IntPtr Handle, string Device, RECT Bounds, RECT WorkArea, double Scale, bool IsPrimary, string Name = "")
    {
        public int WidthPx => Bounds.Right - Bounds.Left;
        public int HeightPx => Bounds.Bottom - Bounds.Top;

        /// <summary>Nom affiché : « Écran du haut — 1920 × 1080 ».</summary>
        public string Label => $"{(Name.Length > 0 ? Name : Loc.T(IsPrimary ? "screen.primary" : "screen.other"))} — {WidthPx} × {HeightPx}";
    }

    public static IntPtr PrimaryMonitor => MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);

    /// <summary>
    /// Tous les écrans, de gauche à droite, nommés d'après leur place par rapport à l'écran principal : « Écran principal »,
    /// « Écran de gauche », « Écran du haut »… (numérotés s'il y en a deux du même côté).
    /// </summary>
    public static List<Monitor> All()
    {
        var monitors = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            if (Describe(handle) is { } monitor) monitors.Add(monitor);
            return true;
        }, IntPtr.Zero);
        monitors = monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();

        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        var names = monitors.Select(m =>
        {
            if (m.IsPrimary || primary.Handle == IntPtr.Zero) return Loc.T(m.IsPrimary ? "screen.primary" : "screen.other");
            double dx = (m.Bounds.Left + m.Bounds.Right - primary.Bounds.Left - primary.Bounds.Right) / 2.0;
            double dy = (m.Bounds.Top + m.Bounds.Bottom - primary.Bounds.Top - primary.Bounds.Bottom) / 2.0;
            return Loc.T(Math.Abs(dy) > Math.Abs(dx) ? (dy < 0 ? "screen.top" : "screen.bottom") : (dx < 0 ? "screen.left" : "screen.right"));
        }).ToList();
        for (int i = 0; i < monitors.Count; i++)
        {
            string name = names[i];
            int same = names.Count(n => n == name);
            int rank = names.Take(i).Count(n => n == name) + 1;
            monitors[i] = monitors[i] with { Name = same > 1 ? $"{name} {rank}" : name };
        }
        return monitors;
    }

    public static Monitor? Describe(IntPtr handle)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (handle == IntPtr.Zero || !GetMonitorInfoEx(handle, ref info)) return null;
        double scale = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return new Monitor(handle, info.szDevice ?? "", info.rcMonitor, info.rcWork, scale, (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    /// <summary>
    /// Écran choisi dans les réglages (son nom pour Windows) ; null, ou un écran débranché, = l'écran principal.
    /// </summary>
    public static Monitor Find(string? device)
    {
        var all = All();
        if (!string.IsNullOrEmpty(device))
            foreach (var monitor in all)
                if (string.Equals(monitor.Device, device, StringComparison.OrdinalIgnoreCase)) return monitor;
        foreach (var monitor in all)
            if (monitor.IsPrimary) return monitor;
        return PrimaryInfo();
    }

    /// <summary>Écran sous ce point (pixels), ou le plus proche.</summary>
    public static Monitor At(POINT point) => Describe(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST)) ?? PrimaryInfo();

    /// <summary>Nom à enregistrer pour un écran : null pour le principal (l'île ou le dock suivent alors l'écran principal).</summary>
    public static string? SettingName(Monitor monitor) => monitor.IsPrimary ? null : monitor.Device;

    private static Monitor PrimaryInfo()
    {
        if (Describe(PrimaryMonitor) is { } primary) return primary;
        var (bounds, work, scale) = Primary();
        return new Monitor(PrimaryMonitor, "", bounds, work, scale, true);
    }

    /// <summary>Limites complètes et zone de travail de l'écran principal, plus son facteur d'échelle (1 = 96 dpi).</summary>
    public static (RECT Bounds, RECT WorkArea, double Scale) Primary()
    {
        var monitor = PrimaryMonitor;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return (info.rcMonitor, info.rcWork, scale);
    }

    /// <summary>
    /// Vrai quand la fenêtre au premier plan (vidéo, jeu…) couvre tout l'écran donné (par défaut le principal).
    /// Les fenêtres de l'app elle-même sont ignorées.
    /// </summary>
    public static bool IsForegroundFullscreen(IntPtr monitor = default)
    {
        var covered = CoveredMonitor(GetForegroundWindow());
        return covered != IntPtr.Zero && covered == (monitor == IntPtr.Zero ? PrimaryMonitor : monitor);
    }

    /// <summary>Écran entièrement couvert par cette fenêtre (plein écran exclusif ou sans bordure), sinon IntPtr.Zero.</summary>
    public static IntPtr CoveredMonitor(IntPtr window)
    {
        if (!IsOtherAppWindow(window) || DockNative.IsIconic(window) || IsZoomed(window)) return IntPtr.Zero;

        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONULL);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || !GetWindowRect(window, out var rect)) return IntPtr.Zero;
        var screen = info.rcMonitor;
        bool covers = rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        return covers ? monitor : IntPtr.Zero;
    }

    /// <summary>
    /// Écrans dont la zone de travail est entièrement cachée par une fenêtre d'une autre appli (agrandie, plein écran ou
    /// simplement assez grande), visible, ni réduite, ni sur un autre bureau virtuel (« cloaked »). Les fenêtres
    /// transparentes (calques, incrustations) ne comptent pas : un écran qui se voit n'est jamais pris pour caché (7A).
    /// </summary>
    public static HashSet<IntPtr> HiddenMonitors()
    {
        const int WS_EX_TRANSPARENT = 0x00000020;
        var hidden = new HashSet<IntPtr>();
        DockNative.EnumWindows((window, _) =>
        {
            if (!IsOtherAppWindow(window) || DockNative.IsIconic(window) || DockNative.IsCloaked(window)) return true;
            if ((GetWindowLong(window, GWL_EXSTYLE) & (DockNative.WS_EX_LAYERED | WS_EX_TRANSPARENT)) != 0) return true;
            var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONULL);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || hidden.Contains(monitor) || !GetMonitorInfo(monitor, ref info) || !GetWindowRect(window, out var rect))
                return true;
            var work = info.rcWork;
            if (rect.Left <= work.Left && rect.Top <= work.Top && rect.Right >= work.Right && rect.Bottom >= work.Bottom) hidden.Add(monitor);
            return true;
        }, IntPtr.Zero);
        return hidden;
    }

    /// <summary>Fenêtre visible d'une autre appli (ni l'app elle-même, ni le bureau, ni la barre des tâches).</summary>
    public static bool IsOtherAppWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !DockNative.IsWindowVisible(window)) return false;
        DockNative.GetWindowThreadProcessId(window, out uint processId);
        if (processId == Environment.ProcessId) return false;

        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
}
