namespace DynamicIsland.Settings;

/// <summary>Déclencheur d'une règle « Quand… » (7D).</summary>
public enum TriggerKind { GameStarted, GameEnded, AppStarted, AppClosed, Time, DeviceConnected, Idle, Resume }

/// <summary>Action d'une règle « … alors » (7D), faite dans l'ordre de la liste.</summary>
public enum ActionKind { Profile, AudioOutput, MuteMic, UnmuteMic, LaunchApp, OpenFolder, Timer, Wallpaper, DoNotDisturbOn, DoNotDisturbOff, Notice }

/// <summary>Appareil branché : n'importe lequel, un casque / des écouteurs, une manette, ou une clé / un disque USB.</summary>
public enum DeviceFilter { Any, Headset, Controller, Storage }

public sealed class AutomationTrigger
{
    public TriggerKind Kind { get; set; } = TriggerKind.GameStarted;
    /// <summary>Appli lancée / fermée : nom de l'exe (« Discord.exe »), comparé sans tenir compte des majuscules.</summary>
    public string? App { get; set; }
    /// <summary>Nom affiché de l'appli (« Discord »).</summary>
    public string? AppName { get; set; }
    /// <summary>Heure précise « HH:mm » et jours (vide = tous les jours).</summary>
    public string? Time { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    public DeviceFilter Device { get; set; }
    /// <summary>Morceau du nom de l'appareil (facultatif, ex. « Arctis »).</summary>
    public string? DeviceName { get; set; }
    /// <summary>PC inactif : minutes sans clavier ni souris.</summary>
    public int Minutes { get; set; } = 10;

    public void Normalize()
    {
        Days = (Days ?? []).Distinct().ToList();
        Minutes = Math.Clamp(Minutes, 1, 600);
        if (Time != null && !TimeSpan.TryParseExact(Time, @"hh\:mm", null, out _)) Time = null;
        App = string.IsNullOrWhiteSpace(App) ? null : App.Trim();
    }
}

public sealed class AutomationAction
{
    public ActionKind Kind { get; set; }
    /// <summary>
    /// Profil (nom), sortie audio (identifiant Windows), appli (chemin, raccourci ou AppUserModelID), dossier, image du fond
    /// d'écran, message de l'île. null = pas encore choisi : l'action est sautée (et signalée dans l'éditeur).
    /// </summary>
    public string? Value { get; set; }
    /// <summary>Nom affiché (sortie audio, appli).</summary>
    public string? Label { get; set; }
    /// <summary>Minuteur : durée ; Ne pas déranger : durée (0 = jusqu'à ce qu'on l'enlève).</summary>
    public int Minutes { get; set; }

    public void Normalize() => Minutes = Math.Clamp(Minutes, 0, 24 * 60);
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>Une règle ne fait rien tant qu'elle n'est pas activée (les exemples sont créés désactivés).</summary>
    public bool Enabled { get; set; }
    public AutomationTrigger Trigger { get; set; } = new();
    public List<AutomationAction> Actions { get; set; } = [];

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..8] : Id;
        Name = (Name ?? "").Trim();
        if (Name.Length > 60) Name = Name[..60];
        Trigger ??= new AutomationTrigger();
        Trigger.Normalize();
        Actions = (Actions ?? []).Where(a => a != null).Take(MaxActions).ToList();
        foreach (var action in Actions) action.Normalize();
    }

    public const int MaxActions = 10;
}

/// <summary>Automatisations « Quand… alors… » (7D) : comme Raccourcis sur macOS, page Paramètres → Automatisations.</summary>
public sealed class AutomationSettings
{
    public List<AutomationRule> Rules { get; set; } = [];
    /// <summary>Les exemples (« Quand un jeu se lance… ») ont été proposés une fois : on ne les recrée pas s'ils sont supprimés.</summary>
    public bool ExamplesAdded { get; set; }

    public void Normalize()
    {
        Rules = (Rules ?? []).Where(r => r != null).Take(MaxRules).ToList();
        foreach (var rule in Rules) rule.Normalize();
    }

    public const int MaxRules = 50;
}

/// <summary>Concentration (7D / 7F) : « Ne pas déranger » d'Opaline.</summary>
public sealed class FocusSettings
{
    /// <summary>Fin du « Ne pas déranger » ; DateTime.MaxValue = jusqu'à ce qu'on l'enlève ; null = arrêté.</summary>
    public DateTime? DoNotDisturbUntil { get; set; }

    /// <summary>Veilleuse d'Opaline (7F) : lumière bleue réduite par la rampe gamma des écrans (désactivée par défaut).</summary>
    public NightLightSettings NightLight { get; set; } = new();

    public void Normalize()
    {
        NightLight ??= new NightLightSettings();
        NightLight.Normalize();
    }
}

public enum NightLightSchedule { Always, Sun, Hours }

/// <summary>Veilleuse (7F) : rien n'est touché tant que <see cref="Enabled"/> est faux.</summary>
public sealed class NightLightSettings
{
    /// <summary>Veilleuse en marche (selon la programmation) ; faux par défaut.</summary>
    public bool Enabled { get; set; }
    /// <summary>Toujours (dès qu'elle est activée), du coucher au lever du soleil, ou heures fixes.</summary>
    public NightLightSchedule Schedule { get; set; } = NightLightSchedule.Always;
    public string From { get; set; } = "21:00";
    public string To { get; set; } = "07:00";
    /// <summary>Intensité 0–100 % : 0 = 6500 K (rien ne change), 100 = 2700 K (très chaud).</summary>
    public int Strength { get; set; } = 50;
    /// <summary>Coupée pendant les jeux (couleurs fidèles) ; activé par défaut.</summary>
    public bool PauseInGames { get; set; } = true;

    public void Normalize()
    {
        Strength = Math.Clamp(Strength, 0, 100);
        if (!Enum.IsDefined(Schedule)) Schedule = NightLightSchedule.Always;
        if (!TimeOnly.TryParseExact(From ?? "", "HH:mm", out _)) From = "21:00";
        if (!TimeOnly.TryParseExact(To ?? "", "HH:mm", out _)) To = "07:00";
    }
}
