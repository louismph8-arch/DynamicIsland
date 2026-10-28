using DynamicIsland.Dock;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Section « Profils ». Les apps et la couleur d'accent du profil actif sont les réglages « vivants »
/// (<see cref="DockSettings.Pinned"/>, <see cref="AppearanceSettings.AccentStart"/>…) : la copie rangée
/// dans le profil n'est mise à jour qu'au changement de profil (voir Services/ProfileService).
/// </summary>
public sealed class ProfileSettings
{
    public string Active { get; set; } = "";
    public List<Profile> Items { get; set; } = [];
    /// <summary>Vrai quand le fond d'écran actuel a été posé par l'app (le fond d'origine est alors sauvegardé).</summary>
    public bool WallpaperChanged { get; set; }

    public void Normalize()
    {
        Items ??= [];
        Items.RemoveAll(p => p == null);
        foreach (var profile in Items) profile.Normalize();
    }
}

public sealed class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Profil";
    /// <summary>Nom de l'icône Lucide (voir <see cref="Icons.ProfileChoices"/>).</summary>
    public string Icon { get; set; } = "star";
    public string AccentStart { get; set; } = Swatch.Hex(Swatch.AccentStart);
    public string AccentEnd { get; set; } = Swatch.Hex(Swatch.AccentEnd);
    /// <summary>Image de fond d'écran ; null = fond d'origine de Windows.</summary>
    public string? Wallpaper { get; set; }
    public List<PinnedApp> Pinned { get; set; } = [];

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(Name)) Name = "Profil";
        Icon = Icons.Normalize(Icon); // les glyphes Segoe d'avant la 5A deviennent des noms Lucide
        if (!AppearanceColors.TryParse(AccentStart, out _)) AccentStart = Swatch.Hex(Swatch.AccentStart);
        if (!AppearanceColors.TryParse(AccentEnd, out _)) AccentEnd = AccentStart;
        if (string.IsNullOrWhiteSpace(Wallpaper)) Wallpaper = null;
        Pinned = PinnedApp.NormalizeList(Pinned);
    }
}

/// <summary>Mode Gaming automatique.</summary>
public sealed class GamingSettings
{
    /// <summary>Jeu en plein écran (exclusif ou fenêtré sans bordure) → île réduite au minimum, dock caché.</summary>
    public bool Auto { get; set; } = true;
    /// <summary>Processus (sans « .exe ») qui ne déclenchent jamais le mode Gaming : navigateurs, lecteurs vidéo…</summary>
    public List<string> Exceptions { get; set; } = [.. DefaultExceptions];

    public static readonly string[] DefaultExceptions =
    [
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
        "vlc", "mpc-hc64", "mpc-be64", "potplayermini64", "wmplayer", "applicationframehost",
        "powerpnt", "explorer", "snippingtool", "screenclippinghost", "lightshot", "sharex",
    ];

    public void Normalize() =>
        Exceptions = (Exceptions ?? []).Select(Clean).Where(name => name.Length > 0).Distinct().ToList();

    /// <summary>« Chrome.exe » → « chrome ».</summary>
    public static string Clean(string? name)
    {
        var clean = (name ?? "").Trim().ToLowerInvariant();
        return clean.EndsWith(".exe") ? clean[..^4] : clean;
    }
}
