using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>Auto-test de la priorité 2 côté île et paramètres : règles par application, onglets, taille, recherche (calculs purs).</summary>
internal static partial class SelfTest
{
    private static void Island2(List<string> lines)
    {
        // Règles par application (point 13).
        var rules = AppRule.NormalizeList(
        [
            new AppRule { Process = @"C:\Program Files\Google\Chrome\Application\Chrome.exe", Island = AppBehavior.Hide },
            new AppRule { Process = "chrome", Dock = AppBehavior.Show }, // doublon : le premier gagne
            new AppRule { Process = "Spotify.exe", Name = "Spotify", Island = AppBehavior.Show, Dock = AppBehavior.Show },
            new AppRule { Process = "  " },
        ]);
        Check(lines, rules.Count == 2 && rules[0].Process == "chrome" && rules[0].Name == "chrome" && rules[1].Process == "spotify"
                     && AppRule.For(rules, "CHROME")?.Island == AppBehavior.Hide && AppRule.For(rules, "notepad") == null,
            "règles : nom de processus nettoyé (chemin, .exe, majuscules), une règle par appli, vides retirées");
        Check(lines, AppRuleService.Combine(AppBehavior.Hide, AppBehavior.Show) == AppBehavior.Show
                     && AppRuleService.Combine(AppBehavior.Default, AppBehavior.Hide) == AppBehavior.Hide
                     && AppRuleService.Combine(AppBehavior.Default, AppBehavior.Default) == AppBehavior.Default,
            "règles : « toujours visible » l'emporte sur « se cache » (appli au premier plan + appli sous l'île)");

        // Onglets (point 12).
        var tabs = IslandTabItem.NormalizeList(
        [
            new IslandTabItem { Id = IslandTabItem.Health, Visible = false },
            new IslandTabItem { Id = "inconnu" },
            new IslandTabItem { Id = IslandTabItem.Music, Visible = false },
        ]);
        Check(lines, tabs.Count == IslandTabItem.All.Length && tabs[0].Id == IslandTabItem.Health && tabs[1].Id == IslandTabItem.Music
                     && tabs.Skip(2).All(t => t.Visible),
            "onglets : ordre gardé, inconnus retirés, nouveaux ajoutés à la fin");
        var none = IslandTabItem.NormalizeList(IslandTabItem.All.Select(id => new IslandTabItem { Id = id, Visible = false }).ToList());
        Check(lines, none.Count(t => t.Visible) == 1 && none.First(t => t.Visible).Id == IslandTabItem.Music,
            "onglets : tout caché → la Musique revient (jamais une île sans onglet)");

        // Taille (point 12) et raccourci (point 14).
        var island = new IslandSettings { Scale = 3, ToggleHotkey = "pas un raccourci", Mode = (IslandMode)42 };
        island.Normalize();
        var free = new IslandSettings { Scale = 0.851, ToggleHotkey = "" };
        free.Normalize();
        Check(lines, island.Scale == IslandSettings.MaxScale && island.ToggleHotkey == Hotkeys.DefaultIsland && island.Mode == IslandMode.Fixed
                     && free.Scale == 0.85 && free.ToggleHotkey == "",
            "île : taille bornée (80–125 %), raccourci invalide → par défaut, vide = aucun, mode inconnu → Fixe");

        // Recherche dans les paramètres (point 11).
        Check(lines, Settings.SettingsWindow.Simplify("Fond d'écran — Garder") == "fond d ecran garder"
                     && Settings.SettingsWindow.Simplify("ÉTAGÈRE") == "etagere",
            "recherche : sans accents, majuscules ni ponctuation");
    }
}
