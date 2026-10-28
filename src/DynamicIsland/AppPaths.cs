using System.IO;

namespace DynamicIsland;

/// <summary>Dossiers et fichiers de l'app.</summary>
internal static class AppPaths
{
    /// <summary>%APPDATA%\DynamicIsland (réglages, état de récupération).</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    /// <summary>Tous les réglages (voir Settings/SettingsStore).</summary>
    public static string Settings => Path.Combine(DataFolder, "settings.json");

    /// <summary>Étagère à fichiers : liste de chemins (références, pas de copies).</summary>
    public static string Shelf => Path.Combine(DataFolder, "shelf.json");

    /// <summary>Raccourcis web (.url + icône) épinglés au dock.</summary>
    public static string Shortcuts => Path.Combine(DataFolder, "Raccourcis");

    /// <summary>Ancien fichier de l'étape 2, migré dans settings.json puis renommé en .bak.</summary>
    public static string LegacyDockSettings => Path.Combine(DataFolder, "dock.json");

    /// <summary>Ancien journal (jusqu'à 1.10, dans %LOCALAPPDATA%) : repris dans le rapport de bug s'il existe encore.</summary>
    public static string LegacyLogFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "log.txt");

    /// <summary>Sauvegardes automatiques des réglages (les 10 dernières, voir Settings/SettingsBackup).</summary>
    public static string SettingsBackups => Path.Combine(DataFolder, "Sauvegardes");

    /// <summary>Présent seulement quand la barre des tâches est masquée par nous : contient son état d'origine.</summary>
    public static string TaskbarState => Path.Combine(DataFolder, "taskbar-state.txt");

    /// <summary>Présent seulement quand l'animation de réduction de Windows est coupée par nous (effet Génie).</summary>
    public static string MinimizeAnimationState => Path.Combine(DataFolder, "animation-reduction.txt");

    /// <summary>Présent seulement quand le dock réserve de l'espace à l'écran : contient le handle de sa fenêtre.</summary>
    public static string AppBarHandle => Path.Combine(DataFolder, "dock-appbar.txt");
    public static string MenuBarHandle => Path.Combine(DataFolder, "barre-de-menus-appbar.txt");

    public static void EnsureDataFolder() => Directory.CreateDirectory(DataFolder);
}
