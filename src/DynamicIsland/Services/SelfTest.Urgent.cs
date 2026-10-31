namespace DynamicIsland.Services;

/// <summary>Auto-test des correctifs urgents (priorité 1) : décisions pures, rien n'est écrit ni changé dans Windows.</summary>
internal static partial class SelfTest
{
    private static void Urgent(List<string> lines)
    {
        var now = new DateTime(2026, 10, 28, 20, 0, 0, DateTimeKind.Utc);
        const int Pid = 4242;
        string running = $"running {Pid}";
        var crash = unchecked((int)0xE0434352); // exception .NET non attrapée
        var first = CrashWatch.Decide(running, Pid, crash, [], now);
        var third = CrashWatch.Decide(running, Pid, crash, [now.AddMinutes(-3), now.AddMinutes(-1)], now);
        var fourth = CrashWatch.Decide(running, Pid, crash, [now.AddMinutes(-5), now.AddMinutes(-3), now.AddMinutes(-1)], now);
        var old = CrashWatch.Decide(running, Pid, crash, [now.AddMinutes(-30), now.AddMinutes(-20), now.AddMinutes(-15)], now);
        Check(lines, first.Arguments == CrashWatch.AfterCrashArgument && third.Arguments == CrashWatch.AfterCrashArgument
                     && fourth.Arguments!.Contains("--safe-mode") && old.Arguments == CrashWatch.AfterCrashArgument && old.Recent.Count == 1,
            "gardien : relancée après un plantage (3 fois), mode sans échec au 4e en 10 min ; les vieux plantages ne comptent plus");
        Check(lines, CrashWatch.Decide("clean", Pid, crash, [], now).Arguments == null && CrashWatch.Decide(running, Pid, 0, [], now).Arguments == null
                     && CrashWatch.Decide(running, Pid, 1, [], now).Arguments == null && CrashWatch.Decide("running 99", Pid, crash, [], now).Arguments == null,
            "gardien : rien n'est relancé après Quitter, une fin de tâche (code 1) ou pour une autre instance");

        // 1 : l'île et ses onglets ne sont plus pris pour un autre module (palette), un onglet qui plante a son propre module.
        const string fromPalette = "System.Exception: x\r\n   at DynamicIsland.IslandWindow.PaletteAnchor()\r\n   at DynamicIsland.Palette.PaletteWindow.Open()";
        ModuleGuard.Fail(ModuleGuard.Island, new InvalidOperationException("auto-test : erreur d'essai dans l'île"));
        Check(lines, ModuleGuard.OwnerOf(fromPalette) == ModuleGuard.Island && !ModuleGuard.IsDisabled(ModuleGuard.Island),
            "erreur dans l'île appelée par la palette → l'île (journal seulement), plus la palette arrêtée");
        Check(lines, ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Views.WidgetsView.Refresh()\r\n   at DynamicIsland.IslandWindow.SelectTab()") == ModuleGuard.WidgetsTab
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Views.MusicView.Update()") == ModuleGuard.MusicTab,
            "erreur dans un onglet de l'île → cet onglet seulement (caché pour la session, l'île continue)");

        // 2 : causes d'échec d'enregistrement traduites.
        Check(lines, Settings.SettingsStore.Describe(new UnauthorizedAccessException()).Contains(AppPaths.DataFolder)
                     && Settings.SettingsStore.Describe(new System.IO.IOException("plein", unchecked((int)0x80070070))) == Loc.T("settings.save.full")
                     && Settings.SettingsStore.Describe(new System.IO.IOException("pris", unchecked((int)0x80070020))) == Loc.T("settings.save.locked"),
            "réglages : accès refusé, disque plein et fichier pris donnent un message clair");

        // 3 : fenêtre rendue comme avant.
        var normal = new Interop.NativeMethods.RECT { Left = 100, Top = 80, Right = 900, Bottom = 700 };
        var moved = normal with { Left = 0, Right = 800 };
        var max = new WindowActions.Placement(true, normal, now);
        Check(lines, !WindowActions.NeedsPutBack(null, false, normal) && !WindowActions.NeedsPutBack(max, true, normal)
                     && WindowActions.NeedsPutBack(max, false, normal) && WindowActions.NeedsPutBack(max with { Maximized = false }, false, moved),
            "dock : fenêtre agrandie qui revient « normale » ou place changée → remise comme avant ; rien à faire sinon");

        // 4 : réordonner = les voisines s'écartent d'une place.
        Check(lines, Dock.DockWindow.ShiftFor(2, 1, 3, 56) == -56 && Dock.DockWindow.ShiftFor(3, 1, 3, 56) == -56 && Dock.DockWindow.ShiftFor(4, 1, 3, 56) == 0
                     && Dock.DockWindow.ShiftFor(0, 2, 0, 56) == 56 && Dock.DockWindow.ShiftFor(1, 2, 0, 56) == 56 && Dock.DockWindow.ShiftFor(2, 2, 0, 56) == 0
                     && Dock.DockWindow.ShiftFor(5, 2, 2, 56) == 0,
            "dock : glisser vers la droite / la gauche écarte les bonnes icônes d'une place, rien hors de l'intervalle");

        // 6 : fond d'Opaline remis seulement quand il faut.
        Check(lines, WallpaperGuard.ShouldReimpose(@"C:\f.png", false, false, false, false, false)
                     && !WallpaperGuard.ShouldReimpose(null, false, false, false, false, false)
                     && !WallpaperGuard.ShouldReimpose(@"C:\f.png", true, false, false, false, false)
                     && !WallpaperGuard.ShouldReimpose(@"C:\f.png", false, true, false, false, false)
                     && !WallpaperGuard.ShouldReimpose(@"C:\f.png", false, false, true, false, false)
                     && !WallpaperGuard.ShouldReimpose(@"C:\f.png", false, false, false, true, false)
                     && !WallpaperGuard.ShouldReimpose(@"C:\f.png", false, false, false, false, true),
            "fond : remis s'il a changé ; jamais sans fond de profil, avec Wallpaper Engine, le fond animé, en jeu, ou juste après notre changement");
        Check(lines, WallpaperService.SameEverywhere(@"C:\A\f.png", [@"c:\a\F.png", @"C:\A\f.png"])
                     && !WallpaperService.SameEverywhere(@"C:\A\f.png", [@"C:\A\f.png", @"C:\Spotlight\x.jpg"])
                     && !WallpaperService.SameEverywhere(@"C:\A\f.png", []),
            "fond : un seul écran changé (ou une couleur unie) est vu");

        // 8 : appareils connectés (lecture seule).
        Check(lines, true, "appareils connectés : " + DeviceListService.DescribeAsync().GetAwaiter().GetResult());
    }
}
