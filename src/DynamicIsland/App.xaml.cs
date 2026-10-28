using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private IslandWindow? _island;
    private DockWindow? _dock;
    private MenuBar.MenuBarWindow? _menuBar;
    private AppTrayIcon? _appTray;
    private readonly TrayIconHost _tray = new();
    private bool _trayReported;
    private bool _quitting;
    /// <summary>Annonces à montrer dès que l'île le permet (elle était ouverte, cachée en jeu…).</summary>
    private readonly List<(string Glyph, string Text, Action? OnClick, DateTime Until)> _pendingNotices = [];
    private DispatcherTimer? _noticeRetry;

    /// <summary>--snapshot --demo (7B) : contenu inventé (musique) pour les images publiées, rien du PC de l'utilisateur.</summary>
    public static bool SnapshotDemo { get; private set; }

    /// <summary>Mode sans échec (Maj maintenue au lancement, 6A) : réglages par défaut, sans modules optionnels, rien d'écrit.</summary>
    public static bool SafeMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Processus gardien : attend la fin de l'app principale puis remet la barre des tâches (voir ShellGuard).
        if (e.Args.Length >= 2 && e.Args[0] == ShellGuard.Argument && int.TryParse(e.Args[1], out int watchedProcess))
        {
            ShellGuard.Run(watchedProcess);
            Shutdown();
            return;
        }

        // Réveil à distance (7B) : Opaline relancée en administrateur règle la carte réseau puis se ferme (sans PowerShell).
        if (e.Args.Contains(Services.Remote.WakeOnLan.SetupArgument))
        {
            Shutdown(Services.Remote.WakeOnLan.RunSetup(e.Args));
            return;
        }

        // Installeur / désinstalleur : ferme proprement l'app qui tourne (la barre des tâches revient), puis répare
        // ce qui resterait (barre masquée, espace réservé, animation de réduction) avant de toucher aux fichiers.
        if (e.Args.Contains("--quit"))
        {
            QuitRunningInstance();
            ShellGuard.RecoverAll();
            Shutdown();
            return;
        }

        // Désinstalleur (après --quit) : remet le fond d'écran, la couleur d'accent, le thème et le curseur de Windows
        // d'avant l'app (réglages lus sans jamais être réécrits : il faut savoir quels fonds sont ceux des profils).
        if (e.Args.Contains("--restore-windows"))
        {
            SettingsStore.Load(readOnly: true);
            WindowsBackup.RestoreAll();
            Shutdown();
            return;
        }

        // Relance (langue changée, réglages précédents remis) : on attend que l'instance d'avant ait fini.
        if (ArgumentAfter(e.Args, "--restart-after") is { } previous && int.TryParse(previous, out int previousId)) WaitForExit(previousId);

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            // Un module qui plante est arrêté pour la session (et annoncé) ; le reste de l'app continue.
            if (!ModuleGuard.TryFail(args.Exception)) Log.Error("Exception UI non gérée", args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            var error = args.Exception.InnerExceptions.Count == 1 ? args.Exception.InnerExceptions[0] : args.Exception;
            Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!ModuleGuard.TryFail(error)) Log.Error("Tâche non observée", error);
            });
        };

        // --settings <fichier> : réglages de test pour le mode --snapshot (le vrai settings.json n'est pas touché).
        int testSettingsArg = Array.IndexOf(e.Args, "--settings");
        bool snapshotMode = Array.IndexOf(e.Args, "--snapshot") >= 0;
        if (snapshotMode) Log.UseDeveloperFile();
        SnapshotDemo = snapshotMode && e.Args.Contains("--demo");
        SafeMode = !snapshotMode && (e.Args.Contains("--safe-mode") || ShiftHeld());
        if (SafeMode) SettingsStore.LoadSafeDefaults();
        else SettingsStore.Load(snapshotMode && testSettingsArg >= 0 && testSettingsArg + 1 < e.Args.Length ? e.Args[testSettingsArg + 1] : null,
                                readOnly: snapshotMode);
        Loc.Initialize(SettingsStore.Current.Language);
        ProfileService.Initialize(); // premier lancement de la 3E : crée les profils (Perso = le dock actuel)
        // Le thème d'abord (avant que les fenêtres ne s'abonnent) : leurs ressources sont prêtes quand elles se mettent à jour.
        AppearanceService.Apply();
        SettingsStore.Changed += AppearanceService.Apply;

        // Mode développeur : rendu de l'île et du dock dans des PNG puis fermeture (voir *.Snapshot.cs).
        int snapshotArg = Array.IndexOf(e.Args, "--snapshot");
        if (snapshotArg >= 0 && snapshotArg + 1 < e.Args.Length)
        {
            base.OnStartup(e);
            var folder = e.Args[snapshotArg + 1];
            if (ArgumentAfter(e.Args, "--glass-bench") is { } benchFile)
            {
                _ = RunGlassBenchAsync(folder, benchFile);
                return;
            }
            if (ArgumentAfter(e.Args, "--self-test") is { } testFile)
            {
                _ = RunSelfTestAsync(testFile);
                return;
            }
            if (ArgumentAfter(e.Args, "--perf-bench") is { } perfFile)
            {
                _ = RunPerfBenchAsync(folder, perfFile);
                return;
            }
            if (ArgumentAfter(e.Args, "--wallpaper-bench") is { } wallpaperFile)
            {
                _ = RunWallpaperBenchAsync(wallpaperFile);
                return;
            }
            if (e.Args.Contains("--render-logo"))
            {
                RenderLogo(folder);
                return;
            }
            if (ArgumentAfter(e.Args, "--claude-test") is { } claudeFile)
            {
                _ = RunClaudeTestAsync(claudeFile);
                return;
            }
            if (ArgumentAfter(e.Args, "--remote-demo") is { } demoPort && int.TryParse(demoPort, out int port))
            {
                RunRemoteDemo(port);
                return;
            }
            _ = RunSnapshotAsync(folder, ArgumentAfter(e.Args, "--export-theme"), ArgumentAfter(e.Args, "--import-theme"),
                                 e.Args.Contains("--check-update"));
            return;
        }

        var importFile = ThemeShareService.ArgumentFile(e.Args); // double-clic sur un thème (.opaline, .bloomtheme)

        // Une seule instance : relancer l'exe ouvre les paramètres de celle qui tourne déjà.
        _singleInstance = new Mutex(true, @"Local\DynamicIsland.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            var running = DockNative.FindWindow(null, AppTrayIcon.MessageWindowTitle);
            if (running != IntPtr.Zero)
            {
                // C'est nous qui avons le droit de passer au premier plan (on vient d'être lancés) : on le cède.
                DockNative.GetWindowThreadProcessId(running, out uint runningProcess);
                DockNative.AllowSetForegroundWindow(runningProcess);
                if (importFile != null) AppTrayIcon.SendImport(running, Path.GetFullPath(importFile));
                else DockNative.PostMessage(running, AppTrayIcon.OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero);
            }
            Shutdown();
            return;
        }

        StartupTimer.Begin();
        // Filet de sécurité : si la session précédente a été tuée, on remet la barre des tâches d'abord.
        RunModule("Réparation de la barre des tâches", ShellGuard.RecoverAll);
        Log.Info($"Démarrage {AppInfo.Name} {typeof(App).Assembly.GetName().Version?.ToString(3)}{(SafeMode ? " (mode sans échec)" : "")}, langue {Loc.Language}");
        if (!SafeMode) RunModule("Sauvegarde des réglages", SettingsBackup.AtStartup);
        RunModule("Étagère", ShelfService.Load);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Plantage fatal : on rend le bureau propre avant que le processus ne meure.
            Log.Error("Plantage", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
            RestoreShell();
        };
        SessionEnding += (_, _) => RestoreShell();

        base.OnStartup(e);
        // Si Windows ferme l'île ou le dock (fin de tâche, WM_CLOSE…), toute l'app s'arrête proprement.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        long created = Stopwatch.GetTimestamp();
        _island = new IslandWindow();
        _island.Closed += (_, _) => QuitAll();
        _island.ContentRendered += (_, _) => StartupTimer.IslandShown();
        MainWindow = _island;
        StartupTimer.Record("Île (création)", created);

        RunModule("Icône de notification", () =>
        {
            _appTray = new AppTrayIcon(SafeMode);
            _appTray.OpenSettingsRequested += () => SettingsWindow.Open();
            _appTray.QuitRequested += () => Shutdown();
            _appTray.RestartRequested += () => Restart();
            _appTray.ImportRequested += path => RunModule("Thèmes", () => SettingsWindow.OpenImport(path));
        });
        RegisterModules();

        // 7A : l'île se dessine d'abord ; le dock, la barre de menus et les icônes d'arrière-plan juste après (priorité basse =
        // une fois la première image faite), puis les autres modules. La barre des tâches, déjà là, est cachée un instant plus tard.
        RunModule("Île", () =>
        {
            if (SettingsStore.Current.Island.Enabled) _island.Show();
        });
        Dispatcher.BeginInvoke(() =>
        {
            long dockCreated = Stopwatch.GetTimestamp();
            _dock = new DockWindow();
            _dock.Closed += (_, _) => QuitAll();
            StartupTimer.Record("Dock (création)", dockCreated);
            ApplyModules();
            SettingsStore.Changed += ApplyModules;
            if (SafeMode)
            {
                // Sans modules optionnels : l'île, l'icône de notification et les paramètres seulement.
                NotifyWhenPossible(Icons.Alert, Loc.T("safe.notice"), () => SettingsWindow.Open(SettingsPage.About));
                StartupTimer.Finish();
                return;
            }
            Dispatcher.BeginInvoke(() => StartModules(importFile), DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    /// <summary>Modules lancés juste après la première image (voir OnStartup), chacun isolé.</summary>
    private void StartModules(string? importFile)
    {
        RunModule("Mode Gaming", GamingMode.Start, ModuleGuard.Gaming);
        RunModule("Couleurs automatiques", () =>
        {
            WallpaperPalette.Start();
            WindowsAccentService.Apply();
            SettingsStore.Changed += WindowsAccentService.Apply; // gère ses erreurs lui-même
            CursorTheme.Apply();
            SettingsStore.Changed += CursorTheme.Apply;
            AutoThemeService.Start();
        }, ModuleGuard.Colors);
        RunModule("Widgets du bureau", () => DesktopWidgets.DesktopWidgetsService.Start(_island!.Media), ModuleGuard.Widgets);
        RunModule("Fonds d'écran", () =>
        {
            DynamicWallpaperService.Start();
            AnimatedWallpaper.Start();
        }, ModuleGuard.Wallpapers);
        RunModule("Palette de commandes", () =>
        {
            GlobalHotkey.Pressed += () => RunModule("Palette de commandes", () => Palette.PaletteWindow.Toggle(_island), ModuleGuard.Palette);
            GlobalHotkey.RevealPressed += () => RunModule("Dock", () => _dock?.ToggleReveal(), ModuleGuard.Dock);
            GlobalHotkey.Start();
        }, ModuleGuard.Palette);
        RunModule("Zone d'état du dock", () =>
        {
            // Langue du clavier aussi dans l'île : seulement quand elle change dans la même fenêtre (Win+Espace, clic).
            KeyboardLayoutService.Changed += switched =>
            {
                if (switched && SettingsStore.Current.Dock.KeyboardInIsland && KeyboardLayoutService.Current is { } layout)
                    RunModule("Île", () => _island?.ShowNotice(Icons.Keyboard, layout.Name, duration: TimeSpan.FromSeconds(1.6)));
            };
            KeyboardLayoutService.Start();
            WindowsUpdateService.Start();
            RecycleBinService.Start();
            DownloadsService.Start();
        }, ModuleGuard.Status);
        RunModule("Annonces de l'île", () =>
        {
            DeviceNotifier.Arrived += arrival => RunModule("Île", () => ShowSoon(() => ShowDevice(arrival), TimeSpan.FromSeconds(10)), ModuleGuard.Notices);
            DownloadNotifier.Finished += files => RunModule("Île", () => ShowSoon(() => ShowDownload(files), TimeSpan.FromSeconds(30)), ModuleGuard.Notices);
            DeviceNotifier.Start();
            DownloadNotifier.Start();
        }, ModuleGuard.Notices);
        RunModule("Télécommande", () =>
        {
            void Notify(string glyph, string text, string? detail) =>
                RunModule("Île", () => ShowSoon(() => _island?.ShowNotice(glyph, text, duration: TimeSpan.FromSeconds(8), detail: detail) ?? true,
                    TimeSpan.FromSeconds(30)));
            Services.Remote.RemoteControl.Start(new Services.Remote.AppRemoteBackend(() => _island, Notify));
            // PC réveillé par le téléphone (Wake on LAN) : l'île le dit, pour savoir que ça a marché.
            Services.Remote.WakeOnLan.WokenByNetwork += () => Dispatcher.BeginInvoke(() =>
                Notify(Icons.Smartphone, Loc.T("wake.notice"), Loc.T("wake.notice.detail")));
            Services.Remote.WakeOnLan.Watch();
        }, ModuleGuard.Remote);
        RunModule("Éclairage RGB", () => Services.Rgb.RgbService.Start(() => _island?.Media), ModuleGuard.Rgb);
        RunModule("Thèmes saisonniers", Services.Seasons.SeasonService.Start);
        RunModule("Santé du PC", () =>
        {
            Services.Health.HealthService.LowDisk += disk => RunModule("Île", () => ShowSoon(() => _island?.ShowNotice(Icons.HardDrive,
                Loc.F("health.disk.low", disk.Name.TrimEnd('\\')), () => Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true }),
                TimeSpan.FromSeconds(10), Loc.F("health.disk.low.detail", Math.Round(Services.Health.HealthService.FreePercent(disk)))) ?? true,
                TimeSpan.FromMinutes(2)), ModuleGuard.Health);
            Services.Health.HealthService.Start();
        }, ModuleGuard.Health);
        RunModule("Amis en ligne", () =>
        {
            Services.Friends.FriendsService.CameOnline += friend => RunModule("Île", () => ShowSoon(() => _island?.ShowNotice(Icons.Friends,
                friend.Display, duration: TimeSpan.FromSeconds(6),
                detail: friend.Status == "game" && friend.Game is { Length: > 0 } game ? Loc.F("friends.status.game", game) : Loc.T("friends.status.online")) ?? true,
                TimeSpan.FromSeconds(30)), ModuleGuard.Friends);
            Services.Friends.FriendsService.Start();
        }, ModuleGuard.Friends);
        RunModule("Thèmes", ThemeShareService.RegisterFileType);
        RunModule("Discord", DiscordVoice.Start, ModuleGuard.Discord);
        RunModule("Mises à jour", () =>
        {
            UpdateService.Found += update => RunModule("Mises à jour", () =>
                _island?.ShowNotice(Icons.Download, Loc.F("update.notice", update.Tag), () => SettingsWindow.Open(SettingsPage.Sharing)), ModuleGuard.Updates);
            UpdateService.Start();
        }, ModuleGuard.Updates);
        if (importFile != null)
            Dispatcher.BeginInvoke(() => RunModule("Thèmes", () => SettingsWindow.OpenImport(Path.GetFullPath(importFile))), DispatcherPriority.ApplicationIdle);
        else if (!SettingsStore.Current.WelcomeDone)
        {
            // Tout premier lancement : l'assistant de bienvenue, une fois l'île déployée.
            var welcome = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            welcome.Tick += (_, _) =>
            {
                welcome.Stop();
                RunModule("Bienvenue", Welcome.WelcomeWindow.Open);
            };
            welcome.Start();
        }
        MemoryWatch.Start();
        StartupTimer.Finish();
    }

    /// <summary>
    /// Annonce passagère (appareil, téléchargement) : si l'île est ouverte ou cachée, on réessaie quelques secondes,
    /// après quoi l'information n'a plus d'intérêt.
    /// </summary>
    /// <summary>Pour l'île elle-même (surprises, 6F) : même chose que <see cref="ShowSoon"/>.</summary>
    internal static void ShowSoonFromIsland(Func<bool> show, TimeSpan within) => ShowSoon(show, within);

    private static void ShowSoon(Func<bool> show, TimeSpan within)
    {
        if (show()) return;
        var until = DateTime.UtcNow + within;
        var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        retry.Tick += (_, _) =>
        {
            if (show() || DateTime.UtcNow > until) retry.Stop();
        };
        retry.Start();
    }

    /// <summary>Appareil connecté (6C) : nom en titre, batterie ou place libre en dessous ; clic = réglages Bluetooth ou la clé.</summary>
    private bool ShowDevice(DeviceNotifier.Arrival arrival)
    {
        if (_island == null) return true;
        string glyph = arrival.Kind switch
        {
            DeviceNotifier.Kind.Audio => Icons.Headphones,
            DeviceNotifier.Kind.Controller => Icons.Gamepad,
            DeviceNotifier.Kind.Storage => Icons.Usb,
            _ => Icons.Bluetooth,
        };
        string detail;
        if (arrival.Drive is { } drive)
        {
            double free = 0;
            try
            {
                free = new DriveInfo(drive).AvailableFreeSpace;
            }
            catch (IOException)
            {
                // Place libre inconnue.
            }
            detail = free > 0 ? Loc.F("device.usb.free", WindowsUpdateService.FormatSize(free)) : Loc.T("device.usb.plugged");
        }
        else
        {
            detail = arrival.Battery is { } battery ? Loc.F("device.connected.battery", battery) : Loc.T("device.connected");
        }
        Action? click = arrival.Kind switch
        {
            DeviceNotifier.Kind.Storage when arrival.Drive is { } path => () => DeviceNotifier.OpenDrive(path),
            DeviceNotifier.Kind.Audio or DeviceNotifier.Kind.Bluetooth => () =>
                Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true })?.Dispose(),
            _ => null,
        };
        return _island.ShowNotice(glyph, arrival.Name, click, TimeSpan.FromSeconds(5), detail);
    }

    /// <summary>Téléchargement terminé (6C) : clic = ouvrir, glisser l'annonce = déposer le fichier ailleurs.</summary>
    private bool ShowDownload(IReadOnlyList<string> files)
    {
        if (_island == null) return true;
        var existing = files.Where(f => File.Exists(f)).ToList();
        if (existing.Count == 0) return true; // déjà déplacé ou supprimé : plus rien à annoncer
        string text = existing.Count == 1 ? Path.GetFileName(existing[0]) : Loc.F("download.many", existing.Count);
        Action click = existing.Count == 1 ? () => DownloadsService.Open(existing[0]) : () => DownloadsService.Open(DownloadsService.Folder);
        return _island.ShowNotice(Icons.FileDown, text, click, TimeSpan.FromSeconds(8), Loc.T("download.done"), existing);
    }

    /// <summary>Annonce de quelques secondes dans l'île (thème installé, mise à jour…).</summary>
    public void Notify(string glyph, string text, Action? onClick = null) =>
        RunModule("Île", () => _island?.ShowNotice(glyph, text, onClick));

    /// <summary>
    /// Mode --remote-demo &lt;port&gt; (développeur) : la page de la télécommande sur 127.0.0.1 SEULEMENT, avec un faux état
    /// (aucune vraie action sur le PC) et le code d'appairage 000000, pour la regarder dans un navigateur. Pas de pare-feu.
    /// </summary>
    private static Services.Remote.RemoteServer? _demoServer;

    private void RunRemoteDemo(int port)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var devices = new List<RemoteDevice>();
        var uploads = new Services.Remote.RemoteUploads(Path.Combine(Path.GetTempPath(), "opaline-remote-demo"));
        var api = new Services.Remote.RemoteApi(new Services.Remote.DemoRemoteBackend(), () => devices, d => devices.Add(new RemoteDevice { Id = d.Id, Name = d.Name, TokenHash = d.TokenHash, Added = d.Added, FullControl = true }),
            id => devices.RemoveAll(d => d.Id == id), uploads); // démo : tout est permis (rien de réel)
        api.OpenPairing("000000");
        api.Changed += () =>
        {
            if (!api.PairingOpen) api.OpenPairing("000000"); // la démo reste appairable
        };
        _demoServer = new Services.Remote.RemoteServer(request => Dispatcher.InvokeAsync(() => api.HandleAsync(request)).Task.Unwrap());
        _demoServer.Start([System.Net.IPAddress.Loopback], port);
        Log.Info($"Démo de la télécommande sur http://127.0.0.1:{port}/ (code 000000)");
    }

    // ------------------------------------------------------------------ solidité (6A)

    /// <summary>Maj maintenue au lancement : mode sans échec.</summary>
    private static bool ShiftHeld() => (DockNative.GetAsyncKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0;

    private static void WaitForExit(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(15_000);
        }
        catch (ArgumentException)
        {
            // Déjà terminée.
        }
    }

    /// <summary>
    /// Relance l'app (langue changée, réglages précédents remis, sortie du mode sans échec) : la nouvelle instance attend
    /// la fin de celle-ci. <paramref name="safeMode"/> : relancer en mode sans échec.
    /// </summary>
    public static void Restart(bool safeMode = false)
    {
        SettingsStore.Flush();
        if (Environment.ProcessPath is { } exe)
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add("--restart-after");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (safeMode) start.ArgumentList.Add("--safe-mode");
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        Current.Shutdown();
    }

    /// <summary>Ce qu'il faut faire pour arrêter chaque module qui planterait (voir ModuleGuard).</summary>
    private void RegisterModules()
    {
        ModuleGuard.Register(ModuleGuard.Dock, () =>
        {
            _dock?.ReleaseShell(); // la barre des tâches revient
            _dock?.DetachTray();
            _tray.Dispose();
            _dock?.Hide();
        });
        ModuleGuard.Register(ModuleGuard.Status, () =>
        {
            KeyboardLayoutService.Stop();
            WindowsUpdateService.Stop();
            RecycleBinService.Stop();
            DownloadsService.Stop();
            _dock?.StopStatus();
        });
        ModuleGuard.Register(ModuleGuard.Notices, () =>
        {
            DeviceNotifier.Stop();
            DownloadNotifier.Stop();
        });
        ModuleGuard.Register(ModuleGuard.MenuBar, () =>
        {
            _menuBar?.Close(); // rend l'espace réservé en haut de l'écran
            _menuBar = null;
        });
        ModuleGuard.Register(ModuleGuard.Widgets, DesktopWidgets.DesktopWidgetsService.Stop);
        ModuleGuard.Register(ModuleGuard.Wallpapers, AnimatedWallpaper.Stop); // le fond dynamique s'arrête de lui-même
        ModuleGuard.Register(ModuleGuard.Discord, DiscordVoice.Reconnect); // la boucle se réveille et voit le module arrêté
        ModuleGuard.Register(ModuleGuard.Claude, ClaudeService.Stop);
        ModuleGuard.Register(ModuleGuard.Palette, GlobalHotkey.Refresh);
        ModuleGuard.Register(ModuleGuard.Rgb, Services.Rgb.RgbService.Stop); // G HUB reprend l'éclairage
        ModuleGuard.Register(ModuleGuard.Friends, Services.Friends.FriendsService.Stop); // présence effacée du serveur
        ModuleGuard.Register(ModuleGuard.Health, Services.Health.HealthService.Stop);
        ModuleGuard.Register(ModuleGuard.Remote, () =>
        {
            Services.Remote.RemoteControl.Stop(); // plus rien n'écoute sur le réseau
            Services.Remote.WakeOnLan.StopWatching();
        });
        ModuleGuard.ModuleDisabled += id =>
            NotifyWhenPossible(Icons.Alert, Loc.F("module.stopped", ModuleGuard.Name(id)), () => SettingsWindow.Open(SettingsPage.About));
    }

    /// <summary>Annonce dans l'île dès que possible (2 minutes au plus) : l'île peut être ouverte ou cachée en ce moment.</summary>
    private void NotifyWhenPossible(string glyph, string text, Action? onClick)
    {
        _pendingNotices.Add((glyph, text, onClick, DateTime.UtcNow.AddMinutes(2)));
        if (_noticeRetry == null)
        {
            _noticeRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _noticeRetry.Tick += (_, _) => ShowPendingNotice();
        }
        _noticeRetry.Start();
    }

    private void ShowPendingNotice()
    {
        _pendingNotices.RemoveAll(n => n.Until < DateTime.UtcNow);
        if (_pendingNotices.Count == 0)
        {
            _noticeRetry?.Stop();
            return;
        }
        var (glyph, text, onClick, _) = _pendingNotices[0];
        if (_island?.ShowNotice(glyph, text, onClick) == true) _pendingNotices.RemoveAt(0);
    }

    /// <summary>Demande à l'instance qui tourne de se fermer (comme « Quitter ») et attend qu'elle ait fini (10 s max.).</summary>
    private static void QuitRunningInstance()
    {
        var island = DockNative.FindWindow(null, "Dynamic Island");
        if (island == IntPtr.Zero) return;
        DockNative.GetWindowThreadProcessId(island, out uint processId);
        DockNative.PostMessage(island, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            process.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // Déjà fermée.
        }
    }

    private static string? ArgumentAfter(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// Mode --snapshot (réglages en lecture seule) : vérification des mises à jour, export et/ou import d'un
    /// thème en mémoire pour les tester, puis rendu des fenêtres. Rien n'est écrit dans les réglages de l'app qui tourne.
    /// </summary>
    private async Task RunSnapshotAsync(string folder, string? exportFile, string? importFile, bool checkUpdate)
    {
        try
        {
            if (checkUpdate)
            {
                await UpdateService.CheckAsync();
                Log.Info($"Snapshot : mises à jour → {UpdateService.Status} {UpdateService.Available?.AssetName} {UpdateService.Available?.AssetSize}");
            }
            if (exportFile != null)
            {
                ThemeShareService.Export(exportFile, allProfiles: true, wallpapers: true);
                Log.Info($"Snapshot : thème exporté → {exportFile}");
            }
            if (importFile != null)
            {
                var result = await ThemeShareService.ImportAsync(ThemeShareService.Read(importFile));
                Log.Info($"Snapshot : import → profils {string.Join(", ", result.Profiles)}, {result.Apps} apps, introuvables : {string.Join(", ", result.Missing)}, actif : {result.Activated}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot : thème", ex);
        }
        try
        {
            // Fonds d'écran du pack (ambiance du pack) + « Aurore » dans les 4 ambiances du fond dynamique.
            await Task.Run(() =>
            {
                foreach (var (design, _) in WallpaperArt.Designs)
                    WallpaperArt.SavePng(WallpaperArt.Render(design, Themes.Wallpapers.Night, 1280, 720),
                        Path.Combine(folder, $"9-fond-{design.ToString().ToLowerInvariant()}.png"));
                foreach (var (mood, name) in new[] { (Themes.Wallpapers.Morning, "matin"), (Themes.Wallpapers.Day, "jour"), (Themes.Wallpapers.Sunset, "coucher") })
                    WallpaperArt.SavePng(WallpaperArt.Render(WallpaperDesign.Aurore, mood, 640, 360),
                        Path.Combine(folder, $"9b-aurore-{name}.png"));
            });
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot : fonds d'écran", ex);
        }
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown; // fermer la fenêtre d'un widget ne doit pas arrêter l'app
            foreach (var shape in Enum.GetValues<CursorTheme.Shape>())
                WallpaperArt.SavePng(CursorTheme.Render(shape, AppearanceService.AccentStart, AppearanceService.AccentEnd, 128),
                    Path.Combine(folder, $"12-curseur-{shape.ToString().ToLowerInvariant()}.png"));
            var menuBar = new MenuBar.MenuBarWindow(snapshot: true);
            menuBar.Show();
            await Task.Delay(1500); // météo, icône de l'appli au premier plan
            await SnapshotRenderer.SaveAsync(menuBar, Path.Combine(folder, "11-barre-de-menus.png"));
            menuBar.Close();
            var media = new MediaService(Dispatcher);
            await media.InitializeAsync();
            await DesktopWidgets.DesktopWidgetsService.RenderSnapshotsAsync(folder, media);
            await Welcome.WelcomeWindow.RenderSnapshotsAsync(folder);
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot : widgets du bureau", ex);
        }
        finally
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        MainWindow = new IslandWindow { SnapshotFolder = folder };
        MainWindow.Show();
        new DockWindow { SnapshotFolder = folder }.Show();
    }

    /// <summary>Mode --render-logo : logo d'Opaline en PNG (aperçus) + app.ico à copier dans Assets, puis fermeture.</summary>
    private void RenderLogo(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 1024 })
                File.WriteAllBytes(Path.Combine(folder, $"logo-{size}.png"), OpalineLogo.Png(OpalineLogo.Render(size)));
            OpalineLogo.WriteIcon(Path.Combine(folder, "app.ico"));
            OpalineLogo.WriteInstallerImages(Path.Combine(folder, "installeur"));
        }
        catch (Exception ex)
        {
            Log.Error("Logo", ex);
        }
        Shutdown();
    }

    /// <summary>
    /// Mode --claude-test : état de Claude Code puis une vraie petite question (même chemin que l'onglet Claude), résultat
    /// dans le fichier. Connecté, cela compte (un peu) dans les limites du forfait de l'utilisateur.
    /// </summary>
    private async Task RunClaudeTestAsync(string file)
    {
        var lines = new List<string> { $"Test du mini Claude — {DateTime.Now:yyyy-MM-dd HH:mm}, modèle {SettingsStore.Current.Claude.Model}" };
        try
        {
            var status = await ClaudeService.CheckAsync();
            lines.Add($"État : installé {status.Installed}, connecté {status.LoggedIn} — {status.Description}");
            var done = new TaskCompletionSource();
            ClaudeService.Changed += () =>
            {
                if (!ClaudeService.IsBusy && ClaudeService.Turns is [.., { Done: true }]) done.TrySetResult();
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ClaudeService.Ask("Réponds seulement : « OK, je suis là. »");
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(3)));
            var turn = ClaudeService.Turns[^1];
            lines.Add($"Réponse en {watch.ElapsedMilliseconds} ms : {(turn.Answer.Length > 0 ? turn.Answer : "(aucune)")}");
            if (turn.Error != null) lines.Add($"Erreur affichée : {turn.Error} (bouton : {turn.Fix})");
        }
        catch (Exception ex)
        {
            lines.Add($"ÉCHEC : {ex}");
        }
        File.WriteAllText(file, string.Join(Environment.NewLine, lines));
        Shutdown();
    }

    /// <summary>Mode --wallpaper-bench : coût du fond animé (voir Services/WallpaperBench), puis fermeture.</summary>
    private async Task RunWallpaperBenchAsync(string file)
    {
        try
        {
            File.WriteAllText(file, await WallpaperBench.RunAsync(Path.ChangeExtension(file, ".png")));
        }
        catch (Exception ex)
        {
            Log.Error("Mesure du fond animé", ex);
        }
        Shutdown();
    }

    /// <summary>Mode --self-test : vérifications sans fenêtre ni changement de Windows (voir Services/SelfTest).</summary>
    private async Task RunSelfTestAsync(string file)
    {
        try
        {
            File.WriteAllText(file, await SelfTest.RunAsync());
        }
        catch (Exception ex)
        {
            Log.Error("Auto-test", ex);
        }
        Shutdown();
    }

    /// <summary>Mode --perf-bench (7A) : coût de chaque module au repos (voir Services/PerfBench), puis fermeture.</summary>
    private async Task RunPerfBenchAsync(string folder, string file)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var island = new IslandWindow { SnapshotFolder = folder, GlassBenchOnly = true };
        var dock = new DockWindow { SnapshotFolder = folder, GlassBenchOnly = true };
        MainWindow = island;
        island.Show();
        dock.Show();
        try
        {
            File.WriteAllText(file, await PerfBench.RunAsync(island, dock));
        }
        catch (Exception ex)
        {
            Log.Error("Audit de performance", ex);
        }
        dock.Close();
        island.Close();
        Shutdown();
    }

    /// <summary>Mode --glass-bench : mesure du coût du verre liquide (voir Services/GlassBench), puis fermeture.</summary>
    private async Task RunGlassBenchAsync(string folder, string file)
    {
        var island = new IslandWindow { SnapshotFolder = folder, GlassBenchOnly = true };
        var dock = new DockWindow { SnapshotFolder = folder, GlassBenchOnly = true };
        MainWindow = island;
        island.Show();
        dock.Show();
        try
        {
            File.WriteAllText(file, await GlassBench.RunAsync(island, dock));
        }
        catch (Exception ex)
        {
            Log.Error("Mesure du verre", ex);
        }
        dock.Close();
        island.Close();
    }

    /// <summary>
    /// Active / désactive l'île, le dock et les icônes d'arrière-plan selon les réglages. Chaque module
    /// est isolé : si l'un plante, les autres continuent (et la barre des tâches peut toujours revenir).
    /// </summary>
    private void ApplyModules()
    {
        var settings = SettingsStore.Current;

        RunModule("Île", () =>
        {
            if (settings.Island.Enabled && !_island!.IsVisible) _island.Show();
            else if (!settings.Island.Enabled && _island!.IsVisible) _island.Hide();
        });

        RunModule("Dock", () =>
        {
            bool dock = settings.Dock.Enabled && !ModuleGuard.IsDisabled(ModuleGuard.Dock);
            if (dock && !_dock!.IsVisible)
            {
                _dock.Show();
                _dock.ApplySettings();
            }
            else if (!dock && _dock!.IsVisible)
            {
                _dock.ReleaseShell(); // pas de dock = la barre des tâches revient
                _dock.Hide();
            }
        }, ModuleGuard.Dock);

        RunModule("Barre de menus", () =>
        {
            bool menuBar = settings.MenuBar.Enabled && !ModuleGuard.IsDisabled(ModuleGuard.MenuBar);
            if (menuBar && _menuBar == null)
            {
                _menuBar = new MenuBar.MenuBarWindow();
                _menuBar.Show();
            }
            else if (!menuBar && _menuBar != null)
            {
                _menuBar.Close(); // rend l'espace réservé en haut de l'écran
                _menuBar = null;
            }
        });

        RunModule("Icônes d'arrière-plan", () =>
        {
            bool wanted = settings.Dock.Enabled && settings.Dock.ShowTrayIcons && !ModuleGuard.IsDisabled(ModuleGuard.Dock);
            if (wanted && _tray.Icons == null)
            {
                // Après le dock : il a déjà masqué la barre et réservé son espace.
                if (_tray.Start() && _tray.Icons is { } icons)
                {
                    _dock!.AttachTray(icons);
                    ReportTrayIconsOnce(icons);
                }
            }
            else if (!wanted && _tray.Icons != null)
            {
                _dock!.DetachTray();
                _tray.Dispose(); // les icônes retournent dans la zone de notification d'Explorer
            }
        });
    }

    /// <summary>Une ligne dans le journal, 5 s après le démarrage, avec les icônes reçues (diagnostic).</summary>
    private void ReportTrayIconsOnce(System.Collections.ObjectModel.ObservableCollection<ManagedShell.WindowsTray.NotifyIcon> icons)
    {
        if (_trayReported) return;
        _trayReported = true;
        var report = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        report.Tick += (_, _) =>
        {
            report.Stop();
            Log.Info($"Zone de notification : {icons.Count} icône(s) — " +
                     string.Join(", ", icons.Select(i => $"{i.Title}{(i.IsHidden ? " (masquée)" : "")}")));
        };
        report.Start();
    }

    /// <summary>
    /// Lance une partie d'un module ; s'il plante, il est arrêté pour la session et annoncé (<paramref name="module"/>,
    /// sinon retrouvé dans la pile d'appels), le reste continue.
    /// </summary>
    private static void RunModule(string name, Action action, string? module = null)
    {
        long started = StartupTimer.Running ? Stopwatch.GetTimestamp() : 0;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (module != null) ModuleGuard.Fail(module, ex);
            else if (!ModuleGuard.TryFail(ex)) Log.Error($"Module « {name} »", ex);
        }
        if (started != 0) StartupTimer.Record(name, started); // 7A : durée de chaque module au démarrage
    }

    /// <summary>Fermeture : une erreur est seulement notée (pas d'arrêt de module ni d'annonce).</summary>
    private static void RunQuietly(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Fermeture : « {name} »", ex);
        }
    }

    private void QuitAll()
    {
        if (_quitting) return;
        _quitting = true;
        Dispatcher.BeginInvoke(() => Shutdown());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Flush();
        RestoreShell();
        RunQuietly("Icônes d'arrière-plan", _tray.Dispose); // rend les icônes à Explorer
        RunQuietly("Icône de notification", () => _appTray?.Dispose());
        RunQuietly("Fond animé", AnimatedWallpaper.Stop); // retire nos fenêtres du bureau
        RunQuietly("Widgets du bureau", DesktopWidgets.DesktopWidgetsService.Stop);
        RunQuietly("Claude", ClaudeService.Stop); // ferme une réponse en cours (processus de Claude Code)
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Remet la barre des tâches et libère l'espace réservé au dock (peut être appelé plusieurs fois).</summary>
    private void RestoreShell()
    {
        try
        {
            _dock?.ReleaseShell();
            _menuBar?.ReleaseSpace();
            if (TaskbarService.IsHidden) TaskbarService.Restore();
        }
        catch (Exception ex)
        {
            Log.Error("Restauration du bureau", ex);
        }
    }
}
