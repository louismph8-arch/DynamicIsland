namespace DynamicIsland.Services;

/// <summary>
/// Modules surveillés (6A) : quand un module plante (exception non gérée, échec au démarrage ou en appliquant un
/// réglage), il est ARRÊTÉ pour la session — ses réglages ne sont pas touchés : il revient au prochain lancement —, l'île
/// l'annonce discrètement, et le reste de l'app continue. Le dock arrêté rend toujours la barre des tâches.
/// Le module fautif est retrouvé dans la pile d'appels (premier cadre de l'app qui appartient à un module).
/// </summary>
public static class ModuleGuard
{
    public const string Dock = "dock";
    public const string Status = "status";
    public const string Notices = "notices";
    public const string MenuBar = "menubar";
    public const string Widgets = "widgets";
    public const string Wallpapers = "wallpapers";
    public const string Colors = "colors";
    public const string Discord = "discord";
    public const string Claude = "claude";
    public const string Palette = "palette";
    public const string Updates = "updates";
    public const string Gaming = "gaming";
    public const string Remote = "remote";
    public const string Rgb = "rgb";
    public const string Friends = "friends";
    public const string Health = "health";

    /// <summary>Préfixes des types de chaque module (espaces de noms ou classes).</summary>
    private static readonly (string Id, string[] Owners)[] Ownership =
    [
        // Zone d'état avant le dock : une erreur dans la corbeille ou Windows Update ne coupe pas tout le dock.
        (Status, ["DynamicIsland.Dock.StatusArea", "DynamicIsland.Dock.DownloadsWindow", "DynamicIsland.Services.KeyboardLayoutService",
            "DynamicIsland.Services.WindowsUpdateService", "DynamicIsland.Services.RecycleBinService", "DynamicIsland.Services.DownloadsService"]),
        (Dock, ["DynamicIsland.Dock.", "DynamicIsland.Services.TrayIconHost"]),
        (Notices, ["DynamicIsland.Services.DeviceNotifier", "DynamicIsland.Services.DownloadNotifier"]),
        (MenuBar, ["DynamicIsland.MenuBar."]),
        (Widgets, ["DynamicIsland.DesktopWidgets."]),
        (Wallpapers, ["DynamicIsland.Services.AnimatedWallpaper", "DynamicIsland.Services.DynamicWallpaperService", "DynamicIsland.Controls.AnimatedAuroraEffect"]),
        (Colors, ["DynamicIsland.Services.WallpaperPalette", "DynamicIsland.Services.WindowsAccentService", "DynamicIsland.Services.AutoThemeService", "DynamicIsland.Services.CursorTheme"]),
        (Discord, ["DynamicIsland.Services.DiscordVoice", "DynamicIsland.Views.VoiceView"]),
        (Claude, ["DynamicIsland.Services.ClaudeService", "DynamicIsland.Views.ClaudeView"]),
        (Palette, ["DynamicIsland.Palette.", "DynamicIsland.Services.GlobalHotkey"]),
        (Updates, ["DynamicIsland.Services.UpdateService"]),
        (Gaming, ["DynamicIsland.Services.GamingMode"]),
        (Remote, ["DynamicIsland.Services.Remote."]),
        (Rgb, ["DynamicIsland.Services.Rgb."]),
        (Friends, ["DynamicIsland.Services.Friends.", "DynamicIsland.Views.FriendsView"]),
        (Health, ["DynamicIsland.Services.Health.", "DynamicIsland.Views.HealthView"]),
    ];

    private static readonly Dictionary<string, Action> Stoppers = [];
    private static readonly List<string> DisabledList = [];

    /// <summary>Modules arrêtés pendant cette session (dans l'ordre).</summary>
    public static IReadOnlyList<string> Disabled => DisabledList;

    /// <summary>Un module vient d'être arrêté (thread de l'interface) : l'île l'annonce.</summary>
    public static event Action<string>? ModuleDisabled;

    /// <summary>Ce qu'il faut faire pour arrêter ce module (fenêtres fermées, minuteries coupées, Windows remis…).</summary>
    public static void Register(string id, Action stop) => Stoppers[id] = stop;

    public static bool IsDisabled(string id) => DisabledList.Contains(id);

    /// <summary>Nom affiché du module (« Widgets du bureau »…).</summary>
    public static string Name(string id) => Loc.T("module." + id);

    /// <summary>Exception d'un module connu : journal, puis arrêt du module (une seule fois) et annonce.</summary>
    public static void Fail(string id, Exception ex)
    {
        Log.Error($"Module « {id} » : erreur, module arrêté pour la session", ex);
        if (DisabledList.Contains(id)) return;
        DisabledList.Add(id);
        if (Stoppers.TryGetValue(id, out var stop))
        {
            try
            {
                stop();
            }
            catch (Exception inner)
            {
                Log.Error($"Module « {id} » : arrêt", inner);
            }
        }
        try
        {
            ModuleDisabled?.Invoke(id);
        }
        catch (Exception inner)
        {
            Log.Error("Annonce d'un module arrêté", inner);
        }
    }

    /// <summary>Exception sans module connu d'avance : on cherche son module ; vrai si un module a été arrêté.</summary>
    public static bool TryFail(Exception ex)
    {
        if (OwnerOf(ex.ToString()) is not { } id) return false;
        Fail(id, ex);
        return true;
    }

    /// <summary>
    /// Module d'après le texte d'une exception (pile d'appels comprise) : le premier cadre « at DynamicIsland.… » qui
    /// appartient à un module. Calcul pur, vérifié par --self-test.
    /// </summary>
    internal static string? OwnerOf(string exceptionText)
    {
        foreach (var line in exceptionText.Split('\n'))
        {
            int at = line.IndexOf("at DynamicIsland.", StringComparison.Ordinal);
            if (at < 0) continue;
            var frame = line[(at + 3)..];
            foreach (var (id, owners) in Ownership)
                if (owners.Any(owner => frame.StartsWith(owner, StringComparison.Ordinal))) return id;
        }
        return null;
    }

    /// <summary>Module d'un type (gestionnaire de réglages qui a planté…), ou null.</summary>
    internal static string? OwnerOf(Type? type) =>
        type?.FullName is { } name ? Ownership.FirstOrDefault(o => o.Owners.Any(owner => name.StartsWith(owner, StringComparison.Ordinal))).Id : null;
}
