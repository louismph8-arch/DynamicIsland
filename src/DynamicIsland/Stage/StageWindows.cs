using System.Runtime.InteropServices;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Lecture seule (Win32) de ce que Stage Manager doit savoir d'une fenêtre : styles, plein écran, droits administrateur.</summary>
internal static class StageWindows
{
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    private static readonly Dictionary<uint, bool> ElevatedCache = [];
    private static bool? _selfElevated;

    public static StageFlags Flags(AppWindow window)
    {
        GetWindowThreadProcessId(window.Handle, out uint pid);
        int style = GetWindowLong(window.Handle, GWL_STYLE);
        int exStyle = GetWindowLong(window.Handle, GWL_EXSTYLE);
        string? process = window.ExePath != null ? System.IO.Path.GetFileNameWithoutExtension(window.ExePath) : GamingMode.ProcessName(window.Handle);
        bool ignored = process != null && AppRule.For(SettingsStore.Current.AppRules, process) is { StageIgnore: true };
        bool fullscreen = !window.IsMinimized && ScreenInfo.CoveredMonitor(window.Handle) != IntPtr.Zero;
        bool game = GamingMode.IsActive && GamingMode.Game is { Length: > 0 } g && process != null
                    && string.Equals(AppRule.Clean(g), AppRule.Clean(process), StringComparison.OrdinalIgnoreCase);
        var shape = StageLayout.Shape(Shape(window.Handle, process, pid, style, exStyle));
        return new StageFlags(
            Own: shape == StageExclusion.Own,
            ToolWindow: (exStyle & WS_EX_TOOLWINDOW) != 0,
            Minimizable: (style & WS_MINIMIZEBOX) != 0,
            Topmost: (exStyle & WS_EX_TOPMOST) != 0,
            Fullscreen: fullscreen,
            Game: game,
            Elevated: shape != StageExclusion.Own && IsElevated(pid),
            Ignored: ignored,
            Popup: shape == StageExclusion.Popup,
            Invisible: shape == StageExclusion.Invisible);
    }

    /// <summary>Lecture (Win32) de ce que <see cref="StageLayout.Shape"/> juge : styles, propriétaire visible, place, écran, cloaked.</summary>
    private static StageWindowShape Shape(IntPtr hwnd, string? process, uint pid, int style, int exStyle)
    {
        bool minimized = IsIconic(hwnd);
        RECT bounds;
        if (minimized)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            bounds = GetWindowPlacement(hwnd, ref placement) ? placement.rcNormalPosition : default;
        }
        else bounds = GetVisibleBounds(hwnd);
        bool onScreen = minimized || MonitorFromWindow(hwnd, 0) != IntPtr.Zero; // MONITOR_DEFAULTTONULL : rien = hors de tout écran
        uint dpi = 0;
        try
        {
            dpi = GetDpiForWindow(hwnd);
        }
        catch (EntryPointNotFoundException)
        {
        }
        return new StageWindowShape(process, pid == (uint)Environment.ProcessId, GetClass(hwnd), style, exStyle,
            VisibleOwner(hwnd) != IntPtr.Zero, minimized, bounds, onScreen, IsCloaked(hwnd), dpi > 0 ? dpi / 96.0 : 1.0);
    }

    /// <summary>Premier propriétaire VISIBLE (un propriétaire caché, comme la fenêtre « application » des vieilles applis Delphi, ne compte pas).</summary>
    public static IntPtr VisibleOwner(IntPtr hwnd)
    {
        var owner = GetWindow(hwnd, GW_OWNER);
        for (int i = 0; owner != IntPtr.Zero && i < 16; i++)
        {
            if (IsWindowVisible(owner)) return owner;
            owner = GetWindow(owner, GW_OWNER);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// « Racine » d'une fenêtre au premier plan : on remonte ses propriétaires VISIBLES (dialogue → fenêtre de l'appli). Une fenêtre
    /// différente de sa racine est un dialogue / menu / fenêtre secondaire possédée : elle ne fait jamais bouger Stage Manager.
    /// </summary>
    public static IntPtr VisibleRoot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        var root = hwnd;
        for (int i = 0; i < 16; i++)
        {
            var owner = VisibleOwner(root);
            if (owner == IntPtr.Zero) break;
            root = owner;
        }
        return root;
    }

    public static bool Resizable(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_STYLE) & WS_THICKFRAME) != 0;

    /// <summary>Bouton « Agrandir » (sinon l'agrandissement natif n'est pas utilisé : SetWindowPos vers la scène).</summary>
    public static bool Maximizable(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_STYLE) & WS_MAXIMIZEBOX) != 0;

    public static string? ProcessName(AppWindow window) =>
        window.ExePath != null ? System.IO.Path.GetFileNameWithoutExtension(window.ExePath) : GamingMode.ProcessName(window.Handle);

    /// <summary>
    /// Processus lancé en administrateur alors qu'Opaline ne l'est pas : Windows (UIPI) refuse qu'on déplace ou réduise ses fenêtres.
    /// Jeton illisible = on le considère protégé (on n'y touche pas). Mis en cache par processus.
    /// </summary>
    public static bool IsElevated(uint pid)
    {
        _selfElevated ??= Elevation((uint)Environment.ProcessId) == true;
        if (_selfElevated == true) return false; // Opaline en administrateur : rien n'est interdit
        if (ElevatedCache.TryGetValue(pid, out bool cached)) return cached;
        bool elevated = Elevation(pid) != false;
        if (ElevatedCache.Count > 200) ElevatedCache.Clear();
        ElevatedCache[pid] = elevated;
        return elevated;
    }

    /// <summary>Vrai = administrateur, faux = normal, null = illisible.</summary>
    private static bool? Elevation(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static void ForgetProcesses() => ElevatedCache.Clear();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
