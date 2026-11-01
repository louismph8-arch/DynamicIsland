using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Animation;
using DynamicIsland.Design;
using DynamicIsland.Palette;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;
using DynamicIsland.Tiles.Everyday;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des tuiles de tous les jours (O6, lot A) : calculs purs (états du minuteur, mini-mois, compteur, disques, fumée du
/// café, liste « À faire », palette), puis vues construites en S / W / L avec des données d'exemple, et rien qui tourne pour une
/// tuile hors de la page affichée. Rien n'est demandé à Windows (Café simulé en mode développeur), le vrai bloc-notes et le vrai
/// presse-papiers ne sont jamais touchés (copies à part), les réglages ne sont pas écrits.
/// </summary>
internal static partial class SelfTest
{
    private static void EverydayTiles(List<string> lines)
    {
        // Minuteur : un texte par état, toujours non vide.
        var states = Enum.GetValues<TimerState>().SelectMany(s => Enum.GetValues<TimerKind>().Select(k => TimerFace.StateText(s, k))).ToList();
        Check(lines, states.All(s => s.Length > 0) && TimerFace.StateText(TimerState.Running, TimerKind.Focus) != TimerFace.StateText(TimerState.Running, TimerKind.Timer)
                     && TimerFace.Presets.SequenceEqual([5, 10, 25, 0]),
            $"minuteur : textes d'état ({string.Join(" / ", states.Distinct())}), préréglages 5 / 10 / 25 min + Pomodoro");

        // Mini-mois : novembre 2026 commence un dimanche → 6 lignes en semaine du lundi, 5 en semaine du dimanche ; février 2026 (28 j.,
        // commence un dimanche) en semaine du dimanche = 4 lignes pile. Chaque jour une seule fois, colonnes 0–6.
        var november = DateFace.MonthCells(new DateTime(2026, 11, 18), DayOfWeek.Monday);
        var novemberUs = DateFace.MonthCells(new DateTime(2026, 11, 18), DayOfWeek.Sunday);
        var february = DateFace.MonthCells(new DateTime(2026, 2, 3), DayOfWeek.Sunday);
        Check(lines, november.Count == 30 && november[0] == (1, 0, 6) && november.Max(c => c.Row) == 5 && novemberUs[0] == (1, 0, 0) && novemberUs.Max(c => c.Row) == 4
                     && february.Max(c => c.Row) == 3 && november.All(c => c.Column is >= 0 and < 7) && november.Select(c => (c.Row, c.Column)).Distinct().Count() == 30,
            "date : mini-mois (novembre 2026 : le 1er = dimanche, 6 semaines en semaine du lundi, 5 en semaine du dimanche ; février 2026 : 4)");

        // Compteur : le chiffre rétrécit quand il s'allonge, jamais plus grand que la taille de base.
        bool fits = CounterFace.FitSize(TileSize.S, "7") == TypeRamp.LargeTitle && CounterFace.FitSize(TileSize.S, "999 999") < CounterFace.FitSize(TileSize.S, "1 234")
                    && CounterFace.FitSize(TileSize.L, "12") == EverydayTileShape.CounterLarge && CounterFace.FitSize(TileSize.L, "-999 999") <= TypeRamp.Display;
        Check(lines, fits, "compteur : taille du chiffre selon sa longueur (S : 26 → 18 DIP ; L : 58 → 22 DIP), bornes ±999 999");

        // Disques : S = le plus rempli, W = 2, L = 5 ; rouge sous 10 % ; tailles lisibles.
        var disks = new[] { new DiskSpace("C:", 120L << 30, 1000L << 30), new DiskSpace("D:", 50L << 30, 1000L << 30), new DiskSpace("E:", 900L << 30, 2000L << 30) };
        Check(lines, DiskFace.Pick(disks, TileSize.S).Single().Name == "D:" && DiskFace.Pick(disks, TileSize.W).Count == 2 && DiskFace.Pick(disks, TileSize.L).Count == 3
                     && disks[1].Low && !disks[0].Low && DiskFace.Size(412L << 30).Contains("412") && DiskFace.Size(3L << 40).Contains('3') && DiskFace.Size(820L << 20).Contains("820"),
            $"espace disque : disque montré en S = le plus rempli, 2 en W, 5 au plus en L ; rouge sous 10 % ; « {DiskFace.Size(412L << 30)} », « {DiskFace.Size(3L << 40)} »");

        // Fumée du café : monte et s'efface (0 au début et à la fin du cycle), jamais au-delà de sa hauteur.
        var (rise0, opacity0) = CoffeeFace.SteamPose(0);
        var (riseMid, opacityMid) = CoffeeFace.SteamPose(0.5);
        var (riseEnd, opacityEnd) = CoffeeFace.SteamPose(1);
        Check(lines, rise0 == 0 && opacity0 == 0 && opacityEnd < 1e-9 && riseEnd == -EverydayTileShape.SteamRise && Math.Abs(opacityMid - EverydayTileShape.SteamMaxOpacity) < 1e-9 && riseMid < 0,
            "café : volute qui monte de 7 DIP en apparaissant puis en s'effaçant");

        // Café : en mode développeur, simulé (rien n'est demandé à Windows) ; durées, temps restant, arrêt.
        bool firstClickTimed = KeepAwake.LastChoice is { } firstChoice && firstChoice == KeepAwake.DefaultChoice; // jamais « toujours » d'office
        KeepAwake.Start(TimeSpan.FromMinutes(30));
        // Activité de la pilule tant qu'il est en marche (visible même sans la tuile), retirée à l'arrêt.
        var coffeeLive = Live.LiveActivities.All.FirstOrDefault(a => a.Id == Live.LiveIds.Coffee);
        bool pillTimed = coffeeLive is { Progress: > 0.9 and <= 1 } && coffeeLive.Value.Length > 0 && !coffeeLive.Breathes;
        var paletteOn = PaletteEngine.CoffeeTitles("café");
        bool paletteStop = paletteOn.Count == KeepAwake.Choices.Length + 1 && paletteOn[0] == Loc.T("palette.coffee.stop")
                           && PaletteEngine.CoffeeTitles("coffee stop").SingleOrDefault() == Loc.T("palette.coffee.stop")
                           && PaletteEngine.CoffeeTitles("café 2 h").Count == 1 && PaletteEngine.CoffeeTitles("caféine").Count == 0
                           && PaletteEngine.CoffeeTitles("calendrier").Count == 0;
        KeepAwake.Stop();
        bool pillGone = Live.LiveActivities.All.All(a => a.Id != Live.LiveIds.Coffee) && PaletteEngine.CoffeeTitles("keep awake").Count == KeepAwake.Choices.Length;
        KeepAwake.Start(null);
        bool pillAlways = Live.LiveActivities.All.FirstOrDefault(a => a.Id == Live.LiveIds.Coffee) is { Progress: 1, Value: "" }; // anneau plein, jamais l'arc qui tourne
        KeepAwake.Stop();
        Check(lines, firstClickTimed && pillTimed && paletteStop && pillGone && pillAlways && KeepAwake.PillValue(TimeSpan.FromMinutes(85)) == "1:25"
                     && KeepAwake.PillValue(TimeSpan.FromSeconds(10)).Contains('1'),
            $"café : 1er clic = {KeepAwake.DefaultChoice.TotalMinutes:0} min (pas « toujours »), activité de la pilule « {coffeeLive?.Value} » puis retirée, palette « café » / « coffee stop » / « café 2 h »");
        // Petits textes colorés des tuiles en opale blanche : rouge du disque et accent du jour de la semaine ≥ 4,5 même sur une tuile
        // foncée (verre clair sur décor coloré, mesuré #BFB3D7) ; en sombre, le rouge et l'accent restent lisibles sur la référence.
        var lightTile = System.Windows.Media.Color.FromRgb(0xBF, 0xB3, 0xD7);
        var darkRef = OpalShade.ReferenceSurface(new OpalShade.State(Swatch.AccentStart, Swatch.AccentEnd, Swatch.IslandBase, 0), 0.8);
        var lightRef = OpalShade.ReferenceSurface(new OpalShade.State(Swatch.AccentStart, Swatch.AccentEnd, InkPalette.PearlBase, 1), 0.8);
        double redLight = OpalColor.Contrast(InkPalette.Light[(int)InkRole.DangerText], lightTile);
        double accentLight = OpalColor.ContrastOn(OpalShade.AccentText(Swatch.Lilac, 1, lightRef), lightTile);
        double accentDark = OpalColor.ContrastOn(OpalShade.AccentText(Swatch.Lilac, 0, darkRef), darkRef);
        Check(lines, redLight >= OpalColor.TextContrast && accentLight >= OpalColor.TextContrast && accentDark >= OpalColor.TextContrast,
            $"tuiles en opale blanche : rouge du disque {redLight:0.0}:1, accent du jour {accentLight:0.0}:1 sur une tuile #BFB3D7 ; accent en sombre {accentDark:0.0}:1");

        // Case S avec un chiffre et un nom long (« Presse-/papiers » en français) : le chiffre monte à droite de l'icône, l'info reste,
        // et tout tient dans la case de 92 DIP (avant : chiffre coupé sous l'icône, info cachée).
        if (TileRegistry.Find(IslandTabItem.Clipboard) is { } clipDefinition)
        {
            var clipFace = new TileFace(clipDefinition, TileSize.S, null);
            clipFace.Show(new TileInfo(Loc.F("tiles.items.many", 8), "8"));
            clipFace.Measure(new Size(TileGridShape.Cell, double.PositiveInfinity));
            var (wraps, beside, lineShown) = clipFace.SmallLayout;
            bool layoutOk = (wraps ? beside && lineShown : !beside) && clipFace.DesiredSize.Height <= TileGridShape.Cell + 0.5;
            Check(lines, layoutOk, $"tuile S « {clipDefinition.Name} » avec un chiffre : nom sur {(wraps ? 2 : 1)} ligne(s), chiffre {(beside ? "à droite de l'icône" : "au-dessus du nom")}, hauteur {clipFace.DesiredSize.Height:0} / {TileGridShape.Cell:0} DIP");
        }

        KeepAwake.Start(TimeSpan.FromMinutes(30));
        bool startedTimed = KeepAwake.IsOn && KeepAwake.Remaining is { } left && left <= TimeSpan.FromMinutes(30) && left > TimeSpan.FromMinutes(29);
        KeepAwake.Stop();
        KeepAwake.Start(null);
        bool always = KeepAwake.IsOn && KeepAwake.Remaining == null && KeepAwake.LastChoice == null;
        KeepAwake.Toggle();
        bool stopped = !KeepAwake.IsOn && KeepAwake.EndsAt == null;
        Check(lines, startedTimed && always && stopped && KeepAwake.FormatRemaining(TimeSpan.FromMinutes(85)).Contains("25") && KeepAwake.FormatRemaining(TimeSpan.FromSeconds(20)).Length > 0
                     && KeepAwake.Choices.Length == 4,
            $"café : 30 min / toujours / arrêt (simulé : rien demandé à Windows) ; « {KeepAwake.FormatRemaining(TimeSpan.FromMinutes(85))} », « {KeepAwake.ChoiceLabel(null)} », « {KeepAwake.ChoiceLabel(TimeSpan.FromHours(2))} »");

        // Liste « À faire » : sur un bloc-notes à part (jamais le vrai) ; ajout, reprise de la note, cases, ordre, cocher.
        var data = SettingsStore.Current.TileData;
        string savedId = data.TodoNoteId;
        try
        {
            data.TodoNoteId = "";
            var store = new NotesStore();
            store.Add("Courses", "- [ ] pain");
            var first = TodoList.Add("acheter du lait", store);
            var second = TodoList.Add("  appeler\nMax  ", store);
            var tasks = TodoList.Tasks(second);
            store.ToggleTask(second!.Id, tasks[0].Line);
            var after = TodoList.Tasks(store.Get(second.Id));
            var ordered = TodoList.Ordered(after);
            bool todoOk = first != null && first.Id == second.Id && data.TodoNoteId == second.Id && store.Count == 2
                          && tasks.Select(t => t.Text).SequenceEqual(["acheter du lait", "appeler Max"]) && after[0].Done && !after[1].Done
                          && ordered[0].Text == "appeler Max" && TodoList.Add("   ", store) == null && TodoList.Find(store)?.Title == Loc.T("tiles.todo.note_title");
            // Une note « À faire » qui existe déjà est reprise (pas de 2e liste).
            data.TodoNoteId = "";
            var other = new NotesStore();
            other.Add(Loc.T("tiles.todo.note_title"), "- [x] déjà fait");
            var adopted = TodoList.Add("nouvelle", other);
            Check(lines, todoOk && other.Count == 1 && TodoList.Tasks(adopted).Count == 2,
                "à faire : tâche ajoutée à la note « À faire » (créée une fois, puis reprise), une ligne seulement, cocher, ce qui reste d'abord, rien d'ajouté si vide");
        }
        finally
        {
            data.TodoNoteId = savedId; // les réglages ne gardent rien du test
        }

        // Palette : « todo … », « à faire … », « tâche : … » ; « todo » seul = la liste ; « todolist » n'est pas une commande.
        Check(lines, PaletteEngine.TodoText("todo acheter du lait") == "acheter du lait" && PaletteEngine.TodoText("À faire appeler Max") == "appeler Max"
                     && PaletteEngine.TodoText("tâche : sortir le chien") == "sortir le chien" && PaletteEngine.TodoText("todo") == "" && PaletteEngine.TodoText("todolist") == null
                     && PaletteEngine.TodoText("note du jour") == null,
            "palette : « todo … » / « à faire … » / « tâche : … » ajoutent une tâche, « todo » seul ouvre la liste");

        // Presse-papiers : dernier élément sur une ligne, image = taille ; une copie sensible en attente n'est jamais montrée.
        var clips = new ClipboardHistoryStore();
        var empty = Tiles.Everyday.EverydayTiles.ClipboardInfo(clips);
        clips.AddText("première ligne\n   deuxième");
        var text = Tiles.Everyday.EverydayTiles.ClipboardInfo(clips);
        const string secret = "Tr0ub4dor&3-motdepasse";
        clips.Offer(secret);
        var hidden = Tiles.Everyday.EverydayTiles.ClipboardInfo(clips);
        Check(lines, empty.Value == null && text.Line == "première ligne deuxième" && text.Value == "1"
                     && hidden.Line != null && !hidden.Line.Contains(secret) && hidden.Active && hidden.Art == null,
            "presse-papiers : dernier élément sur une ligne + nombre ; copie sensible en attente jamais affichée (« " + hidden.Line + " »)");
    }

    /// <summary>Thread de l'interface : chaque tuile du lot en S / W / L avec des données d'exemple, et rien qui tourne page cachée.</summary>
    private static void EverydayTileViews(List<string> lines)
    {
        var context = new TileContext
        {
            Media = new MediaService(Application.Current.Dispatcher),
            Timer = new TimerService(),
            ToolAvailable = _ => true,
            ExistingView = _ => null,
        };
        string[] ids = [Tiles.Everyday.EverydayTiles.Timer, Tiles.Everyday.EverydayTiles.Coffee, Tiles.Everyday.EverydayTiles.Date,
            Tiles.Everyday.EverydayTiles.Counter, Tiles.Everyday.EverydayTiles.Disk, Tiles.Everyday.EverydayTiles.Todo, IslandTabItem.Notes, IslandTabItem.Clipboard];
        var sample = new[] { new DiskSpace("C:", 412L << 30, 1000L << 30), new DiskSpace("D:", 40L << 30, 1000L << 30) };
        var notes = new NotesStore();
        notes.Add("Idées", "Projet\n- [ ] maquette\n- [x] logo");
        notes.Add(Loc.T("tiles.todo.note_title"), "- [ ] pain\n- [ ] lait");
        var broken = new List<string>();
        int built = 0;
        foreach (var id in ids)
        {
            var definition = TileRegistry.Find(id);
            if (definition == null)
            {
                broken.Add(id + " (absente du registre)");
                continue;
            }
            foreach (var size in new[] { TileSize.S, TileSize.W, TileSize.L })
            {
                try
                {
                    var face = definition.Create(context, size);
                    if (face is DiskFace disk) disk.Sample = sample;
                    if (face is TodoFace todo) todo.Store = notes;
                    if (face is NotesFace note) note.Store = notes;
                    face.Refresh();
                    var (columns, rows) = size.Span();
                    var box = new Size(columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap, rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap);
                    face.Width = box.Width;
                    face.Height = box.Height;
                    face.Measure(box);
                    face.Arrange(new Rect(box));
                    // Rien ne dépasse de la case : contenu mesuré sans limite de hauteur ≤ hauteur utile.
                    if (face.Child is FrameworkElement child)
                    {
                        child.Measure(new Size(box.Width - face.Padding.Left - face.Padding.Right, double.PositiveInfinity));
                        double room = box.Height - face.Padding.Top - face.Padding.Bottom;
                        if (child.DesiredSize.Height > room + 0.5) broken.Add($"{id} {size} ({child.DesiredSize.Height:0} > {room:0} DIP)");
                    }
                    built++;
                }
                catch (Exception ex)
                {
                    broken.Add($"{id} {size} ({ex.GetType().Name} : {ex.Message})");
                }
            }
        }
        Check(lines, broken.Count == 0, broken.Count == 0
            ? $"tuiles du lot A : {built} vues construites (Minuteur, Café, Date, Compteur, Espace disque, À faire, Notes, Presse-papiers en S / W / L), rien ne dépasse de sa case"
            : "tuiles du lot A en défaut : " + string.Join(", ", broken));

        // Hors de la page affichée : pas « en vue », rien n'est lu (disque) ni animé (fumée du café) ; en vue : tout repart.
        KeepAwake.Start(null);
        CoffeeFace.AnimateInTests = true;
        try
        {
            var grid = new TileGridView();
            var slots = new List<TileSlot>
            {
                new(IslandTabItem.Calendar, TileSize.S, 0, 0, 0),
                new(Tiles.Everyday.EverydayTiles.Coffee, TileSize.W, 1, 0, 0),
                new(Tiles.Everyday.EverydayTiles.Disk, TileSize.W, 1, 2, 0),
            };
            grid.Show(slots, (d, s) => d.Create(context, s));
            var coffee = (CoffeeFace)grid.FaceOf(Tiles.Everyday.EverydayTiles.Coffee)!;
            var diskFace = (DiskFace)grid.FaceOf(Tiles.Everyday.EverydayTiles.Disk)!;
            grid.IsActive = true; // île ouverte sur la page 1
            bool hiddenQuiet = !coffee.IsLive && !coffee.SteamAnimating && diskFace.Reads == 0;
            grid.ShowPage(1, animate: false);
            bool expectAnimation = MotionPrefs.Rich && OpalTheme.EffectsOn;
            bool shownLive = coffee.IsLive && coffee.SteamAnimating == expectAnimation && diskFace.Reads == 1;
            grid.IsActive = false; // île refermée
            bool closedQuiet = !coffee.IsLive && !coffee.SteamAnimating;
            KeepAwake.Stop();
            grid.IsActive = true;
            coffee.Refresh();
            bool offQuiet = coffee.IsLive && !coffee.SteamAnimating;
            grid.IsActive = false;
            Check(lines, hiddenQuiet && shownLive && closedQuiet && offQuiet,
                $"tuiles vivantes : page cachée = rien lu ni animé ; page affichée = disque lu une fois{(expectAnimation ? ", fumée animée" : " (animations réduites : fumée figée)")} ; île fermée ou café arrêté = plus rien ne tourne");
        }
        finally
        {
            CoffeeFace.AnimateInTests = false;
            KeepAwake.Stop();
        }
    }
}
