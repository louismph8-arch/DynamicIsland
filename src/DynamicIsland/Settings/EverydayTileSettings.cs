namespace DynamicIsland.Settings;

/// <summary>
/// Données des petites tuiles de tous les jours (refonte O6, lot « nouvelles A ») : la valeur du Compteur et la note qui sert de
/// liste « À faire » (les tâches elles-mêmes sont dans les notes, jamais ici). Le Café n'est jamais enregistré : un redémarrage
/// l'arrête.
/// </summary>
public sealed class EverydayTileSettings
{
    public const long MaxCounter = 999_999;

    /// <summary>Valeur du Compteur (bornée à ±999 999 : le chiffre tient toujours dans une case).</summary>
    public long Counter { get; set; }

    /// <summary>Id de la note « À faire » (vide = pas encore créée ; une note supprimée est recréée au prochain ajout).</summary>
    public string TodoNoteId { get; set; } = "";

    public void Normalize()
    {
        Counter = Math.Clamp(Counter, -MaxCounter, MaxCounter);
        TodoNoteId = (TodoNoteId ?? "").Trim();
    }
}
