using System.IO;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;
using static DynamicIsland.Interop.DockNative;

namespace DynamicIsland.Services;

/// <summary>
/// Icônes du bureau cachées (correctif 16), option « Cacher les icônes du bureau » du dock, désactivée par défaut. La vue des
/// icônes (SysListView32 dans SHELLDLL_DefView, sous Progman ou un WorkerW) est seulement masquée par ShowWindow : rien n'est
/// écrit dans le registre de Windows. Comme la barre des tâches, elles reviennent TOUJOURS :
/// 1. la trace (%APPDATA%\DynamicIsland\icones-bureau.txt) est écrite AVANT de les cacher ;
/// 2. option coupée, dock arrêté, module « status » arrêté, fermeture, plantage, fin de session : <see cref="Restore"/> ;
/// 3. le gardien (<see cref="ShellGuard.RecoverAll"/>) et le lancement suivant : <see cref="RecoverIfNeeded"/>.
/// Option active : vérification légère toutes les 2 s (Explorateur redémarré, ou qui les réaffiche → recachées) ; option
/// coupée : rien ne tourne. Un rendu --snapshot ne démarre jamais ce service et ne remet que ce que SON processus a caché.
/// </summary>
public static class DesktopIconsService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private static DispatcherTimer? _watch;
    private static IntPtr _view;
    private static bool _started;
    /// <summary>Cachées par CE processus (seul lui les remet en partant ; le gardien et le lancement suivant lisent la trace).</summary>
    private static bool _hiddenHere;

    /// <summary>Icônes à cacher maintenant : option active, dock en marche, ni mode sans échec ni module arrêté.</summary>
    private static bool Wanted
    {
        get
        {
            var dock = SettingsStore.Current.Dock;
            return _started && dock.Enabled && dock.HideDesktopIcons && !App.SafeMode
                   && !ModuleGuard.IsDisabled(ModuleGuard.Status) && !ModuleGuard.IsDisabled(ModuleGuard.Dock);
        }
    }

    public static void Start()
    {
        if (_started) return;
        _started = true;
        SettingsStore.Changed += Apply;
        Apply();
    }

    /// <summary>Arrêt du module « zone d'état » (ou de l'app) : les icônes reviennent.</summary>
    public static void Stop()
    {
        if (_started) SettingsStore.Changed -= Apply;
        _started = false;
        Apply();
    }

    /// <summary>Réglage changé, dock ou module arrêté : on cache, ou on remet tout de suite.</summary>
    public static void Apply()
    {
        if (Wanted)
        {
            Hide();
            if (_watch == null)
            {
                _watch = new DispatcherTimer { Interval = CheckInterval };
                _watch.Tick += (_, _) => Apply();
            }
            _watch.Start();
        }
        else
        {
            _watch?.Stop();
            Restore();
        }
    }

    /// <summary>Cache la vue des icônes si elle est visible (la trace d'abord ; si elle ne peut pas être écrite, rien n'est caché).</summary>
    private static void Hide()
    {
        if (_view == IntPtr.Zero || !IsWindow(_view)) _view = FindIconView(); // Explorateur redémarré : nouvelle fenêtre
        if (!ShouldHide(true, _view != IntPtr.Zero, _view != IntPtr.Zero && IsWindowVisible(_view))) return;
        try
        {
            if (!File.Exists(AppPaths.DesktopIconsState))
            {
                AppPaths.EnsureDataFolder();
                File.WriteAllText(AppPaths.DesktopIconsState, "visible"); // jamais écrasée : c'est l'état d'avant
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Icônes du bureau : trace impossible à écrire, elles restent affichées", ex);
            return;
        }
        ShowWindowAsync(_view, SW_HIDE); // asynchrone : un Explorateur figé ne bloque pas le dock
        if (!_hiddenHere) Log.Info("Icônes du bureau cachées");
        _hiddenHere = true;
    }

    /// <summary>Remet les icônes cachées par CE processus (option coupée, module arrêté, fermeture, plantage).</summary>
    public static void Restore()
    {
        if (!_hiddenHere) return;
        _hiddenHere = false;
        _watch?.Stop();
        RecoverIfNeeded();
        Log.Info("Icônes du bureau remises");
    }

    /// <summary>Au lancement, dans le gardien et pour --quit : une session a caché les icônes sans les remettre.</summary>
    public static void RecoverIfNeeded()
    {
        if (!File.Exists(AppPaths.DesktopIconsState)) return;
        var view = FindIconView();
        var (show, forget) = RestorePlan(true, view != IntPtr.Zero, view != IntPtr.Zero && IsWindowVisible(view), WindowsHidesIcons());
        if (show) ShowWindowAsync(view, SW_SHOWNA);
        if (forget) File.Delete(AppPaths.DesktopIconsState);
    }

    /// <summary>Cacher maintenant ? Seulement une vue trouvée et visible (calcul pur, vérifié par --self-test).</summary>
    internal static bool ShouldHide(bool wanted, bool found, bool visible) => wanted && found && visible;

    /// <summary>
    /// Remettre les icônes d'après la trace (calcul pur, vérifié par --self-test) : rien sans trace ; Explorateur absent = on
    /// garde la trace pour plus tard ; déjà revenues (Explorateur redémarré) ou « Afficher les icônes du bureau » décoché dans
    /// Windows entre-temps (son choix à lui) = trace oubliée sans rien montrer ; sinon on les remet.
    /// </summary>
    internal static (bool Show, bool Forget) RestorePlan(bool stateFile, bool found, bool visible, bool windowsHides) =>
        !stateFile || !found ? (false, false)
        : visible || windowsHides ? (false, true)
        : (true, true);

    /// <summary>« Afficher les icônes du bureau » décoché dans Windows (clic droit sur le bureau → Affichage) : LU seulement.</summary>
    private static bool WindowsHidesIcons()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return key?.GetValue("HideIcons") is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Vue des icônes du bureau (lecture seule) : la fenêtre et celle qui la porte (« Progman », « WorkerW »).</summary>
    internal static (IntPtr View, string Host) Locate()
    {
        // Windows 11 24H2 et suivants : toujours sous Progman ; avant, Explorateur peut la déplacer dans un WorkerW (Win+D, diaporama).
        var progman = FindWindow("Progman", null);
        var defView = progman == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        string host = "Progman";
        if (defView == IntPtr.Zero)
        {
            // Seulement les WorkerW de premier niveau : une boîte « Ouvrir / Enregistrer » a aussi un SHELLDLL_DefView, à ne jamais toucher.
            var worker = IntPtr.Zero;
            while (defView == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            host = "WorkerW";
        }
        var view = defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return (view, view == IntPtr.Zero ? "" : host);
    }

    private static IntPtr FindIconView() => Locate().View;
}
