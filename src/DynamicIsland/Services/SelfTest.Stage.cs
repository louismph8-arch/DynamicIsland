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
