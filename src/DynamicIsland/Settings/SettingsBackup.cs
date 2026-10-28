using System.Globalization;
using System.IO;

namespace DynamicIsland.Settings;

/// <summary>
/// Sauvegardes automatiques des réglages (6A) : une copie de settings.json au démarrage, puis au plus une par heure après
/// une sauvegarde — seulement si quelque chose a changé depuis la dernière copie. Les 10 plus récentes sont gardées dans
/// %APPDATA%\DynamicIsland\Sauvegardes. « Revenir à une version précédente » remet l'une d'elles (l'état actuel est
/// copié d'abord, on peut donc revenir en arrière) puis relance l'app.
/// </summary>
public static class SettingsBackup
{
    public const int Kept = 10;
    private const string Prefix = "reglages-";
    private const string DateFormat = "yyyy-MM-dd_HH-mm-ss";
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(1);
    private static DateTime _lastCopy = DateTime.MinValue;

    public sealed record Entry(string Path, DateTime When, long Size);

    /// <summary>Copies existantes, de la plus récente à la plus ancienne.</summary>
    public static List<Entry> List(string? folder = null)
    {
        folder ??= AppPaths.SettingsBackups;
        if (!Directory.Exists(folder)) return [];
        var entries = new List<Entry>();
        foreach (var file in Directory.GetFiles(folder, Prefix + "*.json"))
        {
            var stamp = System.IO.Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(stamp, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
                entries.Add(new Entry(file, when, new FileInfo(file).Length));
        }
        return entries.OrderByDescending(e => e.When).ToList();
    }

    /// <summary>Au démarrage : copie des réglages s'ils ont changé depuis la dernière copie.</summary>
    public static void AtStartup() => Copy(AppPaths.Settings, AppPaths.SettingsBackups);

    /// <summary>Après une sauvegarde des réglages : au plus une copie par heure.</summary>
    public static void AfterSave()
    {
        if (DateTime.Now - _lastCopy < MinInterval) return;
        Copy(AppPaths.Settings, AppPaths.SettingsBackups);
    }

    /// <summary>
    /// Copie <paramref name="source"/> dans <paramref name="folder"/> si son contenu diffère de la copie la plus récente,
    /// puis ne garde que les <see cref="Kept"/> plus récentes. Renvoie la copie créée (null : rien à copier).
    /// </summary>
    internal static string? Copy(string source, string folder, DateTime? now = null)
    {
        try
        {
            if (!File.Exists(source)) return null;
            var content = File.ReadAllBytes(source);
            var newest = List(folder).FirstOrDefault();
            _lastCopy = DateTime.Now;
            if (newest != null && File.ReadAllBytes(newest.Path).AsSpan().SequenceEqual(content)) return null;
            Directory.CreateDirectory(folder);
            var target = System.IO.Path.Combine(folder, Prefix + (now ?? DateTime.Now).ToString(DateFormat, CultureInfo.InvariantCulture) + ".json");
            File.WriteAllBytes(target, content);
            foreach (var old in List(folder).Skip(Kept))
                File.Delete(old.Path);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Réglages : sauvegarde automatique", ex);
            return null;
        }
    }

    /// <summary>
    /// Remet cette copie : la copie choisie est mise de côté, l'état actuel est copié (on pourra y revenir), puis la copie
    /// remplace settings.json et plus rien n'est écrit jusqu'au redémarrage (que l'appelant lance).
    /// </summary>
    public static void Restore(Entry entry)
    {
        var chosen = File.ReadAllBytes(entry.Path); // avant la copie de l'état actuel, qui peut effacer la plus ancienne
        SettingsStore.Flush();
        Copy(AppPaths.Settings, AppPaths.SettingsBackups);
        SettingsStore.SuppressSaving();
        var temp = AppPaths.Settings + ".tmp";
        File.WriteAllBytes(temp, chosen);
        File.Move(temp, AppPaths.Settings, overwrite: true);
        Log.Info($"Réglages remis à la version du {entry.When:yyyy-MM-dd HH:mm:ss}");
    }

    /// <summary>« 4 profils, 23 applis épinglées » : de quoi reconnaître une copie (illisible = null).</summary>
    public static string? Describe(Entry entry)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(entry.Path));
            var root = json.RootElement;
            int profiles = root.TryGetProperty("Profiles", out var p) && p.TryGetProperty("Items", out var items) ? items.GetArrayLength() : 0;
            int pins = root.TryGetProperty("Dock", out var d) && d.TryGetProperty("Pinned", out var pinned) ? pinned.GetArrayLength() : 0;
            return Loc.F("backup.summary", profiles, pins);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
