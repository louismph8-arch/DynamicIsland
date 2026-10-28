using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// « Exporter un rapport de bug » (6A) : un .zip sur le Bureau, à envoyer à Louis. Dedans : la version, la configuration
/// de Windows (version, écrans, mémoire, processeur, carte graphique), les modules actifs et arrêtés, les réglages et le
/// journal — SANS données personnelles : nom d'utilisateur et du PC, chemins du profil, courriels, identifiants
/// Discord, salon vocal, applis ouvertes, ville de la météo, applis épinglées et noms de profils perso sont retirés.
/// </summary>
public static partial class BugReport
{
    private const int LegacyLogTail = 200 * 1024; // fin de l'ancien journal (%LOCALAPPDATA%), s'il existe encore

    /// <summary>Crée le rapport sur le Bureau et renvoie son chemin.</summary>
    public static string Create()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"Opaline-rapport-bug-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "rapport.txt", SystemReport());
        Add(zip, "reglages.json", CleanSettings(SettingsStore.ToJson()));
        foreach (var log in Log.Files())
            Add(zip, "journaux/" + Path.GetFileName(log), Clean(ReadShared(log)));
        if (File.Exists(AppPaths.LegacyLogFile))
        {
            var old = ReadShared(AppPaths.LegacyLogFile);
            Add(zip, "journaux/ancien-journal.txt", Clean(old.Length > LegacyLogTail ? old[^LegacyLogTail..] : old));
        }
        Log.Info("Rapport de bug créé");
        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>Lu même si l'app (ou le gardien) est en train d'écrire dedans.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ nettoyage

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Emails();

    [GeneratedRegex(@"\b\d{17,20}\b")]
    private static partial Regex DiscordIds();

    [GeneratedRegex(@"(Zone de notification : \d+ icône\(s\)).*")]
    private static partial Regex TrayIcons();

    [GeneratedRegex(@"(salon vocal[^=\r\n]*=\s*).*")]
    private static partial Regex VoiceChannel();

    [GeneratedRegex(@"(Mode Gaming : ).*")]
    private static partial Regex GameName();

    /// <summary>Retire les données personnelles d'un texte (journal). Vérifié par --self-test.</summary>
    internal static string Clean(string text) => Clean(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.UserName, Environment.MachineName);

    internal static string Clean(string text, string profile, string user, string machine)
    {
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = Emails().Replace(text, "<courriel>");
        text = DiscordIds().Replace(text, "<id>");
        text = TrayIcons().Replace(text, "$1");
        text = VoiceChannel().Replace(text, "$1<salon>");
        text = GameName().Replace(text, "$1<appli>");
        if (user.Length >= 3) text = Regex.Replace(text, $@"\b{Regex.Escape(user)}\b", "<utilisateur>", RegexOptions.IgnoreCase);
        if (machine.Length >= 3) text = Regex.Replace(text, $@"\b{Regex.Escape(machine)}\b", "<pc>", RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>
    /// Réglages sans données personnelles : applis épinglées et dossiers remplacés par leur nombre, profils sans leurs
    /// applis ni fonds (et sans nom s'il a été choisi par l'utilisateur), ville de la météo, ID Discord et téléphones de la
    /// télécommande retirés.
    /// </summary>
    internal static string CleanSettings(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        if (root["Dock"] is JsonObject dock && dock["Pinned"] is JsonArray pinned) dock["Pinned"] = $"{pinned.Count} applis";
        if (root["Profiles"] is JsonObject profiles && profiles["Items"] is JsonArray items)
        {
            int index = 0;
            foreach (var item in items.OfType<JsonObject>())
            {
                index++;
                var id = item["Id"]?.GetValue<string>();
                if (id is not ("perso" or "gaming" or "stage" or "etudes")) item["Name"] = $"Profil {index}";
                if (item["Pinned"] is JsonArray apps) item["Pinned"] = $"{apps.Count} applis";
                if (item["Wallpaper"] != null) item["Wallpaper"] = "(un fond)";
            }
        }
        if (root["Widgets"] is JsonObject widgets)
            foreach (var name in new[] { "City", "CityLatitude", "CityLongitude" }) widgets.Remove(name);
        if (root["Discord"] is JsonObject discord) discord.Remove("ClientId");
        if (root["Remote"] is JsonObject remote && remote["Devices"] is JsonArray phones) remote["Devices"] = $"{phones.Count} appareils";
        return Clean(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    // ------------------------------------------------------------------ configuration

    private static string SystemReport()
    {
        var text = new StringBuilder();
        var version = typeof(BugReport).Assembly.GetName().Version;
        text.AppendLine($"Rapport de bug {AppInfo.Name} — {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine();
        text.AppendLine($"Version : {version?.ToString(3)} · .NET {Environment.Version} · {(Environment.Is64BitProcess ? "64 bits" : "32 bits")}");
        text.AppendLine($"Mode sans échec : {(App.SafeMode ? "oui" : "non")} · langue : {Loc.Language} (Windows : {System.Globalization.CultureInfo.CurrentUICulture.Name})");
        text.AppendLine($"Lancée depuis : {(Environment.ProcessPath?.Contains(@"\Programs\", StringComparison.OrdinalIgnoreCase) == true ? "installation" : "autre dossier (développement ?)")}");
        text.AppendLine($"En marche depuis : {DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime:hh\\:mm\\:ss} · mémoire : {Environment.WorkingSet / (1024 * 1024)} Mo");
        text.AppendLine();
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            var build = key?.GetValue("CurrentBuild") as string ?? "?";
            var name = int.TryParse(build, out int number) && number >= 22000 ? "Windows 11" : "Windows 10";
            text.AppendLine($"Système : {name} {key?.GetValue("EditionID")} {key?.GetValue("DisplayVersion")} (build {build}.{key?.GetValue("UBR")})");
        }
        using (var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            text.AppendLine($"Processeur : {(cpu?.GetValue("ProcessorNameString") as string)?.Trim()} ({Environment.ProcessorCount} cœurs logiques)");
        text.AppendLine($"Mémoire vive : {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024.0):0.#} Go");
        text.AppendLine("Opaline — " + MemoryWatch.Describe()); // 7A : pour voir une fuite
        foreach (var gpu in GraphicsCards()) text.AppendLine($"Carte graphique : {gpu}");
        foreach (var screen in ScreenInfo.All())
            text.AppendLine($"Écran : {screen.Label}, échelle {screen.Scale:P0}, position ({screen.Bounds.Left}, {screen.Bounds.Top})");
        text.AppendLine();

        var settings = SettingsStore.Current;
        text.AppendLine("Modules :");
        text.AppendLine($"  île {On(settings.Island.Enabled)} · dock {On(settings.Dock.Enabled)} ({settings.Dock.Layout}, {settings.Dock.Mode}, barre des tâches masquée {On(settings.Dock.HideTaskbar)})");
        text.AppendLine($"  barre de menus {On(settings.MenuBar.Enabled)} · widgets du bureau {On(settings.DesktopWidgets.Enabled)} · palette {On(settings.Palette.Enabled)}");
        text.AppendLine($"  fond dynamique {On(settings.Wallpaper.Dynamic)} · fond animé {On(settings.Wallpaper.Animated)} · verre liquide {On(settings.Appearance.LiquidGlass)}");
        text.AppendLine($"  accent du fond {On(settings.Appearance.AccentFromWallpaper)} · accent Windows {On(settings.Appearance.SyncWindowsAccent)} · thème auto {On(settings.Appearance.AutoTheme)} · curseur {On(settings.Appearance.ThemedCursor)}");
        text.AppendLine($"  télécommande {On(settings.Remote.Enabled)} ({settings.Remote.Devices.Count} téléphone(s)) · éclairage RGB {On(settings.Rgb.Enabled)}");
        text.AppendLine($"  Discord {On(settings.Discord.Enabled)} · mini Claude {On(settings.Claude.Enabled)} ({settings.Claude.Model}) · mode Gaming auto {On(settings.Gaming.Auto)}");
        text.AppendLine($"Modules arrêtés après une erreur pendant cette session : {(ModuleGuard.Disabled.Count == 0 ? "aucun" : string.Join(", ", ModuleGuard.Disabled))}");
        return Clean(text.ToString());
    }

    private static string On(bool value) => value ? "oui" : "non";

    /// <summary>Noms des cartes graphiques (pilotes d'affichage installés), sans WMI.</summary>
    private static IEnumerable<string> GraphicsCards()
    {
        const string Class = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using var root = Registry.LocalMachine.OpenSubKey(Class);
        if (root == null) yield break;
        var seen = new HashSet<string>();
        foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            string? description;
            try
            {
                using var key = root.OpenSubKey(name);
                description = key?.GetValue("DriverDesc") as string;
            }
            catch (System.Security.SecurityException)
            {
                continue;
            }
            if (description != null && seen.Add(description)) yield return description;
        }
    }
}
