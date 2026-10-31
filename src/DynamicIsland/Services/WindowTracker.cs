using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>Fenêtre d'application visible (celles qu'on trouverait dans Alt+Tab).</summary>
/// <param name="ExePath">Chemin de l'exe du processus (null si inaccessible).</param>
/// <param name="AppId">AppUserModelID pour les applis du Store (fenêtres ApplicationFrameWindow).</param>
public sealed record AppWindow(IntPtr Handle, string Title, string? ExePath, string? AppId, bool IsMinimized);

/// <summary>
/// Suit les fenêtres ouvertes grâce aux événements système (ouverture, fermeture, activation,
/// réduction) avec un rafraîchissement de secours toutes les 2 s. Tout se passe sur le thread UI.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _fallback = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<IntPtr> _hooks = [];
    private readonly WinEventProc _callback; // gardé en vie tant que les hooks existent
    private Dictionary<uint, string?> _processPaths = [];

    public WindowTracker()
    {
        _callback = OnWinEvent;
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_CREATE + 1, EVENT_OBJECT_HIDE); // destruction, affichage, masquage
        Hook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE);

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
        _fallback.Tick += (_, _) => Refresh();
        Refresh();
        _fallback.Start();
    }

    /// <summary>Fenêtres dans l'ordre Z (la première est la plus au premier plan).</summary>
    public IReadOnlyList<AppWindow> Windows { get; private set; } = [];

    public IntPtr Foreground { get; private set; }

    /// <summary>La liste des fenêtres (ou leurs titres / états) a changé.</summary>
    public event Action? Changed;

    /// <summary>Une autre fenêtre est passée au premier plan.</summary>
    public event Action? ForegroundChanged;

    /// <summary>Une fenêtre d'appli vient d'être réduite (bouton, Win+↓, clic sur le dock…).</summary>
    public event Action<IntPtr>? MinimizeStarted;

    public void Refresh()
    {
        var windows = new List<AppWindow>();
        var paths = new Dictionary<uint, string?>();
        uint ownProcess = (uint)Environment.ProcessId;

        EnumWindows((hWnd, _) =>
        {
            if (TryDescribe(hWnd, ownProcess, paths, _processPaths, out var window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);

        _processPaths = paths;
        bool changed = windows.Count != Windows.Count || !windows.ToHashSet().SetEquals(Windows);
        Windows = windows;
        Foreground = GetForegroundWindow();
        if (changed) Changed?.Invoke();
    }

    /// <summary>Les fenêtres d'applis lues une seule fois (télécommande, 6D-1c) : sans crochets ni minuterie.</summary>
    public static IReadOnlyList<AppWindow> Snapshot()
    {
        var windows = new List<AppWindow>();
        var paths = new Dictionary<uint, string?>();
        uint ownProcess = (uint)Environment.ProcessId;
        EnumWindows((hWnd, _) =>
        {
            if (TryDescribe(hWnd, ownProcess, paths, paths, out var window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool TryDescribe(IntPtr hWnd, uint ownProcess, Dictionary<uint, string?> paths, Dictionary<uint, string?> known, out AppWindow window)
    {
        window = null!;
        if (!IsWindowVisible(hWnd)) return false;

        int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        bool forcedInTaskbar = (exStyle & WS_EX_APPWINDOW) != 0;
        if (!forcedInTaskbar &&
            (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero || (exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0))
            return false;
        if (IsCloaked(hWnd)) return false; // autre bureau virtuel, appli Store suspendue…

        var title = GetTitle(hWnd);
        if (title.Length == 0) return false;

        // Nos propres fenêtres normales (les paramètres) sont gardées : sans barre des tâches, c'est le dock
        // qui permet de les retrouver. L'île, le dock et les aperçus sont des fenêtres outil, déjà exclues.
        GetWindowThreadProcessId(hWnd, out uint processId);

        var className = GetClass(hWnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
            return false;

        if (!paths.TryGetValue(processId, out var path))
        {
            path = known.TryGetValue(processId, out var cached) ? cached : GetProcessPath(processId);
            paths[processId] = path;
        }
        var appId = className == "ApplicationFrameWindow" ? ShellNative.GetWindowAppId(hWnd) : null;
        window = new AppWindow(hWnd, title, path, appId, IsIconic(hWnd));
        return true;
    }

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
    }

    /// <summary>Rappel de Windows : une exception ici tuerait l'app (elle ne traverse pas Windows) → toujours attrapée.</summary>
    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            HandleWinEvent(eventType, hWnd, idObject, idChild);
        }
        catch (Exception ex)
        {
            Guarded.Report(ex, "Fenêtres : événement de Windows");
        }
    }

    private void HandleWinEvent(uint eventType, IntPtr hWnd, int idObject, int idChild)
    {
        if (idObject != 0 || idChild != 0 || hWnd == IntPtr.Zero) return; // seulement les fenêtres elles-mêmes

        if (eventType == EVENT_SYSTEM_FOREGROUND)
        {
            Foreground = hWnd;
            ForegroundChanged?.Invoke();
        }
        else if (eventType == EVENT_SYSTEM_MINIMIZESTART && Windows.Any(w => w.Handle == hWnd))
        {
            WindowActions.Remember(hWnd); // agrandie ou non, place exacte : rendue telle quelle au clic sur le dock
            MinimizeStarted?.Invoke(hWnd);
        }
        else if (eventType == EVENT_OBJECT_NAMECHANGE && !Windows.Any(w => w.Handle == hWnd))
        {
            return; // changement de titre d'une fenêtre qui ne nous intéresse pas (très fréquent)
        }

        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        _fallback.Stop();
        _debounce.Stop();
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }
}

/// <summary>Actions sur les fenêtres des autres applis.</summary>
public static class WindowActions
{
    /// <summary>État d'une fenêtre au moment de sa réduction : agrandie ou non, et sa place « normale » (zone de travail).</summary>
    internal readonly record struct Placement(bool Maximized, RECT Normal, DateTime At);

    private static readonly Dictionary<IntPtr, Placement> Remembered = [];

    /// <summary>
    /// Correctif urgent 3 : note l'état d'une fenêtre juste avant que le dock la réduise, ou dès que Windows signale une réduction
    /// (bouton de l'appli, Win+D, Win+↓…). Le clic sur le dock la rend EXACTEMENT ainsi (agrandie, taille, place, écran).
    /// </summary>
    public static void Remember(IntPtr hWnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hWnd, ref placement)) return;
        bool maximized = IsIconic(hWnd) ? (placement.flags & WPF_RESTORETOMAXIMIZED) != 0 : IsZoomed(hWnd);
        var now = DateTime.UtcNow;
        foreach (var gone in Remembered.Where(p => now - p.Value.At > TimeSpan.FromDays(1) || !IsWindow(p.Key)).Select(p => p.Key).ToList())
            Remembered.Remove(gone);
        Remembered[hWnd] = new Placement(maximized, placement.rcNormalPosition, now);
    }

    /// <summary>La fenêtre de cette liste réduite le plus récemment (null si on n'en a noté aucune).</summary>
    public static AppWindow? LastMinimized(IEnumerable<AppWindow> windows) =>
        windows.Where(w => w.IsMinimized && Remembered.ContainsKey(w.Handle)).MaxBy(w => Remembered[w.Handle].At);

    /// <summary>
    /// Décision seule (vérifiée par l'auto-test) : faut-il remettre la place notée avant SW_RESTORE ? Oui si Windows (ou l'appli)
    /// a oublié qu'elle était agrandie, ou a changé sa place normale. Rien de noté = Windows sait faire.
    /// </summary>
    internal static bool NeedsPutBack(Placement? remembered, bool restoreToMaximized, RECT currentNormal) =>
        remembered is { } saved && (saved.Maximized != restoreToMaximized || saved.Normal.Left != currentNormal.Left
            || saved.Normal.Top != currentNormal.Top || saved.Normal.Right != currentNormal.Right || saved.Normal.Bottom != currentNormal.Bottom);

    public static void Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) Restore(hWnd);
        // Le clic sur le dock nous donne le droit de changer le premier plan ; sinon, plan B.
        if (!SetForegroundWindow(hWnd) || GetForegroundWindow() != hWnd) SwitchToThisWindow(hWnd, true);
    }

    private static void Restore(IntPtr hWnd)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hWnd, ref placement))
        {
            ShowWindowAsync(hWnd, SW_RESTORE);
            return;
        }
        Placement? saved = Remembered.TryGetValue(hWnd, out var p) ? p : null;
        if (NeedsPutBack(saved, (placement.flags & WPF_RESTORETOMAXIMIZED) != 0, placement.rcNormalPosition) && saved is { } before)
        {
            // Windows (ou l'appli) a oublié la place d'avant : on la remet, réduite, puis on la montre comme avant.
            placement.rcNormalPosition = before.Normal;
            placement.flags = before.Maximized ? placement.flags | WPF_RESTORETOMAXIMIZED : placement.flags & ~WPF_RESTORETOMAXIMIZED;
            placement.showCmd = SW_SHOWMINNOACTIVE;
            SetWindowPlacement(hWnd, ref placement);
        }
        ShowWindowAsync(hWnd, SW_RESTORE); // une fenêtre « agrandie avant » revient agrandie, sur son écran
        if (saved is { Maximized: true }) _ = CheckMaximizedAsync(hWnd);
    }

    /// <summary>Certaines applis (cadre maison) se rouvrent « normales » malgré tout : on vérifie un instant après.</summary>
    private static async Task CheckMaximizedAsync(IntPtr hWnd)
    {
        await Task.Delay(350);
        if (IsWindow(hWnd) && !IsIconic(hWnd) && !IsZoomed(hWnd)) ShowWindowAsync(hWnd, SW_SHOWMAXIMIZED);
    }

    public static void Minimize(IntPtr hWnd)
    {
        Remember(hWnd); // avant : agrandie ou non, telle qu'elle est à l'écran
        ShowWindowAsync(hWnd, SW_MINIMIZE);
    }

    public static void Close(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}
