using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Animation;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la grille de tuiles (refonte O6, étape 1) : placement dense (S / W / L, débordement sur une nouvelle page, jamais
/// de superposition), disposition par défaut, migration depuis les onglets, réglages relus, pages bornées, registre complet et
/// chaque tuile qui ouvre bien sa vue. Calculs purs, puis vues construites sur le thread de l'interface (rien n'est affiché).
/// </summary>
internal static partial class SelfTest
{
    private static void GridO6(List<string> lines)
    {
        // Page 1 par défaut : Musique L en haut à gauche, Contrôles W à côté, Calendrier et Widgets en dessous — page pleine.
        var first = TileLayout.Place([[("music", TileSize.L), ("control", TileSize.W), ("calendar", TileSize.S), ("widgets", TileSize.S)]]);
        var expected = new[] { new TileSlot("music", TileSize.L, 0, 0, 0), new("control", TileSize.W, 0, 2, 0), new("calendar", TileSize.S, 0, 2, 1), new("widgets", TileSize.S, 0, 3, 1) };
        Check(lines, first.SequenceEqual(expected), "placement : Musique (L) en haut à gauche, Contrôles (W) à côté, Calendrier et Widgets (S) dessous, une seule page");

        // Débordement : 10 tuiles S → 8 sur la page, 2 sur une nouvelle ; une S plus loin bouche le trou laissé par une L.
        var ten = TileLayout.Place([Enumerable.Range(0, 10).Select(i => ($"t{i}", TileSize.S)).ToList()]);
        var holes = TileLayout.Place([[("a", TileSize.S), ("b", TileSize.W), ("c", TileSize.W), ("d", TileSize.S)]]);
        var tidy = TileLayout.Place([[("a", TileSize.S), ("b", TileSize.S), ("c", TileSize.S), ("big", TileSize.L)]]);
        Check(lines, ten.Count(s => s.Page == 0) == 8 && ten.Count(s => s.Page == 1) == 2 && ten[8] == new TileSlot("t8", TileSize.S, 1, 0, 0)
                     && holes.First(s => s.Id == "d") == new TileSlot("d", TileSize.S, 0, 3, 0)
                     && tidy.All(s => s.Page == 0) && tidy.First(s => s.Id == "big") == new TileSlot("big", TileSize.L, 0, 0, 0),
            "débordement : 10 tuiles S → 8 + 2 sur une 2e page ; une S plus loin bouche un trou ; L / W / S rangées de la plus grande à la plus petite quand l'ordre choisi laisserait une L dehors");

        // Hasard : 500 dispositions (1 à 4 pages, 0 à 14 tuiles de tailles mêlées) → chaque tuile posée une fois, dans la grille,
        // sans superposition, pages numérotées sans trou, aucune page vide.
        var random = new Random(6);
        int bad = 0, tiles = 0, pages = 0;
        for (int run = 0; run < 500; run++)
        {
            var input = Enumerable.Range(0, random.Next(1, 5)).Select(p => (IReadOnlyList<(string, TileSize)>)Enumerable.Range(0, random.Next(0, 15))
                .Select(i => ($"p{p}-{i}", (TileSize)random.Next(3))).ToList()).ToList();
            var slots = TileLayout.Place(input);
            tiles += slots.Count;
            int count = slots.Count == 0 ? 0 : slots.Max(s => s.Page) + 1;
            pages += count;
            bool ok = slots.Count == input.Sum(p => p.Count) && slots.Select(s => s.Id).Distinct().Count() == slots.Count
                      && Enumerable.Range(0, count).All(p => slots.Any(s => s.Page == p));
            foreach (var group in slots.GroupBy(s => s.Page))
            {
                var used = new bool[TileGridShape.Columns, TileGridShape.Rows];
                foreach (var slot in group)
                {
                    var (w, h) = slot.Size.Span();
                    if (slot.Column < 0 || slot.Row < 0 || slot.Column + w > TileGridShape.Columns || slot.Row + h > TileGridShape.Rows) ok = false;
                    else
                        for (int x = slot.Column; x < slot.Column + w; x++)
                            for (int y = slot.Row; y < slot.Row + h; y++)
                            {
                                if (used[x, y]) ok = false;
                                used[x, y] = true;
                            }
                }
            }
            if (!ok) bad++;
        }
        Check(lines, bad == 0, $"placement au hasard : 500 dispositions ({tiles} tuiles, {pages} pages) sans superposition, rien hors de la grille, rien de perdu ({bad} erreur(s))");

        // Disposition par défaut : page 1 comme demandé, puis une page par thème (IslandGrid.Themes) ; toutes les tuiles du
        // registre connues, chacune une fois, chaque page dans ses 8 cases (aucune ne déborde sur une page de plus).
        var fresh = IslandGrid.Default();
        var page2 = fresh.Pages.Count > 1 ? fresh.Pages[1].Tiles.Select(t => t.Id).ToList() : [];
        var themed = IslandGrid.Themes.SelectMany(p => p.Select(t => t.Id)).ToList();
        var freshSlots = TileLayout.Place(fresh.Ordered());
        bool defaultOk = fresh.Pages[0].Tiles.Select(t => (t.Id, t.Size)).SequenceEqual(new[] { ("music", TileSize.L), ("control", TileSize.W), ("calendar", TileSize.S), ("widgets", TileSize.S) })
                         && page2.FirstOrDefault() == TileRegistry.AnalogClock && fresh.Pages[1].Tiles[0].Size == TileSize.L
                         && fresh.Pages.Count == IslandGrid.Themes.Length && freshSlots.Max(s => s.Page) + 1 == fresh.Pages.Count
                         && fresh.Pages.Select((p, i) => p.Tiles.OrderBy(t => t.Order).Select(t => t.Id).SequenceEqual(IslandGrid.Themes[i].Select(t => t.Id))).All(same => same)
                         && themed.Distinct().Count() == themed.Count && TileRegistry.All.All(d => themed.Contains(d.Id))
                         && TileRegistry.All.All(d => fresh.Known.Contains(d.Id) && fresh.Contains(d.Id) == (d.InDefaultLayout || IslandTabItem.All.Contains(d.Id)));
        Check(lines, defaultOk, $"disposition par défaut : {fresh.Pages.Count} pages par thème (page 1 = Musique L, Contrôles W, Calendrier, Widgets ; page 2 = {string.Join(", ", page2)}), dans l'ordre des thèmes, toutes les tuiles une fois, aucune page qui déborde");

        // Migration depuis les onglets : onglets cachés = tuiles retirées (et qui ne reviennent pas toutes seules).
        var tabs = IslandTabItem.Defaults();
        tabs.First(t => t.Id == IslandTabItem.Shelf).Visible = false;
        tabs.First(t => t.Id == IslandTabItem.Control).Visible = false;
        (tabs[0], tabs[^1]) = (tabs[^1], tabs[0]); // ordre changé : Traduire d'abord
        var migrated = IslandGrid.FromTabs(tabs);
        migrated.Normalize();
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var old = JsonSerializer.Deserialize<IslandSettings>("{\"Tabs\":[{\"Id\":\"notes\",\"Visible\":false},{\"Id\":\"music\",\"Visible\":true}]}", options)!;
        old.Normalize();
        Check(lines, !migrated.Contains("shelf") && !migrated.Contains("control") && migrated.Contains("music") && migrated.Contains("calendar")
                     && migrated.Pages[1].Tiles[0].Id == TileRegistry.AnalogClock && migrated.Known.Contains("shelf")
                     && MigratedSocial(migrated).SequenceEqual(new[] { "translate", "voice", "profile", "claude", "friends", "phone" })
                     && old.Layout == IslandLayout.Grid && old.Grid != null && !old.Grid.Contains("notes") && old.Grid.Contains("music"),
            "migration : onglets cachés (Étagère, Contrôles) = tuiles retirées, ordre des onglets gardé sur chaque page (Traduire en tête de la page Amis), anciens réglages → grille par défaut");

        // Réglages relus : taille, ordre, mode Onglets ; nettoyage (tuile inconnue, doublon, taille interdite, page vide) ; une tuile
        // jamais proposée (nouvelle version) arrive à la fin, une tuile retirée reste retirée.
        var saved = new IslandSettings { Layout = IslandLayout.Tabs, Grid = IslandGrid.Default() };
        saved.Grid.Pages[0].Tiles[0].Size = TileSize.W;
        saved.Grid.Remove("notes");
        var reread = JsonSerializer.Deserialize<IslandSettings>(JsonSerializer.Serialize(saved, options), options)!;
        reread.Normalize();
        var messy = new IslandGrid
        {
            Pages = [new() { Tiles = [new() { Id = "music", Size = (TileSize)7 }, new() { Id = "inconnue" }, new() { Id = "music", Size = TileSize.S }] }, new()],
            Known = TileRegistry.All.Select(d => d.Id).Where(id => id != "translate").ToList(),
        };
        messy.Normalize();
        string Signature(IslandGrid g) => string.Join("|", g.Pages.Select(p => string.Join(",", p.Tiles.Select(t => $"{t.Id}:{t.Size}:{t.Order}"))));
        Check(lines, reread.Layout == IslandLayout.Tabs && Signature(reread.Grid!) == Signature(saved.Grid) && !reread.Grid!.Contains("notes")
                     && messy.Pages.Count == 1 && messy.Pages[0].Tiles.Select(t => t.Id).SequenceEqual(new[] { "music", "translate" })
                     && messy.Pages[0].Tiles[0].Size == TileSize.L,
            "réglages : grille relue à l'identique (mode Onglets, Musique en W, Notes retirée) ; tuile inconnue / doublon / taille impossible / page vide nettoyés ; nouvelle tuile ajoutée à la fin");

        // Pages : toujours dans [0, n − 1], le bout = rebond (élastique déjà borné à 14 DIP, voir O3).
        bool turns = TileLayout.Turn(0, 3, 1) == (1, false) && TileLayout.Turn(2, 3, 1) == (2, true) && TileLayout.Turn(0, 3, -1) == (0, true)
                     && TileLayout.Turn(1, 3, -5) == (0, false) && TileLayout.Turn(9, 3, 1) == (2, true) && TileLayout.Turn(0, 0, 1) == (0, true)
                     && TileLayout.Turn(1, 2, 0) == (1, false) && Math.Abs(Elastic.Rubber(1e6, Kinetics.ElasticMax)) < Kinetics.ElasticMax;
        Check(lines, turns, "pages : molette / glisser bornés (au bout : on reste et ça rebondit, jamais hors des pages)");

        // Registre : ids uniques, tous les onglets ont leur tuile, tailles par défaut permises, module de chaque tuile connu.
        var ids = TileRegistry.All.Select(d => d.Id).ToList();
        Check(lines, ids.Distinct().Count() == ids.Count && IslandTabItem.All.All(ids.Contains) && TileRegistry.All.All(d => d.Allows(d.DefaultSize) && d.Module != null),
            $"registre : {ids.Count} tuiles (une par onglet + {ids.Count - IslandTabItem.All.Length} autres), ids uniques, tailles par défaut permises");

        // Taille : la grille tient dans la fenêtre de l'île (vue ouverte de 290 DIP, largeur maximale).
        var island = TileGridShape.Island;
        Check(lines, island.Width <= IslandShape.MaxExpandedWidth && island.Height <= IslandShape.MaxExpandedHeightVertical,
            $"taille : île ouverte sur la grille {island.Width:0} × {island.Height:0} DIP (≤ {IslandShape.MaxExpandedWidth:0} × {IslandShape.MaxExpandedHeightVertical:0})");
    }

    /// <summary>Page « les autres et les assistants » d'une grille migrée (celle qui contient la tuile Amis).</summary>
    private static IEnumerable<string> MigratedSocial(IslandGrid grid) =>
        grid.Pages.FirstOrDefault(p => p.Tiles.Any(t => t.Id == IslandTabItem.Friends))?.Tiles.OrderBy(t => t.Order).Select(t => t.Id) ?? [];

    /// <summary>Thread de l'interface : chaque tuile se construit dans les 3 tailles et ouvre la vue de son onglet.</summary>
    private static void GridViews(List<string> lines)
    {
        var markers = TileRegistry.All.ToDictionary(d => d.Id, d => (FrameworkElement)new Border { Tag = d.Id });
        var context = new TileContext
        {
            Media = new MediaService(Application.Current.Dispatcher),
            Timer = new TimerService(),
            ToolAvailable = _ => true,
            ExistingView = id => markers.GetValueOrDefault(id),
            Offline = true, // aucune requête météo pendant l'auto-test
        };
        // Vue détaillée attendue : celle de son onglet ; l'horloge analogique (pas un onglet) ouvre l'outil Horloges.
        var broken = new List<string>();
        foreach (var definition in TileRegistry.All.Where(d => IslandTabItem.All.Contains(d.Id))) // tuiles-onglets ; les autres : leurs propres tests
        {
            try
            {
                foreach (var size in definition.Sizes)
                {
                    var face = definition.Create(context, size);
                    face.Refresh();
                    face.Measure(new Size(TileGridShape.Cell * 2 + TileGridShape.Gap, TileGridShape.Cell * 2 + TileGridShape.Gap));
                }
                // Tuile-onglet : la vue de son onglet ; autre tuile (Moniteur → Santé, Profil → rien) : la vue d'un onglet ou rien.
                var detail = definition.CreateDetail(context);
                bool detailOk = IslandTabItem.All.Contains(definition.Id)
                    ? ReferenceEquals(detail, markers[definition.Id])
                    : detail == null || IslandTabItem.All.Any(id => ReferenceEquals(detail, markers[id]));
                if (!detailOk || string.IsNullOrWhiteSpace(definition.Name)) broken.Add(definition.Id);
            }
            catch (Exception ex)
            {
                broken.Add($"{definition.Id} ({ex.GetType().Name})");
            }
        }
        Check(lines, broken.Count == 0, broken.Count == 0
            ? $"{IslandTabItem.All.Length} tuiles-onglets construites en S / W / L, chacune ouvre la vue détaillée de son onglet"
            : "tuiles en défaut : " + string.Join(", ", broken));

        // Dans une vraie île (jamais affichée) : chaque tuile retrouve bien l'onglet existant de l'île.
        var islandWindow = new IslandWindow { SnapshotFolder = System.IO.Path.GetTempPath() };
        var wrong = TileRegistry.All.Where(d => IslandTabItem.All.Contains(d.Id) && !islandWindow.TileOpensItsView(d.Id)).Select(d => d.Id).ToList();
        Check(lines, wrong.Count == 0, wrong.Count == 0 ? "île : chaque tuile ouvre l'onglet qui lui correspond (Musique → MusicView…)" : "tuiles sans leur onglet : " + string.Join(", ", wrong));

        // Grille construite : pages, molette bornée.
        var grid = new TileGridView();
        var defaultSlots = TileLayout.Place(IslandGrid.Default().Ordered());
        int expectedPages = defaultSlots.Max(s => s.Page) + 1; // onglets : 4 + 8 + 2, puis les tuiles-outils
        grid.Show(defaultSlots, (d, s) => d.Create(context, s));
        int before = grid.CurrentPage;
        bool atStart = !grid.Turn(-1, animate: false);
        bool next = Enumerable.Range(0, expectedPages - 1).All(_ => grid.Turn(1, animate: false));
        bool atEnd = !grid.Turn(1, animate: false);
        Check(lines, grid.PageCount == expectedPages && before == 0 && atStart && next && atEnd && grid.CurrentPage == expectedPages - 1 && grid.Faces.Count() == defaultSlots.Count,
            $"grille : {grid.PageCount} pages, {grid.Faces.Count()} tuiles posées ; molette : rien avant la 1re page ni après la dernière");

        LiveTileChecks(lines, context);
    }
}
