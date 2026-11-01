using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services.Stats;

/// <summary>
/// « Ma semaine » (7F, module « stats ») : relevé LOCAL de l'activité, seulement quand « Suivre mon activité » est activé.
/// Une minuterie d'une minute (rien d'autre) : temps d'écran = temps où l'utilisateur est actif (inactif après 5 min sans
/// clavier ni souris, session verrouillée et veille jamais comptées), temps de jeu = mode Gaming actif (par jeu), applis au
/// premier plan, Pomodoros terminés (fin de concentration du minuteur de l'île). Tout va dans statistiques.json (écriture
/// différée et atomique) ; rien n'est envoyé, rien dans settings.json ni dans le rapport de bug.
/// </summary>
public static class UsageStats
{
    private static readonly TimeSpan SampleEvery = TimeSpan.FromMinutes(1);
    /// <summary>Plus rien au clavier ni à la souris depuis ce temps : l'utilisateur n'est plus là.</summary>
    internal static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    /// <summary>Écart plus long entre deux relevés (veille, PC figé) : rien n'est compté pour cet écart.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(10);

    /// <summary>Jamais comptés comme « appli » : Opaline, le bureau, l'écran de verrouillage, les menus de Windows.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "DynamicIsland", "explorer", "LockApp", "LogonUI", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
        "ApplicationFrameHost", "TextInputHost", "Idle", "dwm", "consent",
    };

    private static readonly DispatcherTimer Sampler = new() { Interval = SampleEvery };
    private static readonly Dictionary<string, string> AppNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> GameNames = new(StringComparer.OrdinalIgnoreCase);
    private static Func<IslandWindow?> _island = () => null;
    private static UsageLog? _log;
    private static UsageLog? _sample; // --snapshot : données d'exemple en mémoire
    private static DateTime _last, _lastSave;
    private static bool _started, _locked, _asleep, _dirty;

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "statistiques.json");

    /// <summary>Les chiffres ont changé (thread de l'interface) : la page « Ma semaine » se redessine.</summary>
    public static event Action? Changed;

    public static bool Tracking => SettingsStore.Current.Stats.Enabled && !ModuleGuard.IsDisabled(ModuleGuard.Stats) && !SettingsStore.IsReadOnly;

    /// <summary>Historique (exemple en --snapshot) ; lu depuis le disque la première fois.</summary>
    public static UsageLog Log => _sample ?? (_log ??= LoadLog());

    private static UsageLog LoadLog()
    {
        var log = UsageLog.Load(FilePath);
        if (log.Prune(DateOnly.FromDateTime(DateTime.Now))) _dirty = true;
        return log;
    }

    public static void Start(Func<IslandWindow?> island)
    {
        if (_started) return;
        _started = true;
        _island = island;
        Sampler.Tick += OnTick;
        SettingsStore.Changed += Apply;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Apply();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        Sampler.Tick -= OnTick;
        SettingsStore.Changed -= Apply;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Sampler.Stop();
        Flush();
    }

    /// <summary>La minuterie tourne seulement si le suivi est activé (rien du tout sinon).</summary>
    private static void Apply()
    {
        if (Tracking)
        {
            if (Sampler.IsEnabled) return;
            _last = DateTime.Now;
            _lastSave = _last;
            Sampler.Start();
            global::DynamicIsland.Log.Info("Ma semaine : suivi de l'activité activé");
        }
        else if (Sampler.IsEnabled)
        {
            Sampler.Stop();
            Flush();
            global::DynamicIsland.Log.Info("Ma semaine : suivi de l'activité coupé");
        }
    }

    private static void OnTick(object? sender, EventArgs e) => Guarded.Run(Sample, "Ma semaine : relevé");

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Guarded.OnUi(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            if (Sampler.IsEnabled) Sample(); // la minute en cours compte encore
            _locked = true;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            _locked = false;
            _last = DateTime.Now; // le temps verrouillé n'est pas compté
        }
    }, "Ma semaine : session");

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => Guarded.OnUi(() =>
    {
        if (e.Mode == PowerModes.Suspend)
        {
            if (Sampler.IsEnabled) Sample();
            Flush();
            _asleep = true;
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _asleep = false;
            _last = DateTime.Now;
        }
    }, "Ma semaine : veille");

    /// <summary>Relevé : attribue le temps écoulé depuis le précédent (actif seulement) au jeu, sinon à l'appli au premier plan.</summary>
    private static void Sample()
    {
        var now = DateTime.Now;
        var elapsed = now - _last;
        _last = now;
        if (!Tracking || _locked || _asleep || elapsed <= TimeSpan.Zero || elapsed > MaxGap) return;

        string? game = GamingMode.IsActive ? GameName(GamingMode.Game) : null;
        // Manette, cinématique : un jeu compte même sans clavier ni souris.
        var active = game != null ? elapsed : ActivePart(elapsed, IdleTime());
        if (active > TimeSpan.Zero)
        {
            Log.Record(now - active, now, game, game == null ? ForegroundApp() : null);
            _dirty = true;
            Changed?.Invoke();
        }
        if (now - _lastSave >= SaveEvery) Flush();
        CheckWeeklySummary(now);
    }

    /// <summary>Part active d'un intervalle : sans entrée depuis <paramref name="idle"/>, tout ce qui dépasse 5 min ne compte pas.</summary>
    internal static TimeSpan ActivePart(TimeSpan elapsed, TimeSpan idle)
    {
        if (idle <= IdleAfter) return elapsed;
        var inactive = idle - IdleAfter;
        return inactive >= elapsed ? TimeSpan.Zero : elapsed - inactive;
    }

    /// <summary>Pomodoro terminé (fin de la concentration, minuteur de l'île ou du téléphone).</summary>
    public static void PomodoroDone()
    {
        if (!Tracking) return;
        Log.AddPomodoro(DateTime.Now);
        _dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Enregistre maintenant s'il y a du nouveau (fermeture de l'app, veille, toutes les 10 min).</summary>
    public static void Flush()
    {
        _lastSave = DateTime.Now;
        if (!_dirty || _log == null || SettingsStore.IsReadOnly) return;
        try
        {
            _log.Prune(DateOnly.FromDateTime(DateTime.Now));
            _log.Save(FilePath);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            global::DynamicIsland.Log.Error("Ma semaine : enregistrement de statistiques.json", ex);
        }
    }

    /// <summary>« Effacer l'historique » : le fichier est supprimé, l'historique en mémoire vidé.</summary>
    public static void Clear()
    {
        _log = new UsageLog();
        _dirty = false;
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            global::DynamicIsland.Log.Info("Ma semaine : historique effacé");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            global::DynamicIsland.Log.Error("Ma semaine : effacement de statistiques.json", ex);
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ résumé du lundi

    /// <summary>Première minute suivie d'une nouvelle semaine (lundi ou mardi, après 7 h) : résumé de la semaine passée dans l'île.</summary>
    private static void CheckWeeklySummary(DateTime now)
    {
        var settings = SettingsStore.Current.Stats;
        var today = DateOnly.FromDateTime(now);
        var week = UsageLog.WeekStart(today);
        var key = week.ToString(UsageLog.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
        if (settings.LastSummaryWeek == key) return;
        bool due = settings.WeeklySummary && now.Hour >= 7 && today.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday;
        var previous = Log.Week(week.AddDays(-7));
        if (due && previous.HasData)
        {
            // Ne pas déranger : ShowNotice répond « fait » sans rien montrer (le résumé est sauté, pas reporté).
            bool shown = _island()?.ShowNotice(Icons.Week, Loc.T("week.notice.title"), () => SettingsWindow.Open(SettingsPage.Week),
                TimeSpan.FromSeconds(8), SummaryLine(previous)) ?? false;
            if (!shown) return; // île occupée : on réessaie à la minute suivante
        }
        else if (settings.WeeklySummary && now.Hour < 7 && today.DayOfWeek == DayOfWeek.Monday && previous.HasData) return; // trop tôt
        SettingsStore.Update(s => s.Stats.LastSummaryWeek = key);
    }

    /// <summary>« 32 h d'écran · 6 h de jeu · 9 Pomodoros ».</summary>
    public static string SummaryLine(WeekSummary week) =>
        Loc.F("week.notice.detail", Format(week.ScreenSeconds), Format(week.GameSeconds), week.Pomodoros);

    /// <summary>« 45 min », « 3 h 05 », « 32 h », « moins d'une minute ».</summary>
    public static string Format(int seconds)
    {
        int minutes = (int)Math.Round(seconds / 60.0);
        if (seconds > 0 && minutes == 0) return Loc.T("week.duration.less");
        if (minutes < 60) return Loc.F("week.duration.m", minutes);
        int hours = minutes / 60, rest = minutes % 60;
        return hours >= 10 || rest == 0 ? Loc.F("week.duration.h", Math.Round(minutes / 60.0)) : Loc.F("week.duration.hm", hours, rest.ToString("00"));
    }

    // ------------------------------------------------------------------ jeu, appli

    private static string? GameName(string? process)
    {
        if (string.IsNullOrWhiteSpace(process)) return null;
        if (!GameNames.TryGetValue(process, out var name))
            GameNames[process] = name = Friends.FriendsService.GameName(process) ?? process;
        return name;
    }

    /// <summary>Nom lisible de l'appli au premier plan (description de l'exe, sinon nom du processus nettoyé) ; null = rien à compter.</summary>
    private static string? ForegroundApp()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        DockNative.GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (Ignored.Contains(name)) return null;
            if (!AppNames.TryGetValue(name, out var display))
                AppNames[name] = display = Describe(process) ?? Friends.FriendsService.PrettyName(name);
            return display;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // fermée entre-temps
        }
    }

    private static string? Describe(Process process)
    {
        try
        {
            var info = process.MainModule?.FileVersionInfo;
            return new[] { info?.FileDescription, info?.ProductName }
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 40 && !n.Contains("Operating System", StringComparison.OrdinalIgnoreCase))?.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return null; // appli administrateur ou protégée : nom du processus
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Deux semaines inventées (en mémoire seulement) pour le rendu de la page.</summary>
    internal static void UseSample(DateOnly today)
    {
        var log = new UsageLog();
        var random = new Random(7);
        string[] apps = ["Google Chrome", "Visual Studio Code", "Discord", "Spotify", "Microsoft Word", "Obsidian"];
        string[] games = ["Valorant", "Rocket League", "Minecraft"];
        var start = UsageLog.WeekStart(today).AddDays(-7);
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            var at = date.ToDateTime(new TimeOnly(9, 0));
            int weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1 : 0;
            foreach (var app in apps)
            {
                int minutes = random.Next(5, 95) - weekend * 20;
                if (minutes <= 0) continue;
                log.Record(at, at.AddMinutes(minutes), null, app);
                at = at.AddMinutes(minutes);
            }
            if (random.NextDouble() < 0.55 + weekend * 0.4)
            {
                var game = games[random.Next(games.Length)];
                log.Record(at, at.AddMinutes(random.Next(30, 150) + weekend * 60), game, null);
            }
            for (int i = random.Next(0, 5 - weekend * 3); i > 0; i--) log.AddPomodoro(at);
        }
        _sample = log;
    }
}
