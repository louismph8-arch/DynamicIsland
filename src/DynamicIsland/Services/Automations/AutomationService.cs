using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services.Automations;

/// <summary>
/// Automatisations « Quand… alors… » (7D, comme Raccourcis sur macOS). Déclencheurs : jeu lancé / fini (mode Gaming), appli
/// lancée / fermée (liste des processus toutes les 3 s, seulement s'il y a une règle de ce type), heure précise (chaque minute),
/// appareil branché (DeviceNotifier), PC inactif (GetLastInputInfo toutes les 30 s), sortie de veille. Les actions d'une règle
/// sont faites dans l'ordre, chacune isolée (une qui échoue n'empêche pas les suivantes), sur le thread de l'interface. Une
/// même règle ne repart pas avant <see cref="Cooldown"/> (pas de boucle). Seules les règles ACTIVÉES par l'utilisateur
/// tournent ; les exemples sont créés désactivés.
/// </summary>
public static class AutomationService
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);
    private static readonly DispatcherTimer Clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private static readonly DispatcherTimer Processes = new() { Interval = TimeSpan.FromSeconds(3) };
    private static readonly Dictionary<string, DateTime> LastRun = [];
    private static Func<IslandWindow?> _island = () => null;
    private static HashSet<string>? _running;
    private static string _lastMinute = "";
    private static readonly HashSet<string> IdleFired = []; // règles « PC inactif » déjà parties pour cette inactivité
    private static bool _started, _wasGaming;

    /// <summary>Une règle vient de tourner (nom, nombre d'actions faites) : la page des paramètres se met à jour.</summary>
    public static event Action<AutomationRule>? Ran;

    public static void Start(Func<IslandWindow?> island)
    {
        if (_started) return;
        _started = true;
        _island = island;
        AddExamplesOnce();
        _wasGaming = GamingMode.IsActive;
        GamingMode.Changed += OnGamingChanged;
        DeviceNotifier.Arrived += OnDeviceArrived;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Clock.Tick += (_, _) => OnClock();
        Processes.Tick += (_, _) => OnProcesses();
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        GamingMode.Changed -= OnGamingChanged;
        DeviceNotifier.Arrived -= OnDeviceArrived;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SettingsStore.Changed -= Apply;
        Clock.Stop();
        Processes.Stop();
    }

    private static IEnumerable<AutomationRule> Active =>
        ModuleGuard.IsDisabled(ModuleGuard.Automations) ? [] : SettingsStore.Current.Automations.Rules.Where(r => r.Enabled);

    /// <summary>Minuteries seulement si une règle active en a besoin (rien ne tourne pour rien).</summary>
    private static void Apply()
    {
        var kinds = Active.Select(r => r.Trigger.Kind).ToHashSet();
        if (kinds.Contains(TriggerKind.Time) || kinds.Contains(TriggerKind.Idle)) Clock.Start();
        else Clock.Stop();
        if (kinds.Contains(TriggerKind.AppStarted) || kinds.Contains(TriggerKind.AppClosed))
        {
            if (!Processes.IsEnabled)
            {
                _running = RunningNames(); // état de départ : les applis déjà ouvertes ne déclenchent rien
                Processes.Start();
            }
        }
        else
        {
            Processes.Stop();
            _running = null;
        }
    }

    // ------------------------------------------------------------------ déclencheurs

    private static void OnGamingChanged()
    {
        bool gaming = GamingMode.IsActive;
        if (gaming == _wasGaming) return;
        _wasGaming = gaming;
        Fire(gaming ? TriggerKind.GameStarted : TriggerKind.GameEnded, _ => true);
    }

    private static void OnDeviceArrived(DeviceNotifier.Arrival arrival) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Fire(TriggerKind.DeviceConnected, trigger => DeviceMatches(trigger, arrival)));

    internal static bool DeviceMatches(AutomationTrigger trigger, DeviceNotifier.Arrival arrival)
    {
        bool kind = trigger.Device switch
        {
            DeviceFilter.Headset => arrival.Kind is DeviceNotifier.Kind.Audio || arrival.Kind is DeviceNotifier.Kind.Bluetooth && LooksLikeAudio(arrival.Name),
            DeviceFilter.Controller => arrival.Kind == DeviceNotifier.Kind.Controller,
            DeviceFilter.Storage => arrival.Kind == DeviceNotifier.Kind.Storage,
            _ => true,
        };
        return kind && (string.IsNullOrWhiteSpace(trigger.DeviceName) || arrival.Name.Contains(trigger.DeviceName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeAudio(string name) =>
        new[] { "casque", "headset", "headphone", "écouteur", "buds", "airpods", "audio", "speaker", "enceinte" }
            .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // Quelques secondes : le réseau, l'audio et les écrans reviennent d'abord.
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            Fire(TriggerKind.Resume, _ => true);
        });
    }

    private static void OnClock()
    {
        var now = DateTime.Now;
        var minute = now.ToString("HH:mm");
        if (minute != _lastMinute)
        {
            _lastMinute = minute;
            Fire(TriggerKind.Time, trigger => TimeMatches(trigger, now));
        }
        var idle = IdleFor();
        foreach (var rule in Active.Where(r => r.Trigger.Kind == TriggerKind.Idle).ToList())
        {
            // Une fois par période d'inactivité : il faut un geste au clavier / à la souris avant qu'elle reparte.
            if (idle < TimeSpan.FromMinutes(rule.Trigger.Minutes)) IdleFired.Remove(rule.Id);
            else if (IdleFired.Add(rule.Id)) Run(rule);
        }
    }

    internal static bool TimeMatches(AutomationTrigger trigger, DateTime now) =>
        trigger.Time == now.ToString("HH:mm") && (trigger.Days.Count == 0 || trigger.Days.Contains(now.DayOfWeek));

    private static void OnProcesses()
    {
        var now = RunningNames();
        var before = _running ?? now;
        _running = now;
        var started = now.Except(before).ToList();
        var closed = before.Except(now).ToList();
        if (started.Count > 0) Fire(TriggerKind.AppStarted, trigger => started.Contains(ProcessName(trigger.App)));
        if (closed.Count > 0) Fire(TriggerKind.AppClosed, trigger => closed.Contains(ProcessName(trigger.App)));
    }

    /// <summary>« C:\…\Discord.exe », « Discord.exe » ou « Discord » → « discord » (nom de processus).</summary>
    internal static string ProcessName(string? app) =>
        string.IsNullOrWhiteSpace(app) ? "" : Path.GetFileNameWithoutExtension(app.Trim()).ToLowerInvariant();

    private static HashSet<string> RunningNames()
    {
        var processes = Process.GetProcesses();
        var names = processes.Select(p => p.ProcessName.ToLowerInvariant()).ToHashSet();
        foreach (var process in processes) process.Dispose();
        return names;
    }

    // ------------------------------------------------------------------ exécution

    private static void Fire(TriggerKind kind, Func<AutomationTrigger, bool> matches)
    {
        foreach (var rule in Active.Where(r => r.Trigger.Kind == kind && matches(r.Trigger)).ToList()) Run(rule);
    }

    /// <summary>Fait les actions de la règle (aussi « Essayer » dans l'éditeur, même désactivée).</summary>
    public static void Run(AutomationRule rule)
    {
        var now = DateTime.UtcNow;
        if (LastRun.TryGetValue(rule.Id, out var last) && now - last < Cooldown) return;
        LastRun[rule.Id] = now;
        int done = 0;
        foreach (var action in rule.Actions)
        {
            try
            {
                if (Do(action)) done++;
            }
            catch (Exception ex)
            {
                Log.Error($"Automatisation « {rule.Name} » : {action.Kind}", ex);
            }
        }
        Log.Info($"Automatisation « {rule.Name} » ({rule.Trigger.Kind}) : {done} / {rule.Actions.Count} action(s)");
        if (done > 0 && !rule.Actions.Any(a => a.Kind == ActionKind.Notice))
            _island()?.ShowNotice(Icons.Automations, rule.Name.Length > 0 ? rule.Name : Loc.T("automations.untitled"), duration: TimeSpan.FromSeconds(2.5),
                                  detail: Loc.T("automations.notice.detail"));
        Ran?.Invoke(rule);
    }

    /// <summary>Une action ; faux si elle n'a rien fait (pas encore choisie, sortie audio débranchée…).</summary>
    private static bool Do(AutomationAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.Profile:
                if (action.Value == null || ProfileService.All.All(p => p.Id != action.Value)) return false;
                ProfileService.Switch(action.Value);
                return true;
            case ActionKind.AudioOutput:
                return action.Value != null && AudioOutputService.SetDefault(action.Value);
            case ActionKind.MuteMic:
            case ActionKind.UnmuteMic:
                // Comme la télécommande : le micro de Discord s'il est relié, sinon tous les micros de Windows.
                bool mute = action.Kind == ActionKind.MuteMic;
                if (DiscordVoice.IsConnected) _ = DiscordVoice.SetVoiceAsync(mute: mute);
                else VolumeService.MuteAllMicrophones(mute);
                return true;
            case ActionKind.LaunchApp:
                if (action.Value == null) return false;
                if (File.Exists(action.Value)) Dock.DockItem.FromFile(action.Value).Launch(); // exe, raccourci .lnk / .url
                else new AppEntry(action.Label ?? action.Value, action.Value).Launch();    // appli du menu Démarrer (Store comprise)
                return true;
            case ActionKind.OpenFolder:
                if (action.Value == null || !Directory.Exists(action.Value)) return false;
                Process.Start(new ProcessStartInfo(action.Value) { UseShellExecute = true });
                return true;
            case ActionKind.Timer:
                if (action.Minutes <= 0 || _island() is not { } island) return false;
                island.StartTimer(TimeSpan.FromMinutes(action.Minutes));
                return true;
            case ActionKind.Wallpaper:
                if (action.Value == null || !File.Exists(action.Value)) return false;
                WallpaperService.Apply(action.Value);
                return true;
            case ActionKind.DoNotDisturbOn:
                FocusService.TurnOn(action.Minutes > 0 ? TimeSpan.FromMinutes(action.Minutes) : null);
                return true;
            case ActionKind.DoNotDisturbOff:
                FocusService.Stop();
                return true;
            case ActionKind.Notice:
                if (string.IsNullOrWhiteSpace(action.Value)) return false;
                return _island()?.ShowNotice(Icons.Automations, action.Value.Trim(), duration: TimeSpan.FromSeconds(5)) == true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ exemples

    /// <summary>
    /// Une seule fois : « Quand un jeu se lance → profil Gaming + casque + Ne pas déranger » et son inverse, DÉSACTIVÉS (la
    /// sortie audio est à choisir : on ne devine pas lequel est le casque).
    /// </summary>
    private static void AddExamplesOnce()
    {
        if (SettingsStore.IsReadOnly || SettingsStore.Current.Automations.ExamplesAdded) return;
        SettingsStore.Update(s =>
        {
            s.Automations.ExamplesAdded = true;
            s.Automations.Rules.AddRange(Examples());
        });
    }

    internal static List<AutomationRule> Examples()
    {
        var gaming = ProfileService.All.FirstOrDefault(p => p.Id == "gaming");
        var home = ProfileService.All.FirstOrDefault(p => p.Id == "perso");
        var start = new AutomationRule { Name = Loc.T("automations.example.game"), Trigger = new AutomationTrigger { Kind = TriggerKind.GameStarted } };
        if (gaming != null) start.Actions.Add(new AutomationAction { Kind = ActionKind.Profile, Value = gaming.Id, Label = gaming.Name });
        start.Actions.Add(new AutomationAction { Kind = ActionKind.AudioOutput });
        start.Actions.Add(new AutomationAction { Kind = ActionKind.DoNotDisturbOn });
        var end = new AutomationRule { Name = Loc.T("automations.example.game_end"), Trigger = new AutomationTrigger { Kind = TriggerKind.GameEnded } };
        if (home != null) end.Actions.Add(new AutomationAction { Kind = ActionKind.Profile, Value = home.Id, Label = home.Name });
        end.Actions.Add(new AutomationAction { Kind = ActionKind.AudioOutput });
        end.Actions.Add(new AutomationAction { Kind = ActionKind.DoNotDisturbOff });
        return [start, end];
    }

    // ------------------------------------------------------------------ inactivité

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    private static TimeSpan IdleFor()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }
}
