using System.Text.Json;
using DynamicIsland.Services.Automations;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des automatisations (7D) : règles (lecture / écriture, bornes), déclencheurs (heure et jours, nom d'appli, type
/// d'appareil), exemples créés désactivés, textes des règles, sorties audio (LECTURE seule : rien n'est changé) et « Ne pas
/// déranger » (en mémoire : les réglages de l'auto-test ne sont jamais écrits).
/// </summary>
internal static partial class SelfTest
{
    private static void Automations(List<string> lines)
    {
        var rule = new AutomationRule
        {
            Name = "  Essai  ",
            Enabled = true,
            Trigger = new AutomationTrigger { Kind = TriggerKind.Time, Time = "07:30", Days = [DayOfWeek.Monday, DayOfWeek.Monday, DayOfWeek.Friday], Minutes = 9999 },
            Actions = [.. Enumerable.Range(0, 14).Select(_ => new AutomationAction { Kind = ActionKind.Timer, Minutes = 99999 })],
        };
        rule.Normalize();
        var copy = JsonSerializer.Deserialize<AutomationRule>(JsonSerializer.Serialize(rule))!;
        Check(lines, rule.Name == "Essai" && rule.Trigger.Days.Count == 2 && rule.Trigger.Minutes == 600 && rule.Actions.Count == AutomationRule.MaxActions
                     && rule.Actions[0].Minutes == 24 * 60 && copy.Trigger.Time == "07:30" && copy.Actions.Count == rule.Actions.Count && copy.Id == rule.Id,
            "règles : bornes (10 actions, durées), jours sans doublon, aller-retour JSON identique");
        var badTime = new AutomationTrigger { Kind = TriggerKind.Time, Time = "25:99" };
        badTime.Normalize();
        Check(lines, badTime.Time == null && AutomationText.NeedsChoice(badTime), "heure invalide refusée (« à choisir »)");

        var monday = new DateTime(2026, 10, 26, 7, 30, 0); // un lundi
        Check(lines, AutomationService.TimeMatches(rule.Trigger, monday) && !AutomationService.TimeMatches(rule.Trigger, monday.AddDays(1))
                     && !AutomationService.TimeMatches(rule.Trigger, monday.AddMinutes(1))
                     && AutomationService.TimeMatches(new AutomationTrigger { Kind = TriggerKind.Time, Time = "07:30" }, monday.AddDays(1)),
            "heure précise : 07:30 le lundi et le vendredi seulement ; sans jour coché = tous les jours");
        Check(lines, AutomationService.ProcessName(@"C:\Program Files\Discord\Discord.exe") == "discord" && AutomationService.ProcessName("Spotify") == "spotify"
                     && AutomationService.ProcessName(" chrome.EXE ") == "chrome" && AutomationService.ProcessName(null) == "",
            "appli : chemin, « Discord.exe » ou « Discord » → même nom de processus");

        var headset = new DeviceNotifier.Arrival(DeviceNotifier.Kind.Audio, "Arctis 7 Wireless", 80, null);
        var pad = new DeviceNotifier.Arrival(DeviceNotifier.Kind.Controller, "Manette Xbox", null, null);
        var usb = new DeviceNotifier.Arrival(DeviceNotifier.Kind.Storage, "SanDisk", null, "E:\\");
        AutomationTrigger Device(DeviceFilter filter, string? name = null) => new() { Kind = TriggerKind.DeviceConnected, Device = filter, DeviceName = name };
        Check(lines, AutomationService.DeviceMatches(Device(DeviceFilter.Headset), headset) && !AutomationService.DeviceMatches(Device(DeviceFilter.Headset), pad)
                     && AutomationService.DeviceMatches(Device(DeviceFilter.Controller), pad) && AutomationService.DeviceMatches(Device(DeviceFilter.Storage), usb)
                     && AutomationService.DeviceMatches(Device(DeviceFilter.Any, "arctis"), headset) && !AutomationService.DeviceMatches(Device(DeviceFilter.Any, "arctis"), usb),
            "appareil branché : casque, manette, clé USB, et morceau du nom (« arctis »)");

        var examples = AutomationService.Examples();
        Check(lines, examples.Count == 2 && examples.All(r => !r.Enabled) && examples[0].Trigger.Kind == TriggerKind.GameStarted
                     && examples[0].Actions.Any(a => a.Kind == ActionKind.DoNotDisturbOn) && examples[0].Actions.Any(a => a.Kind == ActionKind.AudioOutput && AutomationText.NeedsChoice(a))
                     && examples[1].Trigger.Kind == TriggerKind.GameEnded,
            "exemple « jeu lancé → profil Gaming + casque (à choisir) + Ne pas déranger » et son inverse, créés DÉSACTIVÉS");

        var texts = Enum.GetValues<TriggerKind>().Select(k => AutomationText.Describe(new AutomationTrigger { Kind = k, App = "Discord", Time = "22:00" }))
            .Concat(Enum.GetValues<ActionKind>().Select(k => AutomationText.Describe(new AutomationAction { Kind = k, Value = "x", Label = "X", Minutes = 30 })))
            .Concat(Enum.GetValues<ActionKind>().Select(k => AutomationText.Describe(new AutomationAction { Kind = k })))
            .ToList();
        Check(lines, texts.All(t => t.Length > 0 && !t.Contains('{') && !t.StartsWith("automations.")),
            $"textes des règles : {texts.Count} phrases sans clé ni « {{0}} » oublié (ex. « {AutomationText.Describe(examples[0].Trigger)} → {string.Join(" · ", examples[0].Actions.Select(AutomationText.Describe))} »)");
        Check(lines, AutomationText.Days([]) == Loc.T("automations.days.all") && AutomationText.Duration(90).Contains('3') && AutomationText.Duration(60) == Loc.F("automations.hours", 1),
            "jours et durées (« tous les jours », « 1 h 30 »)");

        var outputs = AudioOutputService.List();
        Check(lines, outputs.Count == 0 || outputs.Count(o => o.IsDefault) == 1 && outputs.All(o => o.Name.Length > 0 && o.Id.Length > 0),
            $"sorties audio (lecture seule) : {outputs.Count} trouvée(s), par défaut « {outputs.FirstOrDefault(o => o.IsDefault)?.Name ?? "—"} »");

        bool before = FocusService.DoNotDisturb;
        FocusService.TurnOn(TimeSpan.FromMinutes(10));
        bool on = FocusService.DoNotDisturb && FocusService.Until is { } until && until > DateTime.Now.AddMinutes(9);
        FocusService.Stop();
        Check(lines, !before && on && !FocusService.DoNotDisturb && FocusService.Until == null,
            "Ne pas déranger : activé 10 min puis arrêté (en mémoire, rien n'est écrit)");
    }
}
