namespace DynamicIsland.Settings;

/// <summary>
/// « Ma semaine » (7F) : suivi local de l'activité (temps d'écran, jeux, Pomodoros, applis au premier plan). DÉSACTIVÉ par
/// défaut ; les chiffres eux-mêmes sont dans statistiques.json (jamais ici, jamais envoyés nulle part).
/// </summary>
public sealed class StatsSettings
{
    /// <summary>« Suivre mon activité » : rien n'est relevé tant que l'utilisateur ne l'a pas activé.</summary>
    public bool Enabled { get; set; }
    /// <summary>Petit résumé de la semaine passée dans l'île, le lundi (seulement si le suivi est activé).</summary>
    public bool WeeklySummary { get; set; } = true;
    /// <summary>Lundi (« yyyy-MM-dd ») de la dernière semaine dont le résumé a été montré (ou sauté).</summary>
    public string? LastSummaryWeek { get; set; }
}
