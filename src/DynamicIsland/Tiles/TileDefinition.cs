using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Tiles;

/// <summary>
/// Ce que l'île donne aux tuiles (refonte O6) : ses services (musique, minuteur…) et deux questions qu'elle seule sait trancher.
/// Les tuiles ne gardent rien d'autre de l'île.
/// </summary>
public sealed class TileContext
{
    public required MediaService Media { get; init; }
    public required TimerService Timer { get; init; }
    /// <summary>L'onglet / outil <c>id</c> a quelque chose à montrer et son module tourne (Claude activé, des villes, des amis…).</summary>
    public required Func<string, bool> ToolAvailable { get; init; }
    /// <summary>Vue existante d'un onglet de l'île (« music », « notes »…), qui sert de vue détaillée ; null = inconnue.</summary>
    public required Func<string, FrameworkElement?> ExistingView { get; init; }
    /// <summary>
    /// Niveau du son qui sort (0–1, vumètre Core Audio de l'île, celui de l'égaliseur) : visualiseur de la tuile Musique. Null =
    /// pas de vumètre (auto-test) : les barres restent au repos.
    /// </summary>
    public Func<double>? MeterLevel { get; init; }
    /// <summary>Auto-test : aucune tuile ne demande quoi que ce soit au réseau (météo).</summary>
    public bool Offline { get; init; }
    /// <summary>
    /// Ouvre la palette pré-remplie (« note », « todo », « = » de la calculatrice…) : l'île ne prend jamais le clavier, une saisie
    /// passe par là. Par défaut (rendus, auto-test) : rien.
    /// </summary>
    public Action<string?> OpenPalette { get; init; } = _ => { };
    /// <summary>Referme l'île (avant une capture de l'écran : elle ne doit pas être dessus).</summary>
    public Action CollapseIsland { get; init; } = () => { };
    /// <summary>Petite annonce dans l'île (icône, texte, clic) ; faux si elle ne peut pas s'afficher (île ouverte, jeu…).</summary>
    public Func<string, string, Action?, bool> Notice { get; init; } = (_, _, _) => false;
}

/// <summary>Ce qu'une tuile simple affiche en direct : une ligne d'info, une valeur en grands chiffres, l'icône allumée, une image.</summary>
public readonly record struct TileInfo(string? Line, string? Value = null, bool Active = false, ImageSource? Art = null);

/// <summary>
/// Une tuile de la grille (refonte O6). Pour en ajouter une : une entrée dans <see cref="TileRegistry"/> avec son id (unique,
/// enregistré dans les réglages), son nom (clé de texte FR / EN), son icône Lucide, ses tailles, et
/// <list type="bullet">
/// <item><see cref="Read"/> : l'info en direct d'une tuile simple (lue au plus une fois par seconde, seulement page visible et
/// île ouverte : doit être rapide, sans disque ni réseau) ;</item>
/// <item>ou <see cref="CreateFace"/> : une tuile « vivante » à soi (dérivée de <see cref="TileFace"/>, étapes suivantes) ;</item>
/// <item><see cref="CreateDetail"/> : la vue détaillée (l'outil) ouverte au clic — aujourd'hui un onglet existant de l'île.</item>
/// </list>
/// </summary>
public sealed class TileDefinition
{
    /// <summary>Identifiant enregistré (pour les tuiles-onglets : celui de <see cref="Settings.IslandTabItem"/>).</summary>
    public required string Id { get; init; }
    /// <summary>Clé du nom (fr.json / en.json) ; ignorée si <see cref="DynamicName"/> est donné.</summary>
    public string NameKey { get; init; } = "";
    /// <summary>Nom qui change (« Claude » / « ChatGPT »).</summary>
    public Func<string>? DynamicName { get; init; }
    /// <summary>Icône Lucide (<see cref="Themes.Icons"/>).</summary>
    public required string Glyph { get; init; }
    public TileSize[] Sizes { get; init; } = [TileSize.S, TileSize.W, TileSize.L];
    public TileSize DefaultSize { get; init; } = TileSize.S;
    /// <summary>Module <see cref="ModuleGuard"/> : arrêté après une erreur → tuile cachée pour la session.</summary>
    public string? Module { get; init; }
    /// <summary>Condition d'affichage (en plus du module) : sans contenu, la tuile ne prend pas de place.</summary>
    public Func<TileContext, bool> IsAvailable { get; init; } = _ => true;
    /// <summary>Tuile simple : info en direct.</summary>
    public Func<TileContext, TileInfo> Read { get; init; } = _ => default;
    /// <summary>Tuile vivante (facultatif) : sa propre vue, à la taille demandée.</summary>
    public Func<TileContext, TileSize, TileFace>? CreateFace { get; init; }
    /// <summary>Vue détaillée (outil) ouverte au clic, en grand dans l'île ; null = la tuile n'ouvre rien.</summary>
    public Func<TileContext, FrameworkElement?> CreateDetail { get; init; } = _ => null;
    /// <summary>
    /// Action au clic, à la place d'une vue détaillée (Capture, Micro, Paramètres…). Une erreur arrête le module de la tuile
    /// (<see cref="Module"/>), jamais l'île.
    /// </summary>
    public Action<TileContext, TileFace>? Click { get; init; }
    /// <summary>Action au clic droit (sinon le menu habituel de l'île).</summary>
    public Action<TileContext, TileFace>? RightClick { get; init; }
    /// <summary>
    /// Dans la disposition par défaut (et ajoutée à la fin pour les grilles déjà enregistrées). Faux = seulement dans la
    /// bibliothèque de tuiles (O7) : une tuile qui ne rend pas encore un vrai service en un clic.
    /// </summary>
    public bool InDefaultLayout { get; init; } = true;
    /// <summary>Taille de l'île pour une vue détaillée qui n'est pas un onglet existant (O8) ; null = celle de l'onglet.</summary>
    public Size? DetailSize { get; init; }

    public string Name => DynamicName?.Invoke() ?? Loc.T(NameKey);

    public bool Allows(TileSize size) => Sizes.Contains(size);

    /// <summary>Vue de la tuile à cette taille (simple, ou vivante si <see cref="CreateFace"/> est donné).</summary>
    public TileFace Create(TileContext context, TileSize size) =>
        CreateFace?.Invoke(context, size) ?? new TileFace(this, size, () => Read(context));
}
