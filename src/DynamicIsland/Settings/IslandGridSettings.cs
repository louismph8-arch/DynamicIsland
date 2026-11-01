using DynamicIsland.Tiles;

namespace DynamicIsland.Settings;

/// <summary>Contenu de l'île ouverte (refonte O6) : grille de tuiles (défaut) ou l'ancienne rangée d'onglets (repli).</summary>
public enum IslandLayout
{
    Grid,
    Tabs,
}

/// <summary>Une tuile enregistrée : quoi, quelle taille, à quelle place dans sa page.</summary>
public sealed class IslandGridTile
{
    public string Id { get; set; } = "";
    public TileSize Size { get; set; }
    public int Order { get; set; }
}

/// <summary>Une page de la grille : ses tuiles, rangées dans l'ordre (le placement dense fait le reste).</summary>
public sealed class IslandGridPage
{
    public List<IslandGridTile> Tiles { get; set; } = [];
}

/// <summary>
/// Disposition de la grille de l'île (refonte O6) : pages → tuiles {id, taille, ordre}. <see cref="Known"/> = tuiles déjà
/// proposées : une tuile retirée par l'utilisateur ne revient pas toute seule, une tuile ajoutée dans une version suivante se
/// range à la fin. Prévue pour être par profil en O7 (<see cref="IslandSettings.ProfileGrids"/>), sans édition pour l'instant.
/// </summary>
public sealed class IslandGrid
{
    public const int MaxPages = 12;

    public List<IslandGridPage> Pages { get; set; } = [];
    public List<string> Known { get; set; } = [];

    /// <summary>
    /// Page 1 : Musique (L), Contrôles (W), Calendrier, Widgets (météo) ; page 2 : Horloge analogique (L) puis tout le reste,
    /// dans l'ordre des onglets.
    /// </summary>
    public static IslandGrid Default() => FromTabs(IslandTabItem.Defaults());

    /// <summary>
    /// Disposition par défaut, page par page (8 cases chacune : 4 × 2). Page 1 = l'essentiel (inchangée depuis l'étape 1), puis une
    /// page par thème : le temps, le système et le son, les autres et les assistants, la productivité, la création. Une tuile du
    /// registre absente d'ici (version future) arrive à la fin, à sa taille par défaut.
    /// </summary>
    internal static readonly (string Id, TileSize? Size)[][] Themes =
    [
        // Essentiel
        [(IslandTabItem.Music, TileSize.L), (IslandTabItem.Control, TileSize.W), (IslandTabItem.Calendar, TileSize.S), (IslandTabItem.Widgets, TileSize.S)],
        // Le temps : horloge analogique, minuteur, date, horloges du monde
        [(TileRegistry.AnalogClock, TileSize.L), (Tiles.Everyday.EverydayTiles.Timer, null), (Tiles.Everyday.EverydayTiles.Date, null), (IslandTabItem.Clocks, null)],
        // Le PC : moniteur, santé, disques, mélangeur, micro, café
        [(Tiles.LiveTilesB.MonitorId, null), (IslandTabItem.Health, null), (Tiles.Everyday.EverydayTiles.Disk, null), (IslandTabItem.Mixer, null),
         (Tiles.Tools.ToolTiles.Microphone, null), (Tiles.Everyday.EverydayTiles.Coffee, null)],
        // Les autres et les assistants : amis, vocal, profil, assistant IA, traduction, étagère, téléphone
        [(IslandTabItem.Friends, null), (IslandTabItem.Voice, null), (Tiles.LiveTilesB.ProfileId, null), (IslandTabItem.Claude, null),
         (IslandTabItem.Translate, null), (IslandTabItem.Shelf, null), (Tiles.Tools.ToolTiles.Phone, null)],
        // Productivité : notes, à faire, presse-papiers, compteur, calculatrice, mémo vocal, QR code
        [(IslandTabItem.Notes, null), (Tiles.Everyday.EverydayTiles.Todo, null), (IslandTabItem.Clipboard, null), (Tiles.Everyday.EverydayTiles.Counter, null),
         (Tiles.Tools.ToolTiles.Calculator, null), (Tiles.Tools.ToolTiles.Memo, null), (Tiles.Tools.ToolTiles.Qr, null)],
        // Création et réglages : fond d'écran, couleur, capture, paramètres
        [(Tiles.Tools.ToolTiles.Wallpaper, null), (Tiles.Tools.ToolTiles.Color, null), (Tiles.Tools.ToolTiles.Capture, null), (Tiles.Tools.ToolTiles.Settings, null)],
    ];

    /// <summary>
    /// Migration depuis les onglets (réglages d'avant O6) : la disposition par défaut (<see cref="Themes"/>), sans les onglets
    /// cachés ; sur chaque page après la première, les tuiles-onglets gardent entre elles l'ordre choisi pour les onglets (elles échangent leurs
    /// places, les autres tuiles ne bougent pas). Toutes les tuiles du registre sont « connues » : un onglet caché ne revient pas.
    /// </summary>
    public static IslandGrid FromTabs(IReadOnlyList<IslandTabItem> tabs)
    {
        bool Shown(string id) => tabs.FirstOrDefault(t => t.Id == id)?.Visible ?? true;
        int TabRank(string id)
        {
            for (int i = 0; i < tabs.Count; i++) if (tabs[i].Id == id) return i;
            return tabs.Count + Array.IndexOf(IslandTabItem.All, id);
        }
        // Ordre des onglets jamais changé (disposition par défaut) : l'ordre des thèmes reste tel quel.
        bool reordered = !tabs.Select(t => t.Id).Where(IslandTabItem.All.Contains).SequenceEqual(IslandTabItem.All.Where(id => tabs.Any(t => t.Id == id)));
        var grid = new IslandGrid();
        var placed = new HashSet<string>();
        foreach (var theme in Themes)
        {
            var page = new IslandGridPage();
            foreach (var (id, size) in theme)
            {
                if (TileRegistry.Find(id) is not { } definition || !placed.Add(id)) continue;
                if (!IslandTabItem.All.Contains(id) && !definition.InDefaultLayout) continue; // bibliothèque seulement (O7)
                if (!Shown(id)) continue;
                page.Tiles.Add(new IslandGridTile { Id = id, Size = size is { } s && definition.Allows(s) ? s : definition.DefaultSize });
            }
            // Ordre des onglets gardé (sauf page 1, dessinée à la main) : les places des onglets sont redistribuées dans l'ordre choisi.
            var slots = page.Tiles.Select((tile, index) => (tile, index)).Where(x => reordered && grid.Pages.Count > 0 && IslandTabItem.All.Contains(x.tile.Id)).ToList();
            var sorted = slots.Select(x => x.tile).OrderBy(tile => TabRank(tile.Id)).ToList();
            for (int i = 0; i < slots.Count; i++) page.Tiles[slots[i].index] = sorted[i];
            for (int i = 0; i < page.Tiles.Count; i++) page.Tiles[i].Order = i;
            if (page.Tiles.Count > 0) grid.Pages.Add(page);
        }
        grid.Known = [.. placed, .. TileRegistry.All.Select(d => d.Id).Where(id => !placed.Contains(id) && IslandTabItem.All.Contains(id))];
        grid.Normalize(); // tuiles du registre absentes des thèmes : ajoutées à la fin (si elles vont dans la disposition par défaut)
        return grid;
    }

    /// <summary>
    /// Tuiles connues seulement, une fois chacune (la première l'emporte), tailles permises (sinon celle par défaut), ordres
    /// 0, 1, 2…, pages vides retirées, 12 pages au plus ; les tuiles jamais proposées s'ajoutent à la fin de la dernière page.
    /// </summary>
    public void Normalize()
    {
        Pages = (Pages ?? []).Where(p => p != null).ToList();
        Known = (Known ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        var seen = new HashSet<string>();
        foreach (var page in Pages)
        {
            page.Tiles = (page.Tiles ?? [])
                .Where(t => t != null && TileRegistry.Find(t.Id) != null && seen.Add(t.Id))
                .OrderBy(t => t.Order)
                .ToList();
            for (int i = 0; i < page.Tiles.Count; i++)
            {
                var tile = page.Tiles[i];
                var definition = TileRegistry.Find(tile.Id)!;
                if (!Enum.IsDefined(tile.Size) || !definition.Allows(tile.Size)) tile.Size = definition.DefaultSize;
                tile.Order = i;
            }
        }
        Pages = Pages.Where(p => p.Tiles.Count > 0).Take(MaxPages).ToList();
        foreach (var definition in TileRegistry.All.Where(d => !Known.Contains(d.Id)))
        {
            Known.Add(definition.Id);
            if (seen.Contains(definition.Id) || !definition.InDefaultLayout) continue; // hors disposition par défaut : bibliothèque (O7)
            if (Pages.Count == 0) Pages.Add(new IslandGridPage());
            var last = Pages[^1];
            last.Tiles.Add(new IslandGridTile { Id = definition.Id, Size = definition.DefaultSize, Order = last.Tiles.Count });
            seen.Add(definition.Id);
        }
    }

    public bool Contains(string id) => Pages.Any(p => p.Tiles.Any(t => t.Id == id));

    /// <summary>Retire une tuile (réglage « onglet affiché » coupé) ; elle reste « connue ».</summary>
    public void Remove(string id)
    {
        foreach (var page in Pages) page.Tiles.RemoveAll(t => t.Id == id);
        Normalize();
    }

    /// <summary>Remet une tuile (à la fin de la dernière page), à sa taille par défaut.</summary>
    public void Add(string id)
    {
        if (Contains(id) || TileRegistry.Find(id) is not { } definition) return;
        if (Pages.Count == 0) Pages.Add(new IslandGridPage());
        var last = Pages[^1];
        last.Tiles.Add(new IslandGridTile { Id = id, Size = definition.DefaultSize, Order = last.Tiles.Count });
        Normalize();
    }

    /// <summary>Pages → (id, taille) dans l'ordre, pour <see cref="TileLayout.Place"/>.</summary>
    public IEnumerable<IReadOnlyList<(string Id, TileSize Size)>> Ordered(Func<string, bool>? keep = null) =>
        Pages.Select(p => (IReadOnlyList<(string, TileSize)>)p.Tiles.OrderBy(t => t.Order)
            .Where(t => keep?.Invoke(t.Id) ?? true).Select(t => (t.Id, t.Size)).ToList());
}
