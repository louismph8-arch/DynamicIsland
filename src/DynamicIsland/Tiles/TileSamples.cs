using DynamicIsland.Services;
using DynamicIsland.Services.Friends;

namespace DynamicIsland.Tiles;

/// <summary>
/// Données d'exemple des tuiles vivantes (rendus --snapshot, auto-test) : avec <see cref="Enabled"/>, le Moniteur ne mesure rien et
/// la tuile Amis montre des amis inventés (rien n'est demandé au serveur). Jamais activé dans l'app normale.
/// </summary>
public static class TileSamples
{
    public static bool Enabled { get; set; }

    /// <summary>Mesures d'exemple : la mémoire au-delà du seuil d'alerte (anneau rouge).</summary>
    public static readonly SystemStats Stats = new(34, 88, 12, 1.4 * 1024 * 1024, 96 * 1024);

    public static IReadOnlyList<Friend>? Friends { get; set; }
}
