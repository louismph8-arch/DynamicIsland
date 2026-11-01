using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services.NightLight;

/// <summary>
/// Veilleuse d'Opaline (7F) : moins de lumière bleue grâce à la rampe gamma de chaque écran (<c>SetDeviceGammaRamp</c>, API
/// documentée ; on n'écrit JAMAIS l'état non documenté de la veilleuse de Windows). Désactivée par défaut. Toujours réversible :
/// 1. la rampe d'origine de chaque écran est écrite dans <c>veilleuse.txt</c> AVANT de la changer (<see cref="GammaStore"/>) ;
/// 2. intensité revenue à 0, option coupée, module « nightlight » arrêté, fermeture, plantage, fin de session : <see cref="Restore"/> ;
/// 3. le gardien, --quit, le lancement suivant (<see cref="ShellGuard.RecoverAll"/>) et la désinstallation : <see cref="RecoverIfNeeded"/>.
/// Changement d'écrans : tout est remis puis refait (rampes d'origine relues). Programmation : toujours, du coucher au lever du
/// soleil (ville de la météo), ou heures fixes ; vérifiée chaque minute seulement quand une programmation est choisie.
/// Transition douce (1 min au passage programmé, quelques secondes sinon), écrite seulement pendant la transition.
/// </summary>
public static class NightLightService
{
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan ScheduledTransition = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ManualTransition = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan GameTransition = TimeSpan.FromSeconds(0.8);

    private static GammaStore Store => new(AppPaths.NightLightState);
    private static DispatcherTimer? _step, _clock;
    private static bool _started;
    /// <summary>Rampes changées par CE processus (seul lui les remet en partant ; le gardien et le lancement suivant lisent le fichier).</summary>
    private static bool _modifiedHere;
    private static double _applied, _from, _target;
    private static DateTime _startAt;
    private static TimeSpan _length;
    private static bool? _override, _lastSchedule;
    /// <summary>Intensité maximale qu'un écran accepte (limite de Windows, ou refus constaté), par nom d'écran.</summary>
    private static readonly Dictionary<string, double> Caps = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _hdr = [];

    /// <summary>État ou réglage changé (thread de l'interface) : la tuile et la page des paramètres se mettent à jour.</summary>
    public static event Action? Changed;

    /// <summary>Ce qui s'est passé à la dernière application (pour le dire dans les paramètres).</summary>
    public sealed record Report(IReadOnlyList<string> HdrScreens, int? LimitedTo, bool Failed);

    public static Report LastReport { get; private set; } = new([], null, false);

    /// <summary>Veilleuse allumée (ou en train de s'allumer).</summary>
    public static bool IsOn => _target > 0;

    /// <summary>Intensité visée en ce moment (0–1).</summary>
    public static double Target => _target;

    private static NightLightSettings Settings => SettingsStore.Current.Focus.NightLight;

    private static bool Allowed => _started && !App.SafeMode && !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.NightLight);

    public static void Start()
    {
        if (_started) return;
        _started = true;
        SettingsStore.Changed += OnSettingsChanged;
        GamingMode.Changed += OnGaming;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.TimeChanged += OnTime;
        Evaluate(ManualTransition);
    }

    /// <summary>Module arrêté (ou fin de l'app) : plus rien ne tourne et les écrans reviennent comme avant.</summary>
    public static void Stop()
    {
        if (_started)
        {
            SettingsStore.Changed -= OnSettingsChanged;
            GamingMode.Changed -= OnGaming;
            SystemEvents.DisplaySettingsChanged -= OnDisplays;
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.TimeChanged -= OnTime;
        }
        _started = false;
        Restore();
    }

    /// <summary>Remet tout de suite les rampes d'origine (peut être appelé plusieurs fois, depuis n'importe quel filet de sécurité).</summary>
    public static void Restore()
    {
        _step?.Stop();
        _clock?.Stop();
        bool was = _applied > 0 || _target > 0;
        _applied = _target = _from = 0;
        if (_modifiedHere) RecoverIfNeeded(); // un rendu --snapshot ne touche jamais aux écrans de l'app qui tourne
        _modifiedHere = false;
        if (was)
        {
            Log.Info("Veilleuse : écrans remis comme avant");
            Changed?.Invoke();
        }
    }

    /// <summary>Lancement, gardien, --quit, désinstallation : une session a changé des rampes sans les remettre.</summary>
    public static void RecoverIfNeeded()
    {
        var store = Store;
        if (!store.HasState) return;
        if (!store.RestoreAll(GdiGammaDevice.All())) Log.Info("Veilleuse : un écran absent garde sa rampe d'origine pour plus tard");
    }

    /// <summary>
    /// Tuile de l'île / palette : éteinte → allumée (option activée si besoin) ; allumée → éteinte. Avec une programmation,
    /// le choix vaut jusqu'au prochain passage programmé.
    /// </summary>
    public static void Toggle()
    {
        var settings = Settings;
        if (!settings.Enabled)
        {
            _override = settings.Schedule == NightLightSchedule.Always ? null : true;
            SettingsStore.Update(s => s.Focus.NightLight.Enabled = true);
            return;
        }
        if (IsOn && settings.Schedule == NightLightSchedule.Always)
        {
            _override = null;
            SettingsStore.Update(s => s.Focus.NightLight.Enabled = false);
            return;
        }
        _override = !IsOn;
        Evaluate(ManualTransition);
    }

    // ------------------------------------------------------------------ décisions (pures, vérifiées par --self-test)

    /// <summary>Heure dans la plage [début, fin[ (la plage peut passer minuit : 21:00 → 07:00). Début = fin : jamais.</summary>
    internal static bool InRange(TimeOnly time, TimeOnly from, TimeOnly to) =>
        from == to ? false : from < to ? time >= from && time < to : time >= from || time < to;

    /// <summary>La programmation dit-elle « allumée » maintenant ? Soleil sans ville connue : 21 h → 7 h.</summary>
    internal static bool ScheduleOn(NightLightSchedule schedule, string from, string to, DateTime now, (double Latitude, double Longitude)? place)
    {
        var time = TimeOnly.FromDateTime(now);
        return schedule switch
        {
            NightLightSchedule.Hours => InRange(time, Parse(from, 21), Parse(to, 7)),
            NightLightSchedule.Sun => place is { } p ? !SunCalculator.IsDay(now, p.Latitude, p.Longitude) : InRange(time, new(21, 0), new(7, 0)),
            _ => true,
        };
    }

    /// <summary>Allumée ? Option active, pas en pause de jeu, puis le choix manuel s'il y en a un, sinon la programmation.</summary>
    internal static bool Decide(bool enabled, bool scheduleOn, bool? manual, bool gaming, bool pauseInGames) =>
        enabled && !(gaming && pauseInGames) && (manual ?? scheduleOn);

    private static TimeOnly Parse(string text, int fallbackHour) =>
        TimeOnly.TryParseExact(text ?? "", "HH:mm", out var time) ? time : new TimeOnly(fallbackHour, 0);

    // ------------------------------------------------------------------ événements

    private static void OnSettingsChanged() => Evaluate(ManualTransition);

    private static void OnGaming() => Evaluate(GameTransition);

    private static void OnTime(object? sender, EventArgs e) => Guarded.OnUi(() => Evaluate(ManualTransition), "Veilleuse : heure");

    private static void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // Sortie de veille : le pilote a pu remettre sa rampe ; l'heure a pu passer la programmation.
        Guarded.OnUi(() =>
        {
            if (_applied > 0) ApplyNow(_applied, final: true);
            Evaluate(ManualTransition);
        }, "Veilleuse : sortie de veille");
    }

    private static void OnDisplays(object? sender, EventArgs e) => Guarded.OnUi(() =>
    {
        // Écrans changés : tout est remis (rampes d'origine), puis refait sur les écrans actuels avec leurs rampes relues.
        Caps.Clear();
        _hdr = DisplayFacts.HdrScreens();
        double applied = _applied;
        if (_modifiedHere) RecoverIfNeeded();
        _applied = 0;
        if (!Allowed) return;
        if (applied > 0) ApplyNow(applied, final: true);
        Evaluate(ManualTransition);
    }, "Veilleuse : écrans");

    // ------------------------------------------------------------------ application

    /// <summary>Recalcule ce qu'il faut et démarre la transition vers la nouvelle intensité.</summary>
    public static void Evaluate(TimeSpan transition)
    {
        if (!Allowed)
        {
            if (_applied > 0 || _target > 0 || _modifiedHere) Restore();
            return;
        }
        var settings = Settings;
        var place = CityLocation.Get() is { } city ? (city.Latitude, city.Longitude) : ((double, double)?)null;
        bool scheduleOn = ScheduleOn(settings.Schedule, settings.From, settings.To, DateTime.Now, place);
        bool flipped = _lastSchedule is { } last && last != scheduleOn;
        if (flipped || !settings.Enabled) _override = null; // passage programmé : le choix manuel ne vaut plus
        _lastSchedule = scheduleOn;

        bool on = Decide(settings.Enabled, scheduleOn, _override, GamingMode.IsActive, settings.PauseInGames);
        double target = on ? settings.Strength / 100.0 : 0;
        UpdateClock(settings);
        if (Math.Abs(target - _target) < 0.001) return;
        bool turningOn = _target <= 0 && target > 0;
        StartTransition(target, flipped ? ScheduledTransition : transition);
        if (turningOn) Log.Info($"Veilleuse : allumée ({settings.Strength} %, {GammaRamp.Kelvin(target):0} K)");
        else if (target <= 0) Log.Info("Veilleuse : éteinte");
        Changed?.Invoke();
    }

    private static void UpdateClock(NightLightSettings settings)
    {
        bool needed = settings.Enabled && settings.Schedule != NightLightSchedule.Always;
        if (!needed)
        {
            _clock?.Stop();
            return;
        }
        if (_clock == null)
        {
            _clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _clock.Tick += (_, _) => Evaluate(ManualTransition);
        }
        _clock.Start();
    }

    private static void StartTransition(double target, TimeSpan length)
    {
        _from = _applied;
        _target = target;
        _startAt = DateTime.UtcNow;
        _length = length;
        if (_step == null)
        {
            _step = new DispatcherTimer { Interval = StepInterval };
            _step.Tick += (_, _) => Guarded.Run(StepOnce, "Veilleuse : transition");
        }
        _hdr = DisplayFacts.HdrScreens();
        StepOnce();
        if (_applied != _target) _step.Start();
    }

    private static void StepOnce()
    {
        if (!Allowed)
        {
            Restore();
            return;
        }
        double progress = _length <= TimeSpan.Zero ? 1 : Math.Clamp((DateTime.UtcNow - _startAt) / _length, 0, 1);
        double eased = progress * progress * (3 - 2 * progress); // départ et arrivée en douceur
        bool final = progress >= 1;
        ApplyNow(final ? _target : _from + (_target - _from) * eased, final);
        if (final) _step?.Stop();
    }

    /// <summary>Écrit l'intensité sur chaque écran (0 = rampes d'origine remises et fichier d'état effacé).</summary>
    private static void ApplyNow(double strength, bool final)
    {
        var devices = GdiGammaDevice.All();
        if (strength <= 0.001)
        {
            Store.RestoreAll(devices);
            _applied = 0;
            if (final) Publish(new Report(HdrNames(), null, false));
            return;
        }
        var store = Store;
        int? limited = null;
        bool failed = false;
        foreach (var device in devices)
        {
            if (store.EnsureOriginal(device) is not { } original)
            {
                failed = true;
                continue;
            }
            if (!Caps.TryGetValue(device.Name, out double cap)) Caps[device.Name] = cap = GammaRamp.MaxStrength(original);
            double effective = Math.Min(strength, cap);
            bool written = false;
            for (int attempt = 0; attempt < 5 && !written; attempt++)
            {
                written = device.TryWrite(GammaRamp.Build(original, effective));
                if (written) break;
                effective = Math.Floor(effective * 75) / 100; // Windows refuse : on réduit et on retient la limite
                Caps[device.Name] = effective;
            }
            if (written) _modifiedHere = true;
            if (!written) failed = true;
            else if (effective < strength - 0.005) limited = Math.Min(limited ?? 100, (int)Math.Round(effective * 100));
        }
        _applied = strength;
        if (final) Publish(new Report(HdrNames(), limited, failed));
    }

    private static List<string> HdrNames() =>
        ScreenInfo.All().Where(m => _hdr.Contains(m.Device)).Select(m => m.Name.Length > 0 ? m.Name : m.Device).ToList();

    private static void Publish(Report report)
    {
        bool changed = report.LimitedTo != LastReport.LimitedTo || report.Failed != LastReport.Failed
                       || !report.HdrScreens.SequenceEqual(LastReport.HdrScreens);
        LastReport = report;
        if (report.Failed && changed) Log.Info("Veilleuse : un écran refuse la rampe gamma");
        if (changed) Changed?.Invoke();
    }

    // ------------------------------------------------------------------ textes

    /// <summary>« 4 600 K », « Dès 21:00 », « Au coucher du soleil », « En pause (jeu) », « Désactivée ».</summary>
    public static string StateText()
    {
        var settings = Settings;
        if (IsOn) return Loc.F("nightlight.state.kelvin", Math.Round(GammaRamp.Kelvin(_target) / 100) * 100);
        if (!settings.Enabled) return Loc.T("nightlight.state.off");
        if (GamingMode.IsActive && settings.PauseInGames) return Loc.T("nightlight.state.paused");
        return settings.Schedule switch
        {
            NightLightSchedule.Hours => Loc.F("nightlight.state.from", settings.From),
            NightLightSchedule.Sun => Loc.T("nightlight.state.sunset"),
            _ => Loc.T("nightlight.state.off"),
        };
    }
}
