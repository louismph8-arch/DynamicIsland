using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuiles vivantes du lot B (refonte O6) : définitions du Moniteur et du Profil (nouvelles tuiles) et vue vivante de la tuile Amis.
/// Enregistrées dans <see cref="TileRegistry.All"/> ; des méthodes (pas des champs) : l'ordre d'initialisation reste celui du registre.
/// </summary>
public static class LiveTilesB
{
    public const string MonitorId = "monitor";
    public const string ProfileId = "profile";

    /// <summary>Moniteur : anneaux CPU / RAM (+ GPU en L) ; clic = outil Santé. Module Santé : arrêté → tuile cachée.</summary>
    public static TileDefinition Monitor()
    {
        TileDefinition? definition = null;
        definition = new TileDefinition
        {
            Id = MonitorId,
            NameKey = "tiles.monitor.name",
            Glyph = Icons.Activity,
            Sizes = [TileSize.S, TileSize.W, TileSize.L],
            DefaultSize = TileSize.W,
            Module = ModuleGuard.Health,
            CreateFace = (_, size) => new MonitorTileFace(definition!, size),
            CreateDetail = context => context.ExistingView(IslandTabItem.Health),
        };
        return definition;
    }

    /// <summary>Profil : pastilles des profils, changement en un clic (S : profil actif, clic = suivant). Pas d'outil à ouvrir.</summary>
    public static TileDefinition Profile()
    {
        TileDefinition? definition = null;
        definition = new TileDefinition
        {
            Id = ProfileId,
            NameKey = "tiles.profile.name",
            Glyph = Icons.Profiles,
            Sizes = [TileSize.S, TileSize.W],
            DefaultSize = TileSize.W,
            Module = ModuleGuard.ControlTab,
            IsAvailable = _ => ProfileService.All.Count > 1, // un seul profil : rien à changer
            CreateFace = (_, size) => new ProfileTileFace(definition!, size),
            CreateDetail = _ => null,
        };
        return definition;
    }

    /// <summary>Vue vivante de la tuile Amis (la tuile reste celle de l'onglet : disponible seulement avec un compte et des amis).</summary>
    public static TileFace FriendsFace(TileDefinition definition, TileSize size) => new FriendsTileFace(definition, size);
}
