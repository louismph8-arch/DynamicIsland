using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Mode Gaming automatique : un jeu en plein écran (exclusif ou fenêtré sans bordure) au premier plan →
/// l'île se réduit au minimum et le dock se cache. Tout revient quand le jeu se ferme, se réduit, ou
/// qu'une autre fenêtre passe devant sur son écran (cliquer sur Discord sur un autre écran ne compte pas).
/// Les applis de la liste d'exceptions (navigateurs, lecteurs vidéo…) gardent le comportement « vidéo ».
/// Une vérification par seconde, pas de hook.
/// </summary>
public static class GamingMode
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1) };
    /// <summary>Écrans plein écran de Windows lui-même (verrouillage, menu Démarrer, recherche…) : jamais un jeu.</summary>
    private static readonly HashSet<string> SystemProcesses =
    [
        "lockapp", "logonui", "searchhost", "searchapp", "startmenuexperiencehost", "shellexperiencehost",
        "textinputhost", "applicationframehost", "systemsettings", "consent",
    ];
    private static readonly Dictionary<uint, string?> ProcessNames = [];
    private static IntPtr _game;
    private static int _pending; // vérifications de suite qui contredisent l'état actuel
    private static bool _started;

    public static bool IsActive { get; private set; }
    /// <summary>Processus du jeu détecté (sans « .exe »).</summary>
    public static string? Game { get; private set; }
    /// <summary>Dernière appli vue en plein écran (jeu ou exception), proposée dans les paramètres.</summary>
    public static string? LastFullscreenProcess { get; private set; }

    /// <summary>Levé (thread UI) quand le mode Gaming commence ou se termine.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Mode Gaming actif ET jeu sur cet écran (IntPtr.Zero = l'écran principal) ; jeu introuvable = oui, par prudence.
    /// Le dock placé sur un autre écran que le jeu reste ainsi utilisable pendant la partie.
    /// </summary>
    public static bool IsOnMonitor(IntPtr monitor)
    {
        if (!IsActive) return false;
        if (_game == IntPtr.Zero || !DockNative.IsWindow(_game)) return true;
        var gameMonitor = MonitorFromWindow(_game, MONITOR_DEFAULTTONEAREST);
        return gameMonitor == (monitor == IntPtr.Zero ? ScreenInfo.PrimaryMonitor : monitor);
    }

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Timer.Tick += (_, _) => Check();
        Timer.Start();
        SettingsStore.Changed += () =>
        {
            if (!SettingsStore.Current.Gaming.Auto && IsActive) SetActive(false);
        };
    }

    private static void Check()
    {
        try
        {
            bool wanted = SettingsStore.Current.Gaming.Auto && !ModuleGuard.IsDisabled(ModuleGuard.Gaming) && DetectGame();
            if (wanted == IsActive)
            {
                _pending = 0;
                return;
            }
            if (++_pending < 2) return; // 2 s de suite : pas de clignotement pendant un écran de chargement
            SetActive(wanted);
        }
        catch (Exception ex)
        {
            Log.Error("Mode Gaming : détection", ex);
        }
    }

    private static void SetActive(bool active)
    {
        _pending = 0;
        IsActive = active;
        if (active)
        {
            Log.Info($"Mode Gaming : {Game}");
        }
        else
        {
            _game = IntPtr.Zero;
            Game = null;
        }

        foreach (var handler in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Log.Error("Mode Gaming : application", ex);
            }
        }
    }

    private static bool DetectGame()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return IsActive; // changement de fenêtre en cours

        bool exclusive = SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;
        if (ScreenInfo.CoveredMonitor(foreground) != IntPtr.Zero || (exclusive && ScreenInfo.IsOtherAppWindow(foreground)))
        {
            var name = ProcessName(foreground);
            if (name == null) return false;
            LastFullscreenProcess = name;
            if (SystemProcesses.Contains(name) || SettingsStore.Current.Gaming.Exceptions.Contains(name)) return false;
            _game = foreground;
            Game = name;
            return true;
        }

        // Le jeu couvre toujours son écran et la fenêtre active est sur un autre écran : on reste en jeu.
        if (_game != IntPtr.Zero && DockNative.IsWindow(_game) && ScreenInfo.CoveredMonitor(_game) is var gameMonitor
            && gameMonitor != IntPtr.Zero && MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST) != gameMonitor)
            return true;
        return false;
    }

    private static string? ProcessName(IntPtr window)
    {
        DockNative.GetWindowThreadProcessId(window, out uint processId);
        if (ProcessNames.TryGetValue(processId, out var cached)) return cached;
        if (ProcessNames.Count > 100) ProcessNames.Clear();
        string? name = null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName.ToLowerInvariant();
        }
        catch (Exception)
        {
            // Processus déjà terminé.
        }
        ProcessNames[processId] = name;
        return name;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
