using System.IO;
using DynamicIsland.Settings;
using DynamicIsland.Stage;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de Stage Manager (7G) : décisions pures avec des écrans SIMULÉS (ceux de l'utilisateur : principal 1920 × 1080, un
/// au-dessus, un portrait à gauche) à 100 / 125 / 150 / 200 %, groupes et ordre de la colonne, exclusions, et aller-retour du fichier
/// d'état dans un fichier temporaire. Aucune vraie fenêtre n'est réduite ni déplacée.
/// </summary>
internal static partial class SelfTest
{
    private static RECT R(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static void StageManagerTest(List<string> lines)
    {
        // Écrans de l'utilisateur : principal, au-dessus (y négatif), portrait à gauche (x négatif).
        var primary = R(0, 0, 1920, 1080);
        var top = R(0, -1080, 1920, 0);
        var left = R(-1080, -420, 0, 1500);
        var screens = new List<RECT> { primary, top, left };
        Check(lines, StageLayout.ScreenFor(R(100, 100, 900, 700), screens) == 0
                     && StageLayout.ScreenFor(R(200, -900, 1000, -300), screens) == 1
                     && StageLayout.ScreenFor(R(-900, 200, -100, 900), screens) == 2,
            "écran d'une fenêtre : principal, celui du haut, le portrait à gauche (coordonnées négatives)");
        Check(lines, StageLayout.ScreenFor(R(1500, -200, 2400, 300), screens) == 0 && StageLayout.ScreenFor(R(-300, 100, 400, 600), screens) == 0
                     && StageLayout.ScreenFor(R(-700, 100, 100, 600), screens) == 2,
            "fenêtre à cheval : l'écran qui en contient le plus");
        Check(lines, StageLayout.ScreenFor(R(5000, 5000, 5400, 5300), screens) == 0 && StageLayout.ScreenFor(R(-3000, -3000, -2800, -2900), screens) == 2
                     && StageLayout.ScreenFor(R(0, 0, 10, 10), []) == -1,
            "fenêtre hors de tous les écrans : le plus proche ; aucun écran = aucun");

        // Place au centre : la colonne reste toujours libre, à toutes les échelles.
        bool columnFree = true, centered = true, kept = true, shrunk = true, filled = true, stubborn = true;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            foreach (var work in new[] { R(0, 0, 1920, 1032), R(0, -1080, 1920, 0), R(-1080, -420, 0, 1452), R(0, 28, 1920, 1000) })
            {
                var stage = StageLayout.StageArea(work, scale);
                int reserve = StageLayout.ColumnReserve(scale);
                columnFree &= stage.Left >= work.Left + reserve && stage.Right <= work.Right && stage.Top >= work.Top && stage.Bottom <= work.Bottom;
                int sw = stage.Right - stage.Left, sh = stage.Bottom - stage.Top;
                var small = StageLayout.CenterPlace(stage, 800, 600, true, false);
                kept &= small.Right - small.Left == Math.Min(800, sw) || 800 > sw;
                centered &= 800 > sw || 600 > sh || Math.Abs((small.Left + small.Right) / 2 - (stage.Left + stage.Right) / 2) <= 1
                            && Math.Abs((small.Top + small.Bottom) / 2 - (stage.Top + stage.Bottom) / 2) <= 1 && small.Left >= stage.Left;
                var big = StageLayout.CenterPlace(stage, 5000, 4000, true, false);
                shrunk &= big.Right - big.Left == (int)Math.Round(sw * StageShape.Shrink) && big.Bottom - big.Top == (int)Math.Round(sh * StageShape.Shrink)
                          && big.Left >= stage.Left && big.Right <= stage.Right;
                var max = StageLayout.CenterPlace(stage, 1920, 1080, true, true);
                filled &= max.Left == stage.Left && max.Top == stage.Top && max.Right == stage.Right && max.Bottom == stage.Bottom;
                var fixedSize = StageLayout.CenterPlace(stage, 5000, 4000, false, false);
                stubborn &= fixedSize.Right - fixedSize.Left == 5000 && fixedSize.Left == stage.Left && fixedSize.Top == stage.Top;
            }
        }
        Check(lines, columnFree, "scène = zone de travail moins la colonne à gauche (4 zones de travail × 100 à 200 %)");
        Check(lines, kept && centered, "fenêtre qui tient : taille gardée, centrée dans la scène");
        Check(lines, shrunk, "fenêtre trop grande : ramenée à 80 % de la scène, sans déborder");
        Check(lines, filled, "fenêtre agrandie : elle remplit la scène (plus agrandie : la colonne reste libre)");
        Check(lines, stubborn, "fenêtre qui ne se redimensionne pas : taille gardée, collée en haut à gauche de la scène");
        Check(lines, StageLayout.MaxGroups(1080, 1) == 6 && StageLayout.MaxGroups(1920, 1) == 6 && StageLayout.MaxGroups(1080, 2) == 3
                     && StageLayout.MaxGroups(300, 2) == 1,
            "groupes qui tiennent : 6 au plus (1080 ou portrait 1920 à 100 %), 3 à 200 %, 1 au moins");

        // Groupes de la colonne.
        IntPtr H(int v) => new(v);
        var recent = new List<(IntPtr, string)> { (H(1), "exe:chrome"), (H(2), "exe:code"), (H(3), "exe:chrome"), (H(4), "exe:spotify"), (H(5), "exe:code"), (H(6), "app:notes") };
        var groups = StageLayout.Groups(recent, H(2), 6);
        Check(lines, groups.Count == 4 && groups[0].Key == "exe:chrome" && groups[0].Windows.SequenceEqual([H(1), H(3)])
                     && groups[1].Key == "exe:spotify" && groups[2].Key == "exe:code" && groups[2].Windows.SequenceEqual([H(5)])
                     && groups[3].Key == "app:notes",
            "colonne : groupées par appli, la plus récente d'abord, fenêtre du centre retirée (son appli reste si elle a d'autres fenêtres)");
        Check(lines, StageLayout.Groups(recent, IntPtr.Zero, 2).Count == 2 && StageLayout.Groups([], H(1), 6).Count == 0
                     && StageLayout.Groups([(H(1), "a"), (H(1), "a")], IntPtr.Zero, 6)[0].Windows.Count == 1,
            "colonne : nombre de groupes limité, vide sans fenêtre, une fenêtre jamais en double");
        Check(lines, StageLayout.AppKey("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", @"C:\x\a.exe", H(7)).StartsWith("app:")
                     && StageLayout.AppKey(null, @"C:\Program Files\Google\Chrome.EXE", H(7)) == @"exe:c:\program files\google\chrome.exe"
                     && StageLayout.AppKey(null, null, H(7)) == "win:7",
            "clé d'appli : appli du Store, sinon exe (sans majuscules), sinon la fenêtre seule");

        // Exclusions.
        var normal = new StageFlags(false, false, true, false, false, false, false, false);
        Check(lines, StageLayout.Exclusion(normal) == StageExclusion.None
                     && StageLayout.Exclusion(normal with { Own = true }) == StageExclusion.Own
                     && StageLayout.Exclusion(normal with { ToolWindow = true }) == StageExclusion.Tool
                     && StageLayout.Exclusion(normal with { Minimizable = false }) == StageExclusion.Tool
                     && StageLayout.Exclusion(normal with { Topmost = true }) == StageExclusion.Topmost
                     && StageLayout.Exclusion(normal with { Fullscreen = true }) == StageExclusion.Fullscreen
                     && StageLayout.Exclusion(normal with { Game = true }) == StageExclusion.Fullscreen
                     && StageLayout.Exclusion(normal with { Elevated = true }) == StageExclusion.Elevated
                     && StageLayout.Exclusion(normal with { Ignored = true, Elevated = true }) == StageExclusion.Ignored,
            "exclusions : Opaline, fenêtre outil ou non réductible, toujours au premier plan, plein écran / jeu, administrateur, appli ignorée");
        Check(lines, StageLayout.TakesCenter(true, H(1), H(2), false) && !StageLayout.TakesCenter(true, H(1), H(1), false)
                     && !StageLayout.TakesCenter(false, H(1), H(2), false) && !StageLayout.TakesCenter(true, H(1), H(2), true)
                     && StageLayout.TakesCenter(true, H(1), IntPtr.Zero, false),
            "fenêtre au premier plan : prend le centre (pas déjà au centre, gérée, pas pendant un jeu sur cet écran)");
        var withBorders = StageLayout.WithBorders(R(100, 100, 900, 700), R(-7, 0, 807, 607), R(0, 0, 800, 600));
        Check(lines, withBorders.Left == 93 && withBorders.Top == 100 && withBorders.Right == 907 && withBorders.Bottom == 707,
            "bords invisibles de Windows 11 ajoutés autour de la partie visible");

        // Colonne auto-masquée : scène = toute la zone de travail, à toutes les échelles.
        bool wide = true;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var work = R(0, 0, 1920, 1032);
            int margin = (int)Math.Round(StageShape.ScreenMargin * scale);
            var full = StageLayout.StageArea(work, scale, autoHide: true);
            var reduced = StageLayout.StageArea(work, scale);
            var filledFull = StageLayout.CenterPlace(full, 800, 600, true, true);
            wide &= full.Left == work.Left + margin && full.Right == work.Right - margin && full.Top == reduced.Top && full.Bottom == reduced.Bottom
                    && reduced.Left > full.Left && filledFull.Left == full.Left && filledFull.Right == full.Right;
        }
        Check(lines, wide && StageLayout.ColumnTop(R(0, 0, 1920, 1032), 432) == 300 && StageLayout.ColumnTop(R(0, 28, 1920, 300), 600) == 28,
            "colonne auto-masquée : la scène (et la fenêtre du centre en plein écran) prend toute la largeur ; colonne centrée en hauteur");

        // Décisions de l'auto-masquage, avec les écrans simulés (portrait collé à gauche du principal, rien à gauche du portrait).
        var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        DateTime At(int ms) => t0.AddMilliseconds(ms);
        POINT P(int x, int y) => new() { X = x, Y = y };
        bool neighborPrimary = StageAutoHide.NeighborLeftAt(primary, screens, 500);
        bool neighborPortrait = StageAutoHide.NeighborLeftAt(left, screens, 500);
        bool neighborAbove = StageAutoHide.NeighborLeftAt(top, screens, -500); // le portrait commence à y = -420 : rien à côté de l'écran du haut à y = -500
        Check(lines, neighborPrimary && !neighborPortrait && !neighborAbove && StageAutoHide.NeighborLeftAt(top, screens, -300),
            "écran voisin à gauche : le portrait à gauche du principal ; rien à gauche du portrait ; écran du haut seulement là où le portrait le touche");
        StageEdgeInput In(int ms, POINT cursor, RECT screen, bool neighbor, bool over = false, bool gaming = false, bool groups = true) =>
            new(At(ms), cursor, screen, 300, 732, neighbor, over, gaming, groups);

        // Vrai bord (portrait, rien à sa gauche) : tout de suite ; puis 700 ms après le départ de la souris.
        var edge = new StageAutoHide();
        bool realEdge = edge.Update(In(0, P(-1080, 500), left, false)) && edge.Update(In(100, P(-900, 500), left, false))
                        && edge.Update(In(699, P(-900, 500), left, false)) && !edge.Update(In(800, P(-900, 500), left, false)) && !edge.Pending;
        var outside = new StageAutoHide();
        bool band = !outside.Update(In(0, P(-1078, 500), left, false)) // 3e pixel : hors de la bande
                    && !outside.Update(In(10, P(-1080, 200), left, false)) // au-dessus de la colonne
                    && !outside.Update(In(20, P(-1080, 732), left, false)) // juste en dessous
                    && outside.Update(In(30, P(-1079, 731), left, false));
        Check(lines, realEdge && band, "auto-masquage, vrai bord : la souris au bord (2 px, hauteur de la colonne) la montre tout de suite, cachée 700 ms après son départ");

        // Écran voisin à gauche (principal) : il faut s'arrêter 250 ms au bord ; passer vers l'écran voisin ne la montre pas.
        var dwell = new StageAutoHide();
        bool waits = !dwell.Update(In(0, P(0, 500), primary, true)) && !dwell.Update(In(200, P(1, 500), primary, true))
                     && dwell.Update(In(260, P(1, 500), primary, true));
        var passing = new StageAutoHide();
        bool crossed = !passing.Update(In(0, P(0, 500), primary, true)) && !passing.Update(In(60, P(-50, 500), primary, true))
                       && !passing.Update(In(400, P(0, 500), primary, true)) && !passing.Update(In(500, P(-80, 500), primary, true));
        Check(lines, waits && crossed, "auto-masquage, écran voisin à gauche : 250 ms d'arrêt au bord ; souris qui passe sur l'autre écran = rien");

        // Souris sur la colonne : elle reste ; clic sur une miniature : cachée tout de suite (et pas de retour sans quitter le bord).
        var stay = new StageAutoHide();
        stay.Update(In(0, P(-1080, 500), left, false));
        bool stays = stay.Update(In(500, P(-900, 500), left, false, over: true)) && stay.Update(In(3000, P(-900, 500), left, false, over: true))
                     && stay.Update(In(3600, P(-500, 500), left, false)) && !stay.Update(In(3800, P(-500, 500), left, false));
        var click = new StageAutoHide();
        click.Update(In(0, P(-1080, 500), left, false));
        click.HideNow();
        bool clicked = !click.Visible && !click.Update(In(50, P(-1080, 500), left, false, over: true))
                       && !click.Update(In(100, P(-900, 500), left, false)) && click.Update(In(150, P(-1080, 500), left, false));
        Check(lines, stays && clicked, "auto-masquage : reste tant que la souris est sur la colonne ; clic = cachée tout de suite, revient au bord suivant");

        // Fenêtre partie dans la colonne : montrée ~1,5 s ; jeu sur l'écran ou colonne vide : jamais.
        var flash = new StageAutoHide();
        flash.Flash(At(0));
        bool flashed = flash.Pending && flash.Update(In(10, P(900, 500), primary, true)) && flash.Update(In(1400, P(900, 500), primary, true))
                       && !flash.Update(In(1600, P(900, 500), primary, true)) && !flash.Pending;
        var game = new StageAutoHide();
        game.Flash(At(0));
        bool noGame = !game.Update(In(10, P(-1080, 500), left, false, gaming: true)) && !game.Pending
                      && !new StageAutoHide().Update(In(0, P(-1080, 500), left, false, groups: false));
        Check(lines, flashed && noGame, "auto-masquage : montrée 1,5 s quand une fenêtre y part ; jamais pendant un jeu sur cet écran ni sans miniature");

        // Poignée (bord partagé avec un autre écran : la souris n'y « s'arrête » jamais).
        bool handleRules = StageAutoHide.HandleVisible(true, true, false, false)
                           && !StageAutoHide.HandleVisible(false, true, false, false) // option coupée : colonne toujours là
                           && !StageAutoHide.HandleVisible(true, false, false, false) // pas de miniature
                           && !StageAutoHide.HandleVisible(true, true, true, false)   // jeu sur cet écran
                           && !StageAutoHide.HandleVisible(true, true, false, true);  // colonne déjà sortie
        var zone = StageAutoHide.HandleRect(primary, 300, 732, 1.0);
        var zone150 = StageAutoHide.HandleRect(primary, 300, 732, 1.5);
        var zoneLow = StageAutoHide.HandleRect(R(0, 0, 1920, 400), 300, 732, 1.0);
        bool handleZone = zone.Left == primary.Left && zone.Right - zone.Left == 14 && zone.Bottom - zone.Top == 120 && zone.Top == 456
                          && zone150.Right - zone150.Left == 21 && zone150.Bottom - zone150.Top == 180 && zoneLow.Bottom == 400
                          && StageAutoHide.Inside(P(0, 500), zone) && !StageAutoHide.Inside(P(14, 500), zone) && !StageAutoHide.Inside(P(5, 300), zone);
        StageEdgeInput OnHandle(int ms, POINT cursor, bool over, bool overColumn = false) =>
            new(At(ms), cursor, primary, 300, 732, true, overColumn, false, true, over);
        var hover = new StageAutoHide();
        bool hovered = !hover.Update(OnHandle(0, P(5, 500), true)) && !hover.Update(OnHandle(100, P(5, 500), true)) && hover.Update(OnHandle(130, P(5, 500), true))
                       && hover.Update(OnHandle(700, P(8, 500), true)) // toujours sur la poignée : reste
                       && hover.Update(OnHandle(1000, P(150, 500), false, overColumn: true)) // vers la colonne
                       && hover.Update(OnHandle(1600, P(900, 500), false)) && !hover.Update(OnHandle(1750, P(900, 500), false)); // 700 ms après
        var brush = new StageAutoHide();
        bool brushed = !brush.Update(OnHandle(0, P(5, 500), true)) && !brush.Update(OnHandle(60, P(-40, 500), false)) && !brush.Update(OnHandle(200, P(-40, 500), false));
        var clickHandle = new StageAutoHide();
        clickHandle.Flash(At(0), StageAutoHide.FlashTime);
        bool clickShows = clickHandle.Update(OnHandle(0, P(5, 500), true)) && clickHandle.Update(OnHandle(1400, P(900, 500), false))
                          && !clickHandle.Update(OnHandle(1600, P(900, 500), false));
        var peek = new StageAutoHide();
        peek.Flash(At(0), StageAutoHide.PeekTime);
        bool peeked = peek.Update(OnHandle(3900, P(900, 500), false)) && !peek.Update(OnHandle(4100, P(900, 500), false));
        var afterClick = new StageAutoHide();
        afterClick.Update(OnHandle(0, P(5, 500), true));
        afterClick.Update(OnHandle(200, P(5, 500), true));
        afterClick.HideNow();
        bool suppressed = !afterClick.Update(OnHandle(300, P(5, 500), true)) && !afterClick.Update(OnHandle(400, P(500, 500), false))
                          && !afterClick.Update(OnHandle(500, P(5, 500), true)) && afterClick.Update(OnHandle(650, P(5, 500), true));
        Check(lines, handleRules && handleZone && hovered && brushed && clickShows && peeked && suppressed,
            "poignée : visible seulement auto-masquée avec miniatures, hors jeu et colonne rentrée ; zone 14 × 120 au bord, centrée ; survol 120 ms = la colonne sort, simple passage = rien ; clic 1,5 s ; palette 4 s");

        // Règle « ignorer » et réglages.
        var rules = AppRule.NormalizeList([new AppRule { Process = "Chrome.exe", StageIgnore = true }]);
        var stageSettings = new StageSettings { Hotkey = "Rien+X" };
        stageSettings.Normalize();
        Check(lines, rules.Count == 1 && rules[0].StageIgnore && AppRule.For(rules, "chrome")?.StageIgnore == true
                     && !new StageSettings().Enabled && new StageSettings().Hotkey == "" && stageSettings.Hotkey == "",
            "règle « Stage Manager : ignorer » gardée ; mode coupé et sans raccourci par défaut ; raccourci illisible oublié");

        // Fichier d'état : aller-retour dans un fichier temporaire.
        var entries = new List<StageEntry>
        {
            new(0x1234, 4321, true, true, R(10, 20, 810, 620), null),
            new(-77, 99, false, false, R(-1000, -400, -200, 100), R(300, 40, 1400, 900)),
        };
        var parsed = StageState.Parse(StageState.Format(entries));
        Check(lines, parsed.SequenceEqual(entries), "état de Stage Manager : écrit puis relu à l'identique (handles, coordonnées négatives)");
        var damaged = StageState.Parse("# Opaline Stage Manager 1\nabc def\n12 34 min max 1 2 3\n" + StageState.Format(entries.Take(1)).Split('\n')[1] + "\n");
        Check(lines, damaged.Count == 1 && damaged[0] == entries[0], "état abîmé : lignes illisibles ignorées, les bonnes gardées");
        var path = Path.Combine(Path.GetTempPath(), $"opaline-stage-test-{Environment.ProcessId}.txt");
        try
        {
            bool saved = StageState.Save(entries, path) && File.Exists(path);
            bool loaded = StageState.Load(path).SequenceEqual(entries);
            bool emptied = StageState.Save([], path) && !File.Exists(path) && StageState.Load(path).Count == 0;
            Check(lines, saved && loaded && emptied, "fichier d'état temporaire : enregistré, relu, effacé quand il n'y a plus rien");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // Ce qui est rendu (décision pure) : seulement les fenêtres encore là, du même processus, pas changées depuis.
        var min = entries[0];
        var moved = entries[1];
        Check(lines, StageState.Plan(min, true, 4321, true, null) == StageRestore.Show
                     && StageState.Plan(min, false, 0, false, null) == StageRestore.Nothing
                     && StageState.Plan(min, true, 5555, true, null) == StageRestore.Nothing
                     && StageState.Plan(min, true, 4321, false, R(0, 0, 10, 10)) == StageRestore.Nothing,
            "fenêtre réduite par nous : rendue si toujours réduite ; fermée, handle réutilisé ou déjà rouverte = rien");
        Check(lines, StageState.Plan(moved, true, 99, false, R(302, 38, 1401, 903)) == StageRestore.PutBack
                     && StageState.Plan(moved, true, 99, false, R(600, 40, 1700, 900)) == StageRestore.Nothing
                     && StageState.Plan(moved, true, 99, true, R(302, 38, 1401, 903)) == StageRestore.Nothing
                     && StageState.Plan(moved with { Placed = null }, true, 99, false, R(0, 0, 1, 1)) == StageRestore.Nothing,
            "fenêtre déplacée au centre : remise à sa place si elle n'a pas bougé depuis (4 px près) ; déplacée ou réduite par l'utilisateur = rien");
    }
}
