using System.Globalization;
using System.IO;
using System.Windows.Media;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Mode développeur <c>--snapshot &lt;dossier&gt; --self-test &lt;fichier&gt;</c> : vérifie les couleurs automatiques
/// SANS rien changer à Windows. Le registre est testé dans une clé à part (HKCU\Software\DynamicIsland-SelfTest,
/// supprimée à la fin) avec une sauvegarde temporaire ; les vraies valeurs ne sont que LUES.
/// </summary>
internal static partial class SelfTest
{
    private const string TestKey = @"Software\DynamicIsland-SelfTest";

    public static async Task<string> RunAsync()
    {
        var lines = new List<string> { $"Auto-test des couleurs automatiques — {DateTime.Now:yyyy-MM-dd HH:mm}" };
        await Task.Run(() =>
        {
            Run(lines, "Soleil", () => Sun(lines));
            Run(lines, "Fond d'écran", () => Wallpaper(lines));
            Run(lines, "Palette de Windows", () => WindowsPalette(lines));
            Run(lines, "Sauvegarde / restauration", () => RoundTrip(lines));
            Run(lines, "Placement de l'île (échelles 100 à 200 %, 1 à 4 écrans)", () => Placement(lines));
            Run(lines, "Fond d'écran d'origine (désinstallation)", () => WallpaperRestore(lines));
            Run(lines, "Batterie", () => Battery(lines));
            Run(lines, "Mini Claude : messages d'erreur", () => ClaudeErrors(lines));
            Run(lines, "Mini Claude : moteur clé API (sans vraie clé)", () => ClaudeApi(lines));
            Run(lines, "Langues (français / anglais)", () => Languages(lines));
            Run(lines, "Module qui plante : à qui est l'erreur", () => ModuleOwners(lines));
            Run(lines, "Sauvegardes des réglages", () => Backups(lines));
            Run(lines, "Rapport de bug sans données personnelles", () => BugReportPrivacy(lines));
            Run(lines, "Zone d'état du dock (lecture seule)", () => StatusZone(lines));
            Run(lines, "Île : appareils, téléchargements, horloges", () => IslandExtras(lines));
            Run(lines, "Télécommande : QR code, réseaux, sécurité", () => RemoteOffline(lines));
            Run(lines, "Éclairage RGB (G HUB cherché, OpenRGB simulé)", () => Rgb(lines));
            Run(lines, "Amis en ligne", () => Friends(lines));
            Run(lines, "Santé du PC (lecture seule)", () => Health(lines));
            Run(lines, "Thèmes saisonniers", () => Seasons(lines));
        });
        Run(lines, "Curseur de souris", () => Cursor(lines)); // dessin WPF : sur le thread de l'interface
        lines.Add("");
        lines.Add("== Télécommande : aller-retour HTTP sur 127.0.0.1 (faux état)");
        try
        {
            await RemoteRoundTripAsync(lines);
        }
        catch (Exception ex)
        {
            lines.Add($"ÉCHEC : {ex}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void Run(List<string> lines, string name, Action test)
    {
        lines.Add("");
        lines.Add($"== {name}");
        try
        {
            test();
        }
        catch (Exception ex)
        {
            lines.Add($"ÉCHEC : {ex}");
        }
    }

    private static void Sun(List<string> lines)
    {
        foreach (var date in new[] { DateOnly.FromDateTime(DateTime.Now), new DateOnly(2026, 6, 21), new DateOnly(2026, 12, 21) })
        {
            var sun = SunCalculator.For(date, SunCalculator.MontrealLatitude, SunCalculator.MontrealLongitude);
            lines.Add($"Montréal {date:yyyy-MM-dd} : lever {sun.Sunrise:HH:mm}, coucher {sun.Sunset:HH:mm}");
        }
        var north = SunCalculator.For(new DateOnly(2026, 6, 21), 78.22, 15.65); // Svalbard : jour polaire
        lines.Add($"Svalbard 2026-06-21 : {north.Polar}");
        lines.Add($"Maintenant à Montréal : {(SunCalculator.IsDay(DateTime.Now, SunCalculator.MontrealLatitude, SunCalculator.MontrealLongitude) ? "jour" : "nuit")}");
    }

    private static void Wallpaper(List<string> lines)
    {
        var path = WallpaperService.CurrentPath();
        lines.Add($"Fond actuel : {path ?? "(couleur unie)"}");
        if (path == null) return;
        var colors = WallpaperPalette.Extract(path);
        lines.Add(colors is { } c
            ? $"Accent {Swatch.Hex(c.Start)} → {Swatch.Hex(c.End)}, teinte de l'île {Swatch.Hex(c.Surface)}"
            : "Presque pas de couleur : on garde l'accent du profil");
    }

    /// <summary>Compare la palette générée par Windows pour sa couleur actuelle avec la nôtre (lecture seule).</summary>
    private static void WindowsPalette(List<string> lines)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        if (key?.GetValue("AccentPalette") is not byte[] { Length: 32 } bytes)
        {
            lines.Add("Pas de palette dans le registre.");
            return;
        }
        var windows = Enumerable.Range(0, 8).Select(i => Color.FromRgb(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2])).ToArray();
        var ours = WindowsAccentService.Palette(windows[3], windows[7]);
        lines.Add("Windows : " + string.Join(" ", windows.Select(Swatch.Hex)));
        lines.Add("Nous    : " + string.Join(" ", ours.Select(Swatch.Hex)));
        double worst = Enumerable.Range(0, 7).Max(i => Math.Abs(ColorTools.ToHsl(windows[i]).L - ColorTools.ToHsl(ours[i]).L));
        lines.Add($"Plus grand écart de luminosité : {worst * 100:0.0} points");
    }

    /// <summary>Synchronise puis remet, dans une clé de test : tout doit revenir à l'identique (absences comprises).</summary>
    private static void RoundTrip(List<string> lines)
    {
        var (oldRoot, oldFile) = (WindowsBackup.Root, WindowsBackup.FilePath);
        var file = Path.Combine(Path.GetTempPath(), $"dynamicisland-selftest-{Environment.ProcessId}.json");
        Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);
        var root = Registry.CurrentUser.CreateSubKey(TestKey);
        try
        {
            // Départ réaliste : copie des vraies valeurs (lues), sauf une, absente exprès.
            foreach (var (keyPath, name) in WindowsAccentService.Values.Concat(AutoThemeService.Values))
            {
                if (name == "StartColorMenu") continue;
                using var source = Registry.CurrentUser.OpenSubKey(keyPath);
                if (source?.GetValue(name) is not { } data) continue;
                using var target = root.CreateSubKey(keyPath);
                target.SetValue(name, data, source.GetValueKind(name));
            }
            WindowsBackup.Root = root;
            WindowsBackup.FilePath = file;
            var all = WindowsAccentService.Values.Concat(AutoThemeService.Values).ToArray();
            var before = Dump(root, all);

            WindowsAccentService.Sync(Color.FromRgb(0xFF, 0x2D, 0x95), broadcast: false);
            WindowsBackup.SaveIfMissing("Theme", AutoThemeService.Values);
            using (var personalize = root.CreateSubKey(AutoThemeService.Values[0].Key))
            {
                personalize.SetValue("AppsUseLightTheme", 1, RegistryValueKind.DWord);
                personalize.SetValue("SystemUsesLightTheme", 1, RegistryValueKind.DWord);
            }
            var during = Dump(root, all);
            lines.Add("Après synchro : " + Changed(before, during));
            Check(lines, during["AccentColorMenu"] == "DWord:" + unchecked((int)0xFF952DFF).ToString(CultureInfo.InvariantCulture), "accent écrit (0xFF952DFF)");
            Check(lines, during["AutoColorization"] == "DWord:0", "choix automatique de Windows coupé");
            Check(lines, File.Exists(file), "sauvegarde écrite");

            WindowsBackup.Restore("Accent");
            WindowsBackup.Restore("Theme");
            var after = Dump(root, all);
            Check(lines, all.All(v => before[v.Name] == after[v.Name]), "tout est revenu à l'identique : " + Changed(before, after));
            Check(lines, after["StartColorMenu"] == "(absente)", "une valeur absente au départ est supprimée");
            Check(lines, !File.Exists(file), "sauvegarde effacée une fois tout remis");
        }
        finally
        {
            WindowsBackup.Root = oldRoot;
            WindowsBackup.FilePath = oldFile;
            root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>Curseur : écrit dans une clé de test, fichiers .cur lus par Windows (sans les appliquer), puis tout remis.</summary>
    private static void Cursor(List<string> lines)
    {
        var (oldRoot, oldFile) = (WindowsBackup.Root, WindowsBackup.FilePath);
        var file = Path.Combine(Path.GetTempPath(), $"dynamicisland-selftest-curseur-{Environment.ProcessId}.json");
        var folder = Path.Combine(Path.GetTempPath(), $"dynamicisland-selftest-curseurs-{Environment.ProcessId}");
        Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);
        var root = Registry.CurrentUser.CreateSubKey(TestKey);
        try
        {
            foreach (var (keyPath, name) in CursorTheme.Values)
            {
                using var source = Registry.CurrentUser.OpenSubKey(keyPath);
                if (source?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } data) continue;
                using var target = root.CreateSubKey(keyPath);
                target.SetValue(name, data, source.GetValueKind(name));
            }
            WindowsBackup.Root = root;
            WindowsBackup.FilePath = file;
            var before = Dump(root, CursorTheme.Values);

            CursorTheme.Install(folder, Color.FromRgb(0x4F, 0x7B, 0xFF), Color.FromRgb(0xA0, 0x5C, 0xFF), reload: false);
            using (var key = root.OpenSubKey(CursorTheme.Values[0].Key))
            {
                foreach (var name in new[] { "Arrow", "Hand", "IBeam" })
                {
                    var path = key?.GetValue(name) as string ?? "";
                    var handle = File.Exists(path) ? LoadCursorFromFile(path) : IntPtr.Zero;
                    Check(lines, handle != IntPtr.Zero, $"{name} : {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024} Ko) lu par Windows");
                    if (handle != IntPtr.Zero) DestroyCursor(handle);
                }
                Check(lines, key?.GetValue("") as string == AppInfo.Name, $"jeu de curseurs nommé « {AppInfo.Name} »");
            }

            WindowsBackup.Restore(CursorTheme.Section);
            var after = Dump(root, CursorTheme.Values);
            Check(lines, CursorTheme.Values.All(v => before[v.Name] == after[v.Name]), "tout est revenu à l'identique : " + Changed(before, after));
        }
        finally
        {
            WindowsBackup.Root = oldRoot;
            WindowsBackup.FilePath = oldFile;
            root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(TestKey, throwOnMissingSubKey: false);
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadCursorFromFile(string path);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr cursor);

    private static Dictionary<string, string> Dump(RegistryKey root, (string Key, string Name)[] values) =>
        values.ToDictionary(v => v.Name, v =>
        {
            using var key = root.OpenSubKey(v.Key);
            var data = key?.GetValue(v.Name);
            if (key == null || data == null) return "(absente)";
            return $"{key.GetValueKind(v.Name)}:" + (data is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(data, CultureInfo.InvariantCulture));
        });

    private static string Changed(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var names = a.Keys.Where(k => a[k] != b[k]).ToList();
        return names.Count == 0 ? "aucune différence" : string.Join(", ", names) + " modifiées";
    }

    private static void Check(List<string> lines, bool ok, string what) => lines.Add($"{(ok ? "OK" : "ÉCHEC")} : {what}");

    // ------------------------------------------------------------------ 5H : configurations, désinstallation

    /// <summary>
    /// Écrans simulés à cette échelle : le principal, un écran en portrait à gauche (coordonnées négatives), un au-dessus,
    /// et un portable 1920 × 1080 à droite (à 200 % il ne fait que 960 × 540 unités). Barre des tâches de 48 px en bas.
    /// </summary>
    private static IEnumerable<ScreenInfo.Monitor> TestScreens(double scale)
    {
        static Interop.NativeMethods.RECT Rect(int left, int top, int width, int height) =>
            new() { Left = left, Top = top, Right = left + width, Bottom = top + height };
        int w = (int)(1920 * scale), h = (int)(1080 * scale), taskbar = (int)(48 * scale);
        var primary = Rect(0, 0, w, h);
        var work = primary;
        work.Bottom -= taskbar;
        yield return new ScreenInfo.Monitor(IntPtr.Zero, "TEST1", primary, work, scale, true);
        var left = Rect(-h, -(w - h) / 2, h, w);
        yield return new ScreenInfo.Monitor(IntPtr.Zero, "TEST2", left, left, scale, false);
        var top = Rect(0, -h, w, h);
        yield return new ScreenInfo.Monitor(IntPtr.Zero, "TEST3", top, top, scale, false);
        var laptop = Rect(w, 0, 1920, 1080);
        yield return new ScreenInfo.Monitor(IntPtr.Zero, "TEST4", laptop, laptop, scale, false);
    }

    /// <summary>
    /// L'île lâchée partout (grille de 9 × 7 points par écran) : la pilule reste dans l'écran, revient sous le curseur
    /// quand aucun bord ni aimant n'est proche, et l'île ouverte la plus grande tient dans l'écran.
    /// </summary>
    private static void Placement(List<string> lines)
    {
        var window = new System.Windows.Size(560, 320); // taille de la fenêtre de l'île (IslandWindow.xaml)
        var pill = IslandShape.Compact;
        double openWidth = IslandShape.MaxExpandedWidth, openHeight = IslandShape.Calendar.Height;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            int drops = 0, outside = 0, notUnder = 0, notFitting = 0;
            double worst = 0;
            foreach (var screen in TestScreens(scale))
            {
                double left = screen.Bounds.Left / scale, top = screen.Bounds.Top / scale;
                double right = screen.Bounds.Right / scale, bottom = screen.Bounds.Bottom / scale;
                for (int ix = 0; ix <= 8; ix++)
                {
                    for (int iy = 0; iy <= 6; iy++)
                    {
                        var drop = new Interop.NativeMethods.POINT
                        {
                            X = screen.Bounds.Left + (int)(screen.WidthPx * (0.02 + 0.96 * ix / 8)),
                            Y = screen.Bounds.Top + (int)(screen.HeightPx * (0.02 + 0.96 * iy / 6)),
                        };
                        var place = IslandWindow.PlacementFor(screen, drop);
                        var origin = IslandWindow.WindowOrigin(place, screen, window);
                        var center = IslandWindow.PillCenter(place, origin, window);
                        drops++;

                        // La pilule entière dans l'écran.
                        if (center.X - pill.Width / 2 < left - 0.5 || center.X + pill.Width / 2 > right + 0.5
                            || center.Y - pill.Height / 2 < top - 0.5 || center.Y + pill.Height / 2 > bottom + 0.5) outside++;

                        // Loin des bords et des aimants : exactement sous le curseur (au pixel près).
                        double dx = drop.X / scale - left, dy = drop.Y / scale - top;
                        double width = right - left, height = bottom - top;
                        bool free = dx > openWidth / 2 + 1 && width - dx > openWidth / 2 + 1 && Math.Abs(dx - width / 2) > IslandShape.SnapDistance
                                    && dy > 80 && height - dy > 80 && Math.Abs(dy - height / 2) > 1;
                        if (free)
                        {
                            double gap = Math.Max(Math.Abs(center.X * scale - drop.X), Math.Abs(center.Y * scale - drop.Y));
                            worst = Math.Max(worst, gap);
                            if (gap > 1.5 * scale) notUnder++;
                        }

                        // L'île ouverte la plus grande, qui grandit depuis la pilule vers l'intérieur, tient dans l'écran.
                        double openLeft = place.Horizontal switch
                        {
                            HorizontalAnchor.Left => center.X - pill.Width / 2,
                            HorizontalAnchor.Right => center.X + pill.Width / 2 - openWidth,
                            _ => center.X - openWidth / 2,
                        };
                        double openTop = place.Vertical == VerticalAnchor.Bottom ? center.Y + pill.Height / 2 - openHeight : center.Y - pill.Height / 2;
                        if (openLeft < left - 0.5 || openLeft + openWidth > right + 0.5 || openTop < top - 0.5 || openTop + openHeight > bottom + 0.5) notFitting++;
                    }
                }
            }
            Check(lines, outside == 0 && notUnder == 0 && notFitting == 0,
                $"{scale:P0} : {drops} lâchers sur 4 écrans — pilule hors écran {outside}, pas sous le curseur {notUnder} (écart max {worst:0.#} px), île ouverte qui dépasse {notFitting}");
        }

        // Une place enregistrée sur un grand écran, relue sur un petit : ramenée dedans.
        var small = TestScreens(2.0).Last();
        var far = new IslandPlacement { Horizontal = HorizontalAnchor.Center, X = 1500, Vertical = VerticalAnchor.Bottom, Y = 2000 };
        var c = IslandWindow.PillCenter(far, IslandWindow.WindowOrigin(far, small, window), window);
        Check(lines, c.X * 2 >= small.Bounds.Left && c.X * 2 <= small.Bounds.Right && c.Y * 2 >= small.Bounds.Top && c.Y * 2 <= small.Bounds.Bottom,
            "place trop loin pour un petit écran (portable à 200 %) : ramenée dedans");
    }

    /// <summary>Décision de la désinstallation pour le fond d'écran, dans tous les cas (rien n'est appliqué).</summary>
    private static void WallpaperRestore(List<string> lines)
    {
        string ours = Path.Combine(AppPaths.DataFolder, "Fonds", "aurore-1920x1080.png");
        string mine = @"C:\Images\vacances.jpg";
        string later = @"C:\Images\choisi-apres.jpg";
        string transcoded = @"C:\Users\test\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper";
        string backup = Path.Combine(AppPaths.DataFolder, "fond-d-origine.jpg");
        static List<WindowsBackup.Value> Saved(string? path) =>
            [new(@"Control Panel\Desktop", "Wallpaper", path == null ? null : RegistryValueKind.String, path)];
        bool Exists(string path) => path == mine;
        string? Plan(List<WindowsBackup.Value>? saved, bool changed, string? current, string? copy) =>
            WallpaperService.RestorePlan(saved, changed, current, copy, WallpaperService.IsOurs, Exists);

        Check(lines, WallpaperService.IsOurs(ours) && !WallpaperService.IsOurs(mine), "un fond du pack est reconnu comme venant de l'app, une photo perso non");
        Check(lines, Plan(null, false, ours, backup) == null, "fond jamais changé par l'app → rien n'est touché");
        Check(lines, Plan(Saved(mine), true, later, backup) == null, "fond choisi ensuite dans Windows → gardé");
        Check(lines, Plan(Saved(mine), true, null, backup) == null, "couleur unie choisie ensuite → gardée");
        Check(lines, Plan(Saved(mine), true, ours, backup) == mine, "fond d'origine remis (son fichier existe encore)");
        Check(lines, Plan(Saved(transcoded), true, ours, backup) == WallpaperService.BackupMarker, "copie interne de Windows écrasée → la copie sauvée (recopiée dans Images)");
        Check(lines, Plan(Saved(""), true, ours, backup) == "", "couleur unie à l'origine → couleur unie");
        Check(lines, Plan(null, true, ours, backup) == WallpaperService.BackupMarker, "réglages d'avant la 5H (sans section) → la copie sauvée");
        Check(lines, Plan(null, true, ours, null) == null, "aucune trace du fond d'origine → rien");
    }

    /// <summary>Erreurs de Claude Code traduites en message clair + bouton (dont celle vue chez l'utilisateur le 2026-10-25).</summary>
    private static void ClaudeErrors(List<string> lines)
    {
        const string tooOld = "API Error: 400 {\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Claude Code 2.1.92 does not support this model; "
                              + "version 2.1.280 or newer is required. Run 'claude update', or update the Claude desktop app, then try again.\",\"details\":{\"error_code\":\"claude_code_version_too_old\"}},\"request_id\":\"req_test\"}";
        var old = ClaudeService.Explain(tooOld);
        Check(lines, old.Fix == ClaudeFix.Update && old.Message.Contains("2.1.92") && old.Message.Contains("2.1.280") && !old.Message.Contains('{'),
            $"Claude Code trop ancien → « {old.Message} » + bouton de mise à jour");
        var login = ClaudeService.Explain("Not logged in · Please run /login");
        Check(lines, login.Fix == ClaudeFix.Login, $"pas connecté → « {login.Message} » + bouton de connexion");
        var other = ClaudeService.Explain("API Error: 529 {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}");
        Check(lines, other.Fix == ClaudeFix.None && other.Message.EndsWith("Overloaded") && !other.Message.Contains('{'), $"autre erreur de l'API → « {other.Message} » (sans JSON)");
    }

    /// <summary>Les deux fichiers de langue ont les mêmes clés, les mêmes {0}… et aucun texte vide.</summary>
    private static void Languages(List<string> lines)
    {
        var fr = Loc.All(Loc.French);
        var en = Loc.All(Loc.English);
        var onlyFr = fr.Keys.Except(en.Keys).ToList();
        var onlyEn = en.Keys.Except(fr.Keys).ToList();
        Check(lines, fr.Count > 300 && onlyFr.Count == 0 && onlyEn.Count == 0,
            $"{fr.Count} clés en français, {en.Count} en anglais" + (onlyFr.Count + onlyEn.Count > 0 ? $" — seulement fr : {string.Join(", ", onlyFr)} ; seulement en : {string.Join(", ", onlyEn)}" : ""));
        static int Holes(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"\{\d").Count;
        var mismatched = fr.Keys.Where(k => en.TryGetValue(k, out var e) && Holes(fr[k]) != Holes(e)).ToList();
        Check(lines, mismatched.Count == 0, "mêmes valeurs {0}, {1}… dans les deux langues" + (mismatched.Count > 0 ? " — " + string.Join(", ", mismatched) : ""));
        var empty = fr.Concat(en).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
        Check(lines, empty.Count == 0, "aucun texte vide" + (empty.Count > 0 ? " — " + string.Join(", ", empty) : ""));
        Check(lines, Loc.Resolve("fr") == Loc.French && Loc.Resolve("en") == Loc.English && Loc.Resolve("auto") is Loc.French or Loc.English,
            $"choix de la langue : « auto » → {Loc.Resolve("auto")} (Windows en {CultureInfo.CurrentUICulture.Name})");
    }

    private static void ModuleOwners(List<string> lines)
    {
        const string discord = "System.InvalidOperationException: test\r\n   at System.Text.Json.JsonNode.GetValue()\r\n"
                               + "   at DynamicIsland.Services.DiscordVoice.RunAsync() in DiscordVoice.cs:line 120\r\n   at DynamicIsland.App.OnStartup()";
        Check(lines, ModuleGuard.OwnerOf(discord) == ModuleGuard.Discord, "erreur dans DiscordVoice → module Discord");
        Check(lines, ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Dock.DockWindow.Refresh()") == ModuleGuard.Dock, "erreur dans le dock → module Dock");
        Check(lines, ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.App.OnStartup()\r\n   at System.Windows.Application.Run()") == null,
            "erreur hors module (démarrage) → aucun module arrêté");
        Check(lines, ModuleGuard.OwnerOf(typeof(GamingMode)) == ModuleGuard.Gaming && ModuleGuard.OwnerOf(typeof(SelfTest)) == null,
            "gestionnaire de réglages : type du mode Gaming → module Gaming, autre type → aucun");
    }

    /// <summary>Copies dans un dossier temporaire : jamais deux copies identiques de suite, 10 gardées au plus.</summary>
    private static void Backups(List<string> lines)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DynamicIsland-selftest-sauvegardes-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(folder, "source.json");
        try
        {
            Directory.CreateDirectory(folder);
            var copies = Path.Combine(folder, "copies");
            var start = new DateTime(2026, 1, 1, 12, 0, 0);
            for (int i = 0; i < 13; i++)
            {
                File.WriteAllText(source, $"{{\"essai\": {i}}}");
                SettingsBackup.Copy(source, copies, start.AddHours(i));
            }
            var kept = SettingsBackup.List(copies);
            Check(lines, kept.Count == SettingsBackup.Kept, $"13 changements → {kept.Count} copies gardées (attendu : {SettingsBackup.Kept})");
            Check(lines, kept.Count > 0 && File.ReadAllText(kept[0].Path).Contains("12") && kept[0].When == start.AddHours(12),
                "la plus récente en premier, avec le dernier contenu");
            Check(lines, SettingsBackup.Copy(source, copies, start.AddHours(20)) == null && SettingsBackup.List(copies).Count == SettingsBackup.Kept,
                "réglages inchangés → pas de nouvelle copie");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void BugReportPrivacy(List<string> lines)
    {
        const string text = @"Erreur : C:\Users\testeur\AppData\Roaming\DynamicIsland\settings.json pour testeur@exemple.com, salon 123456789012345678, "
                            + "PC DESKTOP-ESSAI, session testeur";
        var clean = BugReport.Clean(text, @"C:\Users\testeur", "testeur", "DESKTOP-ESSAI");
        Check(lines, !clean.Contains("testeur", StringComparison.OrdinalIgnoreCase) && !clean.Contains("DESKTOP-ESSAI") && !clean.Contains("123456789012345678")
                     && clean.Contains("%USERPROFILE%"), $"journal nettoyé : « {clean} »");
        var settings = BugReport.CleanSettings(SettingsStore.ToJson());
        var current = SettingsStore.Current;
        bool noApps = current.Dock.Pinned.All(p => string.IsNullOrEmpty(p.Path) || !settings.Contains(p.Path.Replace(@"\", @"\\"), StringComparison.OrdinalIgnoreCase));
        bool noCity = string.IsNullOrWhiteSpace(current.Widgets.City) || !settings.Contains("\"City\"");
        Check(lines, noApps && noCity && !settings.Contains("\"ClientId\""), "réglages du rapport : ni applis épinglées, ni ville, ni ID Discord");
    }

    /// <summary>
    /// 6B : réglages de la zone d'état, langues du clavier, Windows Update (recherche hors ligne : on LIT le dernier scan de
    /// Windows, rien n'est téléchargé ni installé), corbeille (lue, jamais vidée), Téléchargements (dossier temporaire).
    /// </summary>
    private static void StatusZone(List<string> lines)
    {
        var messy = new List<Dock.StatusItem>
        {
            new() { Id = Dock.StatusItem.Trash, Visible = false }, new() { Id = "inconnu" }, new() { Id = Dock.StatusItem.Trash }, new() { Id = Dock.StatusItem.Keyboard },
        };
        var fixedList = Dock.StatusItem.NormalizeList(messy);
        Check(lines, string.Join(",", fixedList.Select(i => i.Id)) == "trash,keyboard,updates,downloads" && !fixedList[0].Visible,
            "réglages : ordre gardé, doublon et inconnu retirés, éléments manquants ajoutés à la fin");

        var layouts = KeyboardLayoutService.Installed();
        Check(lines, layouts.Count > 0 && layouts.All(l => l.Short.Length == 2 && l.Name.Length > 2),
            $"langues du clavier : {string.Join(" | ", layouts.Select(l => $"{l.Short} = {l.Name}"))}");

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (pending, restart) = WindowsUpdateService.Read(online: false);
        Check(lines, true, $"Windows Update (dernier scan, {watch.ElapsedMilliseconds} ms) : {pending.Count} en attente"
                           + (pending.Count > 0 ? $" ({string.Join(" ; ", pending.Take(3).Select(u => u.Title))})" : "") + $", redémarrage requis : {restart}");

        var bin = RecycleBinService.Query();
        Check(lines, bin != null, bin is { } b ? $"corbeille lue : {b.Count} élément(s), {b.Size / (1024 * 1024)} Mo" : "corbeille illisible");

        var folder = Path.Combine(Path.GetTempPath(), "DynamicIsland-selftest-telechargements-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var now = DateTime.Now;
            void Make(string name, int minutesAgo, bool hidden = false)
            {
                var path = Path.Combine(folder, name);
                File.WriteAllText(path, "essai");
                File.SetCreationTime(path, now.AddMinutes(-minutesAgo));
                File.SetLastWriteTime(path, now.AddMinutes(-minutesAgo));
                if (hidden) File.SetAttributes(path, FileAttributes.Hidden);
            }
            Make("ancien.pdf", 60);
            Make("recent.zip", 1);
            Make("en-cours.zip.crdownload", 0);
            Make("cache.tmp", 0);
            Make("cache.ini", 0, hidden: true);
            var recent = DownloadsService.Recent(5, folder);
            Check(lines, string.Join(",", recent.Select(e => e.Name)) == "recent.zip,ancien.pdf",
                $"Téléchargements : {string.Join(", ", recent.Select(e => e.Name))} (sans fichiers partiels ni cachés, du plus récent au plus ancien)");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 6C : requêtes d'appareils (lecture seule), téléchargement « terminé » ou non (dossier temporaire), fuseaux des horloges.
    /// </summary>
    private static void IslandExtras(List<string> lines)
    {
        Check(lines, true, "appareils : " + DeviceNotifier.DescribeAsync().GetAwaiter().GetResult());

        var folder = Path.Combine(Path.GetTempPath(), "DynamicIsland-selftest-termine-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            string done = Path.Combine(folder, "fini.pdf"), partial = Path.Combine(folder, "fini.pdf.crdownload"),
                empty = Path.Combine(folder, "vide.zip"), writing = Path.Combine(folder, "en-cours.zip");
            File.WriteAllText(done, "essai");
            File.WriteAllText(partial, "essai");
            File.WriteAllText(empty, "");
            File.WriteAllText(writing, "essai");
            using var writer = new FileStream(writing, FileMode.Open, FileAccess.Write, FileShare.Read); // un navigateur qui écrit encore
            Check(lines, DownloadNotifier.State(done) == DownloadNotifier.FileState.Complete
                         && DownloadNotifier.State(partial) == DownloadNotifier.FileState.Gone
                         && DownloadNotifier.State(empty) == DownloadNotifier.FileState.Busy
                         && DownloadNotifier.State(writing) == DownloadNotifier.FileState.Busy
                         && DownloadNotifier.State(Path.Combine(folder, "absent.pdf")) == DownloadNotifier.FileState.Gone,
                "téléchargement terminé seulement quand le fichier est complet et que plus personne ne l'écrit");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }

        var clocks = new[] { "Europe/Paris", "Asia/Tokyo", "America/Vancouver", "Asia/Kolkata", "Australia/Sydney" }
            .Select(zone => (zone, WorldClockService.Now(new WorldClock { Name = zone, TimeZone = zone }))).ToList();
        Check(lines, clocks.All(c => c.Item2 is { } now && Math.Abs(now.Offset) <= 26),
            "horloges (fuseaux IANA lus par Windows) : " + string.Join(", ", clocks.Select(c => c.Item2 is { } now
                ? $"{c.zone.Split('/')[1]} {now.Time:HH:mm} ({(now.Offset >= 0 ? "+" : "")}{now.Offset} h)" : $"{c.zone} introuvable")));
    }

    /// <summary>Batterie : affichée (barre de menus) et prise en compte (fond animé) seulement si le PC en a une.</summary>
    private static void Battery(List<string> lines)
    {
        bool read = GetSystemPowerStatus(out var power);
        bool hasBattery = read && power.BatteryFlag is not (128 or 255);
        lines.Add(read ? $"État d'alimentation : secteur {power.ACLineStatus}, BatteryFlag {power.BatteryFlag}" : "État d'alimentation illisible");
        lines.Add(hasBattery ? "Ce PC a une batterie : icône affichée, fond animé en pause sur batterie." : "Pas de batterie : aucune icône de batterie, le fond animé ne s'occupe pas de l'alimentation.");
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
