using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace DynamicIsland.Services;

/// <summary>
/// Verrouiller / veille / redémarrer / éteindre (barre de menus et bouton d'alimentation du dock, correctif 20). Verrouiller part
/// tout de suite ; les trois autres demandent TOUJOURS une confirmation. `shutdown` sans /f : Windows laisse les applis protéger
/// un travail non enregistré.
/// </summary>
public static class PowerActions
{
    public static void Lock() => LockWorkStation();

    public static void Sleep()
    {
        if (!Confirm(Loc.T("power.sleep.confirm"), apps: false)) return;
        SetSuspendState(false, false, false);
    }

    public static void Restart()
    {
        if (Confirm(Loc.T("menubar.restart.confirm"), apps: true)) Shutdown("/r /t 0");
    }

    public static void ShutDown()
    {
        if (Confirm(Loc.T("menubar.shutdown.confirm"), apps: true)) Shutdown("/s /t 0");
    }

    private static bool Confirm(string question, bool apps) =>
        MessageBox.Show(apps ? question + " " + Loc.T("menubar.apps_will_close") : question, AppInfo.Name,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void Shutdown(string arguments)
    {
        Log.Info($"Alimentation : shutdown {arguments} demandé depuis Opaline");
        Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
}
