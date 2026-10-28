using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// État d'origine des réglages de Windows que l'app modifie sur demande (couleur d'accent, thème clair/sombre).
/// Il est copié AVANT le premier changement dans <c>windows-d-origine.json</c>, par section ; décocher l'option
/// (ou désinstaller l'app : <c>--restore-windows</c>) remet exactement les valeurs d'origine, y compris
/// l'absence d'une valeur. Le fichier survit à un arrêt forcé.
/// </summary>
public static class WindowsBackup
{
    /// <summary>Racine du registre (HKCU ; une clé de test pour <c>--self-test</c>).</summary>
    internal static RegistryKey Root { get; set; } = Registry.CurrentUser;
    internal static string FilePath { get; set; } = Path.Combine(AppPaths.DataFolder, "windows-d-origine.json");

    /// <summary>Une valeur du registre telle qu'elle était (Kind null = absente).</summary>
    public sealed record Value(string Key, string Name, RegistryValueKind? Kind, string? Data);

    public static bool Has(string section) => Load().ContainsKey(section);

    /// <summary>Copie l'état actuel des valeurs, sauf si la section est déjà sauvée (on garde le VRAI état d'origine).</summary>
    public static void SaveIfMissing(string section, IEnumerable<(string Key, string Name)> values)
    {
        var all = Load();
        if (all.ContainsKey(section)) return;
        all[section] = values.Select(v => Read(v.Key, v.Name)).ToList();
        Save(all);
    }

    /// <summary>Remet les valeurs d'une section puis l'oublie. Vrai si quelque chose a été remis.</summary>
    public static bool Restore(string section)
    {
        var all = Load();
        if (!all.TryGetValue(section, out var values)) return false;
        foreach (var value in values) Write(value);
        all.Remove(section);
        Save(all);
        return true;
    }

    /// <summary>Valeurs sauvées d'une section (null si rien n'a été changé).</summary>
    public static IReadOnlyList<Value>? Get(string section) => Load().GetValueOrDefault(section);

    /// <summary>Oublie une section sans rien remettre (l'utilisateur a changé le réglage lui-même depuis).</summary>
    public static void Forget(string section)
    {
        var all = Load();
        if (all.Remove(section)) Save(all);
    }

    /// <summary>Désinstallation : remet tout ce qui a été sauvé (fond d'écran compris) et prévient Windows.</summary>
    public static void RestoreAll()
    {
        try
        {
            WallpaperService.RestoreOriginal(); // traite (et retire) sa propre section
        }
        catch (Exception ex)
        {
            Log.Error("Fond d'écran d'origine", ex);
        }
        bool restored = false, cursors = false;
        foreach (var section in Load().Keys.ToList())
        {
            if (!Restore(section)) continue;
            restored = true;
            cursors |= section == CursorTheme.Section;
        }
        if (restored) BroadcastColors(wait: true); // l'app s'arrête juste après
        if (cursors) CursorTheme.Reload();
    }

    /// <summary>Prévient Explorer et les applis d'un changement de couleurs / de thème (en arrière-plan par défaut).</summary>
    public static void BroadcastColors(bool wait = false)
    {
        var task = Task.Run(() => SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out _));
        if (wait) task.Wait(TimeSpan.FromSeconds(5));
    }

    private static Value Read(string keyPath, string name)
    {
        using var key = Root.OpenSubKey(keyPath);
        var data = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key == null || data == null) return new Value(keyPath, name, null, null);
        var kind = key.GetValueKind(name);
        string text = data switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => data.ToString() ?? "",
        };
        return new Value(keyPath, name, kind, text);
    }

    private static void Write(Value value)
    {
        using var key = Root.CreateSubKey(value.Key);
        if (value.Kind is not { } kind || value.Data is not { } data)
        {
            key.DeleteValue(value.Name, throwOnMissingValue: false);
            return;
        }
        object raw = kind switch
        {
            RegistryValueKind.Binary => Convert.FromBase64String(data),
            RegistryValueKind.DWord => int.Parse(data, System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(data, System.Globalization.CultureInfo.InvariantCulture),
            _ => data,
        };
        key.SetValue(value.Name, raw, kind);
    }

    private static Dictionary<string, List<Value>> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, List<Value>>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("Sauvegarde des réglages de Windows", ex);
            return [];
        }
    }

    private static void Save(Dictionary<string, List<Value>> all)
    {
        if (all.Count == 0)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }
}
