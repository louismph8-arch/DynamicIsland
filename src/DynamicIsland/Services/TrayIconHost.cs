using System.Collections.ObjectModel;
using ManagedShell;
using ManagedShell.WindowsTray;

namespace DynamicIsland.Services;

/// <summary>
/// Zone de notification (icônes d'arrière-plan : NordVPN, Discord…) hébergée par l'app grâce à
/// ManagedShell : une fenêtre « Shell_TrayWnd » au premier plan reçoit les icônes des applis et
/// transmet le reste à Explorer. À la fermeture, ManagedShell demande aux applis de se réenregistrer
/// auprès d'Explorer (et le gardien le refait si l'app est tuée).
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private ShellManager? _shell;

    /// <summary>Icônes reçues (filtrer celles dont <c>IsHidden</c> est vrai).</summary>
    public ObservableCollection<NotifyIcon>? Icons { get; private set; }

    public bool Start()
    {
        try
        {
            var config = ShellManager.DefaultShellConfig; // struct : copie modifiable
            config.EnableTasksService = false;            // les fenêtres, on les suit déjà nous-mêmes
            config.AutoStartTasksService = false;
            config.EnableTrayService = true;
            config.AutoStartTrayService = true;
            _shell = new ShellManager(config);

            if (_shell.NotificationArea is not { IsFailed: false } area)
            {
                Log.Error("Zone de notification", new InvalidOperationException("Initialisation impossible"));
                return false;
            }
            Icons = area.TrayIcons;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Zone de notification : démarrage", ex);
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _shell?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Zone de notification : arrêt", ex);
        }
        _shell = null;
        Icons = null;
    }
}
