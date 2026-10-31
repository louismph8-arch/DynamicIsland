using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Mises à jour Windows en attente (6B), par l'API officielle « Windows Update Agent » (COM Microsoft.Update.Session),
/// SANS droits administrateur et en LECTURE seule : on ne télécharge, n'installe ni ne redémarre jamais rien.
/// Vérification automatique = résultats du dernier scan fait par Windows lui-même (recherche hors ligne, instantanée),
/// 15 s après le démarrage, puis toutes les 30 min, à la sortie de veille, au déverrouillage et dès que Windows Update écrit
/// dans sa clé du registre (installation finie, redémarrage demandé) — correctif urgent 5 : l'icône restait jusqu'à 3 h après
/// l'installation. « Vérifier maintenant » (menu) = vraie recherche en ligne. Rien pendant le mode Gaming : la vérification
/// attend la fin de la partie.
/// </summary>
public static class WindowsUpdateService
{
    /// <summary>Mise à jour en attente : titre et taille de téléchargement maximale (octets, 0 si inconnue).</summary>
    public sealed record Update(string Title, double Size);

    private const string Criteria = "IsInstalled=0 and IsHidden=0 and BrowseOnly=0";
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(15);
    private static readonly DispatcherTimer Timer = new() { Interval = FirstCheck };
    /// <summary>Plusieurs écritures de Windows Update d'affilée → une seule vérification, 10 s après la dernière.</summary>
    private static readonly DispatcherTimer Soon = new() { Interval = TimeSpan.FromSeconds(10) };
    private const string AutoUpdateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
    private static RegistryWatcher? _registry;
    private static bool _hooked;
    private static bool _started;
    private static bool _waitingForGame;

    public static IReadOnlyList<Update> Pending { get; private set; } = [];
    public static bool RestartRequired { get; private set; }
    public static bool Checking { get; private set; }
    public static DateTime? LastCheck { get; private set; }
    public static string? Error { get; private set; }

    /// <summary>Nouvel état (thread UI).</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Timer.Tick += (_, _) =>
        {
            Timer.Interval = Period;
            _ = CheckAsync(online: false);
        };
        if (!_hooked)
        {
            _hooked = true; // une seule fois, même si le module est arrêté puis relancé
            Soon.Tick += (_, _) =>
            {
                Soon.Stop();
                if (Timer.IsEnabled) _ = CheckAsync(online: false);
            };
            SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == PowerModes.Resume) Guarded.OnUi(CheckSoon, "Windows Update : sortie de veille");
            };
            SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason == SessionSwitchReason.SessionUnlock) Guarded.OnUi(CheckSoon, "Windows Update : déverrouillage");
            };
        }
        GamingMode.Changed += () =>
        {
            if (!GamingMode.IsActive && _waitingForGame) _ = CheckAsync(online: false);
        };
        SettingsStore.Changed += Apply;
        Apply();
    }

    /// <summary>Vérification hors ligne dans 10 s (regroupe les signaux rapprochés).</summary>
    private static void CheckSoon()
    {
        if (!Timer.IsEnabled) return;
        Soon.Stop();
        Soon.Start();
    }

    public static void Stop()
    {
        Timer.Stop();
        Soon.Stop();
        _registry?.Dispose();
        _registry = null;
        SettingsStore.Changed -= Apply;
        _started = false;
    }

    /// <summary>Vérifie seulement si la cellule est affichée.</summary>
    private static void Apply()
    {
        bool wanted = !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Status)
                      && SettingsStore.Current.Dock.ShowsStatus(StatusItem.Updates);
        if (wanted == Timer.IsEnabled) return;
        if (wanted)
        {
            Timer.Interval = LastCheck == null ? FirstCheck : Period;
            Timer.Start();
            // Windows Update écrit ici quand une installation se termine ou qu'un redémarrage est demandé (lecture seule, sans droits).
            _registry ??= RegistryWatcher.TryStart(Registry.LocalMachine, AutoUpdateKey, () => Guarded.OnUi(CheckSoon, "Windows Update : registre"));
        }
        else
        {
            Timer.Stop();
            Soon.Stop();
            _registry?.Dispose();
            _registry = null;
        }
    }

    /// <param name="online">Vraie recherche sur les serveurs de Microsoft (demandée par l'utilisateur), sinon le dernier scan de Windows.</param>
    public static async Task CheckAsync(bool online)
    {
        if (Checking) return;
        if (GamingMode.IsActive && !online)
        {
            _waitingForGame = true;
            return;
        }
        _waitingForGame = false;
        Checking = true;
        Changed?.Invoke();
        try
        {
            var (pending, restart) = await Task.Run(() => Read(online));
            Pending = pending;
            RestartRequired = restart;
            Error = null;
            LastCheck = DateTime.Now;
            Log.Info($"Windows Update ({(online ? "en ligne" : "dernier scan")}) : {pending.Count} en attente, redémarrage requis {restart}");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error("Windows Update : lecture", ex);
        }
        finally
        {
            Checking = false;
            Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
        }
    }

    /// <summary>Lecture (thread de travail, COM). Aucune action sur les mises à jour : recherche seulement.</summary>
    internal static (List<Update> Pending, bool RestartRequired) Read(bool online)
    {
        var list = new List<Update>();
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;
        session.ClientApplicationID = AppInfo.Name;
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online = online;
        dynamic result = searcher.Search(Criteria);
        dynamic updates = result.Updates;
        int count = updates.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic update = updates[i];
            double size = 0;
            try
            {
                size = Convert.ToDouble(update.MaxDownloadSize);
            }
            catch (Exception)
            {
                // Taille inconnue.
            }
            list.Add(new Update((string)update.Title, size));
        }
        return (list, IsRestartRequired());
    }

    private static bool IsRestartRequired()
    {
        try
        {
            dynamic info = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo", throwOnError: true)!)!;
            if ((bool)info.RebootRequired) return true;
        }
        catch (Exception)
        {
            // Repli : la clé que Windows Update crée quand un redémarrage est en attente.
        }
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        return key != null;
    }

    /// <summary>Paramètres → Windows Update (c'est Windows qui installe, quand l'utilisateur le décide).</summary>
    public static void OpenSettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true })?.Dispose();

    /// <summary>« 120 Mo », « 1,4 Go » (taille d'une mise à jour).</summary>
    public static string FormatSize(double bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 * 1024 * 1024 => Loc.F("size.mb", Math.Max(1, Math.Round(bytes / (1024 * 1024)))),
        _ => Loc.F("size.gb", Math.Round(bytes / (1024 * 1024 * 1024), 1)),
    };
}
