using System.Diagnostics;
using System.IO;
using DynamicIsland.Interop;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Webcam / micro en cours d'utilisation, d'après le registre que Windows tient à jour pour ses propres
/// indicateurs (CapabilityAccessManager\ConsentStore) : une appli utilise l'appareil tant que sa valeur
/// « LastUsedTimeStop » vaut 0.
///
/// Piège : une appli tuée ou mise à jour laisse une entrée « en cours » pour toujours (vu chez
/// l'utilisateur : d'anciennes versions de Discord). Pour les applis classiques, on vérifie donc en plus
/// que l'exe tourne vraiment. Lecture toutes les 2 s : quasi gratuit.
/// </summary>
public static class PrivacyService
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static bool IsCameraInUse() => IsInUse("webcam");

    public static bool IsMicrophoneInUse() => IsInUse("microphone");

    private static bool IsInUse(string capability)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStore + capability);
            if (root == null) return false;

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key == null) continue;
                if (name == "NonPackaged")
                {
                    // Applis classiques : une sous-clé par exe, chemin avec « # » à la place de « \ ».
                    foreach (var exeKey in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(exeKey);
                        if (app != null && IsActive(app) && IsRunning(exeKey.Replace('#', '\\'))) return true;
                    }
                }
                else if (IsActive(key))
                {
                    return true; // appli du Store (nettoyée correctement par Windows)
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Confidentialité : lecture de « {capability} »", ex);
            return false;
        }
    }

    private static bool IsActive(RegistryKey app) =>
        app.GetValue("LastUsedTimeStop") is long stop && stop == 0 &&
        app.GetValue("LastUsedTimeStart") is long start && start != 0;

    /// <summary>Un processus tourne-t-il depuis cet exe précis (et pas une ancienne version) ?</summary>
    private static bool IsRunning(string exePath)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            using (process)
            {
                var path = DockNative.GetProcessPath((uint)process.Id);
                if (path != null && string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
