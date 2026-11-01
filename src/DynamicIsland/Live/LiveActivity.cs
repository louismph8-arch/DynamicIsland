using System.Windows;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Live;

/// <summary>Comment une activité se dessine dans la pilule.</summary>
public enum LiveStyle
{
    /// <summary>Anneau de progression (icône au centre) à gauche, valeur en chiffres à droite.</summary>
    Ring,
    /// <summary>Musique : pochette à gauche, égaliseur à droite.</summary>
    Music,
    /// <summary>Salon vocal Discord : avatars à gauche.</summary>
    Voice,
}

/// <summary>Priorités : la plus haute est l'activité principale de la pilule (les autres comptent dans le « +1 »).</summary>
public static class LivePriority
{
    public const int Alert = 100;     // minuteur terminé
    public const int Recording = 90;  // mémo vocal (O8)
    public const int Timer = 80;      // minuteur / Pomodoro
    public const int Transfer = 70;   // fichier téléphone ↔ PC
    public const int Update = 60;     // installation Windows Update
    public const int Assistant = 50;  // réponse de l'assistant IA en cours
    public const int Voice = 30;      // salon vocal Discord
    public const int Coffee = 20;     // Café : PC gardé éveillé (tuile O6)
    public const int Music = 10;      // musique qui joue
}

/// <summary>Identifiants des activités connues (une seule activité par identifiant).</summary>
public static class LiveIds
{
    public const string Timer = "timer";
    public const string Transfer = "transfer";
    public const string Update = "windows-update";
    public const string Assistant = "assistant";
    public const string Recording = "recording";
    public const string Music = "music";
    public const string Voice = "voice";
    public const string Coffee = "coffee";
}

/// <summary>
/// Une activité en direct de l'île réduite (refonte O5) : minuteur, transfert, installation, réponse de l'assistant,
/// enregistrement, musique, salon vocal… Système unique et extensible : un module ajoute / met à jour son activité par
/// <see cref="LiveActivities.Set"/> et la retire par <see cref="LiveActivities.Remove"/> ; la pilule montre la plus prioritaire.
/// </summary>
/// <param name="Id">Identifiant (<see cref="LiveIds"/>) : la même activité est remplacée, pas ajoutée.</param>
/// <param name="Icon">Glyphe Lucide (<see cref="Icons"/>) au centre de l'anneau.</param>
/// <param name="Progress">0–1, ou null = indéterminée (arc qui tourne doucement).</param>
/// <param name="Value">Valeur courte (« 24:59 », « 42 % », « 2/5 ») en chiffres tabulaires ; vide = rien à droite.</param>
/// <param name="Color">Couleur de l'anneau et de la valeur ; null = l'accent.</param>
/// <param name="Priority">Plus haut = devant (<see cref="LivePriority"/>).</param>
public sealed record LiveActivity(string Id, string Icon, double? Progress, string Value, Color? Color, int Priority)
{
    public LiveStyle Style { get; init; } = LiveStyle.Ring;

    /// <summary>La bordure irisée « respire » pendant cette activité (pas pour la musique ni le vocal, qui durent des heures).</summary>
    public bool Breathes { get; init; } = true;

    /// <summary>Début (gardé quand l'activité est mise à jour) : à priorité égale, la plus ancienne reste devant.</summary>
    public DateTime Since { get; init; } = DateTime.UtcNow;

    /// <summary>Mémo vocal en cours (O8, pas encore codé) : point rouge, temps écoulé, priorité forte.</summary>
    public static LiveActivity Recording(TimeSpan elapsed) =>
        new(LiveIds.Recording, Icons.Voice, null, Services.TimerService.Format(elapsed), Swatch.Red, LivePriority.Recording);
}

/// <summary>
/// Les activités en cours, sur le fil de l'interface (un appel d'un autre fil est renvoyé dessus). <see cref="Changed"/> n'est
/// levé que si quelque chose a vraiment changé : au repos, rien ne tourne.
/// </summary>
public static class LiveActivities
{
    private static readonly Dictionary<string, LiveActivity> Items = new(StringComparer.Ordinal);

    public static event Action? Changed;

    public static IReadOnlyCollection<LiveActivity> All => Items.Values;

    public static void Set(LiveActivity activity)
    {
        if (Marshal(() => Set(activity))) return;
        if (Items.TryGetValue(activity.Id, out var old))
        {
            activity = activity with { Since = old.Since };
            if (old == activity) return;
        }
        Items[activity.Id] = activity;
        Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        if (Marshal(() => Remove(id))) return;
        if (Items.Remove(id)) Changed?.Invoke();
    }

    /// <summary>Activité principale et nombre des autres (« +1 »).</summary>
    public static (LiveActivity? Main, int Others) Pick() => LivePill.Select(Items.Values);

    /// <summary>Rendus et auto-test : tout retirer.</summary>
    internal static void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Changed?.Invoke();
    }

    private static bool Marshal(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) return false;
        dispatcher.BeginInvoke(action);
        return true;
    }
}
