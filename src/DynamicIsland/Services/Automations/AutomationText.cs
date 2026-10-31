using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services.Automations;

/// <summary>Textes et icônes des règles (7D) : « Quand un jeu se lance → profil Gaming · Ne pas déranger ».</summary>
public static class AutomationText
{
    public static string Key(TriggerKind kind) => kind.ToString().ToLowerInvariant();
    public static string Key(ActionKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>Nom court du déclencheur (cartes à choisir dans l'éditeur).</summary>
    public static string TriggerName(TriggerKind kind) => Loc.T($"automations.trigger.{Key(kind)}");

    /// <summary>Nom très court, pour les petites cartes de l'éditeur (« Jeu lancé »).</summary>
    public static string TriggerShortName(TriggerKind kind) => Loc.T($"automations.trigger.short.{Key(kind)}");

    public static string ActionName(ActionKind kind) => Loc.T($"automations.action.{Key(kind)}");

    public static string TriggerGlyph(TriggerKind kind) => kind switch
    {
        TriggerKind.GameStarted or TriggerKind.GameEnded => Icons.Gamepad,
        TriggerKind.AppStarted or TriggerKind.AppClosed => Icons.AppWindow,
        TriggerKind.Time => Icons.Timer,
        TriggerKind.DeviceConnected => Icons.Usb,
        TriggerKind.Idle => Icons.DarkMode,
        _ => Icons.Power,
    };

    public static string ActionGlyph(ActionKind kind) => kind switch
    {
        ActionKind.Profile => Icons.Profiles,
        ActionKind.AudioOutput => Icons.Headphones,
        ActionKind.MuteMic => Icons.MicOff,
        ActionKind.UnmuteMic => Icons.Voice,
        ActionKind.LaunchApp => Icons.Play,
        ActionKind.OpenFolder => Icons.FolderOpen,
        ActionKind.Timer => Icons.Timer,
        ActionKind.Wallpaper => Icons.Picture,
        ActionKind.DoNotDisturbOn => Icons.BellOff,
        ActionKind.DoNotDisturbOff => Icons.Bell,
        _ => Icons.Message,
    };

    /// <summary>« Quand … » complet, avec ses réglages (« Discord se lance », « À 22:30 en semaine »).</summary>
    public static string Describe(AutomationTrigger trigger) => trigger.Kind switch
    {
        TriggerKind.AppStarted => trigger.App is { } app ? Loc.F("automations.when.app_started", AppLabel(trigger)) : Missing(TriggerName(trigger.Kind)),
        TriggerKind.AppClosed => trigger.App is { } app2 ? Loc.F("automations.when.app_closed", AppLabel(trigger)) : Missing(TriggerName(trigger.Kind)),
        TriggerKind.Time => trigger.Time is { } time ? Loc.F("automations.when.time", time, Days(trigger.Days)) : Missing(TriggerName(trigger.Kind)),
        TriggerKind.DeviceConnected => Loc.F("automations.when.device", Loc.T($"automations.device.{trigger.Device.ToString().ToLowerInvariant()}"))
                                       + (string.IsNullOrWhiteSpace(trigger.DeviceName) ? "" : $" « {trigger.DeviceName.Trim()} »"),
        TriggerKind.Idle => Loc.F("automations.when.idle", trigger.Minutes),
        _ => Loc.T($"automations.when.{Key(trigger.Kind)}"),
    };

    /// <summary>Une action en quelques mots (« Profil Gaming », « Sortie audio : Casque », « Minuteur 25 min »).</summary>
    public static string Describe(AutomationAction action) => action.Kind switch
    {
        ActionKind.Profile => action.Value == null ? Missing(ActionName(action.Kind)) : Loc.F("automations.do.profile", Label(action)),
        ActionKind.AudioOutput => action.Value == null ? Missing(ActionName(action.Kind)) : Loc.F("automations.do.audiooutput", Label(action)),
        ActionKind.LaunchApp => action.Value == null ? Missing(ActionName(action.Kind)) : Loc.F("automations.do.launchapp", Label(action)),
        ActionKind.OpenFolder => action.Value is { } folder ? Loc.F("automations.do.openfolder", System.IO.Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder)
                                                            : Missing(ActionName(action.Kind)),
        ActionKind.Wallpaper => action.Value is { } image ? Loc.F("automations.do.wallpaper", System.IO.Path.GetFileName(image)) : Missing(ActionName(action.Kind)),
        ActionKind.Timer => action.Minutes > 0 ? Loc.F("automations.do.timer", action.Minutes) : Missing(ActionName(action.Kind)),
        ActionKind.DoNotDisturbOn => action.Minutes > 0 ? Loc.F("automations.do.dnd_for", Duration(action.Minutes)) : Loc.T("automations.do.dnd_on"),
        ActionKind.Notice => string.IsNullOrWhiteSpace(action.Value) ? Missing(ActionName(action.Kind)) : Loc.F("automations.do.notice", action.Value.Trim()),
        _ => ActionName(action.Kind),
    };

    /// <summary>Une action dont la valeur reste à choisir (sortie audio des exemples…) : sautée tant qu'elle n'est pas choisie.</summary>
    public static bool NeedsChoice(AutomationAction action) => action.Kind switch
    {
        ActionKind.Profile or ActionKind.AudioOutput or ActionKind.LaunchApp or ActionKind.OpenFolder or ActionKind.Wallpaper or ActionKind.Notice
            => string.IsNullOrWhiteSpace(action.Value),
        ActionKind.Timer => action.Minutes <= 0,
        _ => false,
    };

    public static bool NeedsChoice(AutomationTrigger trigger) => trigger.Kind switch
    {
        TriggerKind.AppStarted or TriggerKind.AppClosed => string.IsNullOrWhiteSpace(trigger.App),
        TriggerKind.Time => trigger.Time == null,
        _ => false,
    };

    /// <summary>« 30 min », « 1 h », « 1 h 30 ».</summary>
    public static string Duration(int minutes) =>
        minutes < 60 ? Loc.F("automations.minutes", minutes) : minutes % 60 == 0 ? Loc.F("automations.hours", minutes / 60) : Loc.F("automations.hours_minutes", minutes / 60, minutes % 60);

    /// <summary>Jours choisis : « tous les jours », « en semaine », « le week-end », ou « lun., mer. ».</summary>
    public static string Days(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count is 0 or 7) return Loc.T("automations.days.all");
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (days.Count == 5 && weekdays.All(days.Contains)) return Loc.T("automations.days.weekdays");
        if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday)) return Loc.T("automations.days.weekend");
        var names = Loc.Culture.DateTimeFormat.AbbreviatedDayNames;
        return string.Join(", ", WeekOrder.Where(days.Contains).Select(d => names[(int)d]));
    }

    /// <summary>Lundi d'abord.</summary>
    public static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static string AppLabel(AutomationTrigger trigger) =>
        trigger.AppName is { Length: > 0 } name ? name : System.IO.Path.GetFileNameWithoutExtension(trigger.App ?? "");

    private static string Label(AutomationAction action) => action.Label ?? action.Value ?? "";

    private static string Missing(string what) => Loc.F("automations.to_choose", what);
}
