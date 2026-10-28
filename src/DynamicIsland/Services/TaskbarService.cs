using System.Diagnostics;
using System.Globalization;
using System.IO;
using static DynamicIsland.Interop.DockNative;

namespace DynamicIsland.Services;

/// <summary>
/// Masque / restaure les barres des tâches Windows (écran principal et autres écrans).
///
/// Sécurité (la barre doit TOUJOURS revenir) :
/// 1. avant de la masquer, on écrit son état d'origine dans %APPDATA%\DynamicIsland\taskbar-state.txt ;
/// 2. à la fermeture normale (et en cas d'exception), <see cref="Restore"/> la remet et efface le fichier ;
/// 3. un processus gardien (<see cref="ShellGuard"/>) attend la fin de l'app — même tuée ou plantée — et restaure ;
/// 4. au lancement suivant, <see cref="RecoverIfNeeded"/> restaure si le fichier traîne encore.
/// </summary>
public static class TaskbarService
{
    public static bool IsHidden { get; private set; }

    public static void Hide()
    {
        var tray = MainTray();
        if (tray == IntPtr.Zero) return;

        if (!IsHidden)
        {
            // L'état d'origine est sauvé AVANT toute modification (et jamais écrasé s'il existe déjà).
            int original = GetState(tray);
            if (!File.Exists(AppPaths.TaskbarState))
            {
                AppPaths.EnsureDataFolder();
                File.WriteAllText(AppPaths.TaskbarState, original.ToString(CultureInfo.InvariantCulture));
            }
            // Masquage automatique : libère l'espace qu'elle réserve pour les fenêtres maximisées.
            SetState(tray, original | ABS_AUTOHIDE);
            IsHidden = true;
        }
        foreach (var window in TrayWindows()) ShowWindow(window, SW_HIDE);
    }

    /// <summary>Explorer peut refaire apparaître une barre (touche Windows, redémarrage…) : on la recache.</summary>
    public static void EnforceHidden()
    {
        if (!IsHidden) return;
        foreach (var window in TrayWindows())
            if (IsWindowVisible(window)) ShowWindow(window, SW_HIDE);
    }

    public static void Restore()
    {
        IsHidden = false;
        var tray = MainTray();
        if (tray == IntPtr.Zero) return; // Explorer absent : le fichier d'état reste pour plus tard

        foreach (var window in TrayWindows()) ShowWindow(window, SW_SHOWNA);
        try
        {
            if (File.Exists(AppPaths.TaskbarState))
            {
                if (int.TryParse(File.ReadAllText(AppPaths.TaskbarState), NumberStyles.Integer, CultureInfo.InvariantCulture, out int original))
                    SetState(tray, original);
                File.Delete(AppPaths.TaskbarState);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Barre des tâches : restauration", ex);
        }
    }

    /// <summary>Filet de sécurité au démarrage : restaure si une session précédente n'a pas pu le faire.</summary>
    public static void RecoverIfNeeded()
    {
        if (File.Exists(AppPaths.TaskbarState))
        {
            Restore();
            return;
        }
        foreach (var window in TrayWindows())
            if (!IsWindowVisible(window)) ShowWindow(window, SW_SHOWNA);
    }

    /// <summary>
    /// La vraie barre d'Explorer. Pendant qu'on héberge la zone de notification, une deuxième fenêtre
    /// « Shell_TrayWnd » (la nôtre, via ManagedShell) existe : on l'ignore.
    /// </summary>
    public static IntPtr MainTray()
    {
        uint ownProcess = (uint)Environment.ProcessId;
        var window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != ownProcess) return window;
        }
        return IntPtr.Zero;
    }

    /// <summary>Barre principale + barres des autres écrans (si « afficher sur tous les écrans » est activé).</summary>
    private static List<IntPtr> TrayWindows()
    {
        var windows = new List<IntPtr>();
        var main = MainTray();
        if (main != IntPtr.Zero) windows.Add(main);
        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            windows.Add(secondary);
        return windows;
    }

    private static int GetState(IntPtr tray)
    {
        var data = NewAppBarData(tray);
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    private static void SetState(IntPtr tray, int state)
    {
        var data = NewAppBarData(tray);
        data.lParam = state;
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }
}

/// <summary>
/// Processus gardien : « DynamicIsland.exe --shell-guard &lt;pid&gt; » attend la fin du processus principal
/// (fermeture, plantage ou arrêt forcé) puis remet la barre des tâches et libère l'espace réservé au dock.
/// </summary>
public static class ShellGuard
{
    public const string Argument = "--shell-guard";
    private static Process? _guard;

    public static void Start()
    {
        if (_guard is { HasExited: false } || Environment.ProcessPath is not { } exe) return;
        try
        {
            _guard = Process.Start(new ProcessStartInfo(exe, $"{Argument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Gardien : démarrage", ex);
        }
    }

    /// <summary>Exécuté dans le processus gardien.</summary>
    public static void Run(int processId)
    {
        try
        {
            using var watched = Process.GetProcessById(processId);
            watched.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Déjà terminé.
        }
        RecoverAll();
        AskAppsToReAddTrayIcons();
    }

    /// <summary>Remet le bureau dans son état normal si une session précédente ne l'a pas fait.</summary>
    public static void RecoverAll()
    {
        try { TaskbarService.RecoverIfNeeded(); }
        catch (Exception ex) { Log.Error("Récupération : barre des tâches", ex); }
        try { AppBar.RemoveStale(); }
        catch (Exception ex) { Log.Error("Récupération : espace réservé au dock et à la barre de menus", ex); }
        try { SystemMinimizeAnimation.RecoverIfNeeded(); }
        catch (Exception ex) { Log.Error("Récupération : animation de réduction", ex); }
    }

    /// <summary>
    /// Demande à toutes les applis de réenregistrer leurs icônes de notification (message « TaskbarCreated »).
    /// Le gardien l'envoie après la mort de l'app : sans ça, Explorer garderait des icônes périmées.
    /// </summary>
    public static void AskAppsToReAddTrayIcons()
    {
        try
        {
            uint message = RegisterWindowMessage("TaskbarCreated");
            PostMessage(new IntPtr(0xFFFF), message, IntPtr.Zero, IntPtr.Zero); // HWND_BROADCAST
        }
        catch (Exception ex)
        {
            Log.Error("Récupération : icônes de notification", ex);
        }
    }
}
