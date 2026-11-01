using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// « Mettre à jour et redémarrer / arrêter » (correctif urgent 21), seulement au clic et après confirmation :
/// <list type="bullet">
/// <item>A — mises à jour déjà préparées par Windows (redémarrage requis) : <c>InitiateShutdown</c> avec
/// SHUTDOWN_INSTALL_UPDATES, exactement le « Mettre à jour et redémarrer » du menu Démarrer. Sans droits administrateur.</item>
/// <item>B — mises à jour pas encore installées : Opaline relancée avec la fenêtre administrateur de Windows
/// (<see cref="Argument"/>) télécharge et installe par l'API Windows Update Agent, écrit son avancement dans
/// <see cref="ProgressFile"/> (lu par l'app pour l'annonce de l'île), puis redémarre / éteint de la même façon.</item>
/// </list>
/// Refus de la fenêtre administrateur = rien ne se passe. Jamais pendant une partie : c'est l'utilisateur qui clique.
/// </summary>
public static class WindowsUpdateInstall
{
    public const string Argument = "--install-updates";
    private static readonly string ProgressFile = Path.Combine(Path.GetTempPath(), "opaline-maj-windows.txt");
    private static DispatcherTimer? _watch;

    public static bool Running => _watch != null;

    /// <param name="restart">Vrai = redémarrer, faux = arrêter le PC.</param>
    public static async void Start(bool restart)
    {
        if (Running) return;
        int pending = WindowsUpdateService.Pending.Count;
        bool prepared = WindowsUpdateService.RestartRequired;
        if (pending == 0 && !prepared)
        {
            MessageBox.Show(Loc.T("updates.win.none"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string question = pending > 0
            ? Loc.F(restart ? "updates.win.confirm.install.restart" : "updates.win.confirm.install.shutdown", pending)
            : Loc.T(restart ? "updates.win.confirm.restart" : "updates.win.confirm.shutdown");
        if (MessageBox.Show(question + " " + Loc.T("menubar.apps_will_close"), AppInfo.Name, MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        if (pending == 0)
        {
            // A : Windows a déjà tout préparé.
            Log.Info($"Windows Update : mise à jour et {(restart ? "redémarrage" : "arrêt")} (déjà préparées)");
            if (!ShutdownWithUpdates(restart))
                MessageBox.Show(Loc.T("updates.win.failed"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // B : installation par Opaline en administrateur.
        try { File.Delete(ProgressFile); } catch (Exception) { /* pas grave */ }
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watch.Tick += (_, _) => Guarded.Run(ShowProgress, "Windows Update : avancement");
        _watch.Start();
        Notify(Loc.T("updates.win.progress.start"));
        var exe = Environment.ProcessPath ?? "";
        bool? done = await Remote.FirewallRule.RunElevatedAsync(exe, $"{Argument} {(restart ? "restart" : "shutdown")}");
        _watch?.Stop();
        _watch = null;
        if (done == null) Notify(Loc.T("updates.win.refused"));
        else if (done == false) Notify(Loc.T("updates.win.failed"));
        _ = WindowsUpdateService.CheckAsync(online: false);
    }

    private static string? _lastProgress;

    private static void ShowProgress()
    {
        string text;
        try { text = File.ReadAllText(ProgressFile).Trim(); }
        catch (Exception) { return; }
        if (text.Length == 0 || text == _lastProgress) return;
        _lastProgress = text;
        var parts = text.Split('|');
        string message = parts[0] switch
        {
            "download" when parts.Length > 1 => Loc.F("updates.win.progress.download", parts[1]),
            "install" when parts.Length > 1 => Loc.F("updates.win.progress.install", parts[1]),
            "restart" => Loc.T("updates.win.progress.restart"),
            _ => "",
        };
        if (message.Length > 0) Notify(message);
    }

    private static void Notify(string text) => (Application.Current as App)?.Notify(Icons.WindowsUpdate, text, WindowsUpdateService.OpenSettings);

    // ------------------------------------------------------------------ côté administrateur

    /// <summary>
    /// Opaline relancée en administrateur : recherche, téléchargement, installation, puis redémarrage / arrêt avec
    /// installation des mises à jour en attente. Codes : 0 = fait, 1 = échec, 3 = arguments.
    /// </summary>
    public static int Run(string[] args)
    {
        int index = Array.IndexOf(args, Argument);
        if (index < 0 || index + 1 >= args.Length || args[index + 1] is not ("restart" or "shutdown")) return 3;
        bool restart = args[index + 1] == "restart";
        try
        {
            dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;
            session.ClientApplicationID = AppInfo.Name;
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic found = searcher.Search("IsInstalled=0 and IsHidden=0 and BrowseOnly=0 and Type='Software'");
            dynamic updates = found.Updates;
            dynamic collection = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)!)!;
            int count = updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic update = updates[i];
                if (!(bool)update.EulaAccepted) continue; // un contrat à accepter : laissé à l'app Paramètres de Windows
                collection.Add(update);
            }
            int total = collection.Count;
            for (int i = 0; i < total; i++)
            {
                dynamic one = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)!)!;
                one.Add(collection[i]);
                if (!(bool)collection[i].IsDownloaded)
                {
                    Write($"download|{i + 1}/{total}");
                    dynamic downloader = session.CreateUpdateDownloader();
                    downloader.Updates = one;
                    downloader.Download();
                }
            }
            int installed = 0;
            for (int i = 0; i < total; i++)
            {
                if (!(bool)collection[i].IsDownloaded) continue;
                Write($"install|{i + 1}/{total}");
                dynamic one = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", throwOnError: true)!)!;
                one.Add(collection[i]);
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = one;
                dynamic result = installer.Install();
                if ((int)result.ResultCode is 2 or 3) installed++; // orcSucceeded / orcSucceededWithErrors
            }
            Log.Info($"Windows Update (administrateur) : {installed}/{total} installées, puis {(restart ? "redémarrage" : "arrêt")}");
            Write("restart");
            return ShutdownWithUpdates(restart) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error("Windows Update (administrateur) : installation", ex);
            return 1;
        }
    }

    private static void Write(string line)
    {
        try { File.WriteAllText(ProgressFile, line); }
        catch (Exception) { /* l'avancement n'est qu'indicatif */ }
    }

    // ------------------------------------------------------------------ redémarrage avec installation

    private const uint ShutdownRestart = 0x4, ShutdownPowerOff = 0x8, ShutdownInstallUpdates = 0x40;
    private const uint ReasonPlannedUpgrade = 0x80000000 | 0x00020000 | 0x00000003; // PLANNED | MAJOR_OPERATINGSYSTEM | MINOR_UPGRADE

    /// <summary>« Mettre à jour et redémarrer / arrêter » de Windows : les applis peuvent protéger un travail non enregistré.</summary>
    internal static bool ShutdownWithUpdates(bool restart)
    {
        EnableShutdownPrivilege();
        uint code = InitiateShutdownW(null, null, 0, ShutdownInstallUpdates | (restart ? ShutdownRestart : ShutdownPowerOff), ReasonPlannedUpgrade);
        if (code == 0) return true;
        Log.Error($"Windows Update : InitiateShutdown a échoué ({code})", new Win32Exception((int)code));
        return false;
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out var token)) return; // ADJUST_PRIVILEGES | QUERY
        try
        {
            if (!LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid)) return;
            var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 }; // SE_PRIVILEGE_ENABLED
            AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint InitiateShutdownW(string? machine, string? message, uint grace, uint flags, uint reason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
