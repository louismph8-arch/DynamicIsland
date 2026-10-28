using DynamicIsland.Dock;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Profils (Perso, Gaming, Stage, Études + profils créés par l'utilisateur) : chacun a ses apps de dock,
/// sa couleur d'accent et son fond d'écran. Changer de profil = ranger les valeurs actuelles dans le
/// profil qu'on quitte, puis charger celles du nouveau (le dock et le thème suivent via SettingsStore.Changed).
/// </summary>
public static class ProfileService
{
    public const string PersoId = "perso";

    private static ProfileSettings Settings => SettingsStore.Current.Profiles;

    public static IReadOnlyList<Profile> All => Settings.Items;
    public static Profile? Active => Find(Settings.Active);
    public static Profile? Find(string id) => Settings.Items.FirstOrDefault(p => p.Id == id);

    /// <summary>Couleurs d'un profil (pour le profil actif : celles en cours, peut-être modifiées depuis).</summary>
    public static (System.Windows.Media.Color Start, System.Windows.Media.Color End) AccentOf(Profile profile)
    {
        // Profil actif : la couleur effective (celle du fond d'écran si l'option est cochée).
        if (profile.Id == Settings.Active) return (AppearanceService.AccentStart, AppearanceService.AccentEnd);
        AppearanceColors.TryParse(profile.AccentStart, out var start);
        if (!AppearanceColors.TryParse(profile.AccentEnd, out var end)) end = start;
        return (start, end);
    }

    /// <summary>
    /// Au démarrage. Premier lancement de la 3E : Perso reprend le dock et la couleur actuels (rien n'est
    /// perdu), et Gaming / Stage / Études sont créés avec les applis trouvées sur le PC.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            var current = SettingsStore.Current;
            if (current.Profiles.Items.Count == 0)
            {
                current.Profiles.Items.AddRange(
                [
                    new Profile
                    {
                        Id = PersoId, Name = Loc.T("profile.default.perso"), Icon = "user",
                        AccentStart = current.Appearance.AccentStart, AccentEnd = current.Appearance.AccentEnd,
                        Pinned = PinnedApp.CloneAll(current.Dock.Pinned),
                    },
                    new Profile
                    {
                        Id = "gaming", Name = "Gaming", Icon = "gamepad-2", AccentStart = Swatch.Hex(Swatch.Red), AccentEnd = Swatch.Hex(Swatch.Magenta),
                        Pinned = GamingPins(),
                    },
                    new Profile
                    {
                        Id = "stage", Name = Loc.T("profile.default.stage"), Icon = "briefcase", AccentStart = Swatch.Hex(Swatch.Blue), AccentEnd = Swatch.Hex(Swatch.Teal),
                        Pinned = [DockSettings.Explorer(), .. DockSettings.Find(KnownApps.Chrome, KnownApps.Outlook, KnownApps.Word, KnownApps.Excel)],
                    },
                    new Profile
                    {
                        Id = "etudes", Name = Loc.T("profile.default.etudes"), Icon = "graduation-cap", AccentStart = Swatch.Hex(Swatch.Green), AccentEnd = Swatch.Hex(Swatch.Teal),
                        Pinned = [DockSettings.Explorer(), .. DockSettings.Find(KnownApps.Chrome, KnownApps.Word, KnownApps.OneNote, KnownApps.Excel)],
                    },
                ]);
                current.Profiles.Active = PersoId;
                current.Normalize();
                SettingsStore.ScheduleSave();
            }
            else if (Find(current.Profiles.Active) == null)
            {
                current.Profiles.Active = current.Profiles.Items[0].Id;
                SettingsStore.ScheduleSave();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Profils : initialisation", ex);
        }
    }

    /// <summary>Gaming : Steam et Discord, puis les autres lanceurs rangés dans un dossier (s'il y en a plusieurs).</summary>
    private static List<PinnedApp> GamingPins()
    {
        var pins = new List<PinnedApp> { DockSettings.Explorer() };
        pins.AddRange(DockSettings.Find(KnownApps.Steam, KnownApps.Discord));
        var launchers = DockSettings.Find(KnownApps.Epic, KnownApps.BattleNet, KnownApps.Ubisoft, KnownApps.Riot, KnownApps.Ankama);
        if (launchers.Count > 1) pins.Add(PinnedApp.Folder(Loc.T("profile.default.launchers"), launchers));
        else pins.AddRange(launchers);
        return pins;
    }

    /// <summary>Range les apps et la couleur actuelles dans le profil actif (avant de le quitter ou de l'afficher).</summary>
    public static void CaptureActive()
    {
        if (Active is not { } profile) return;
        var current = SettingsStore.Current;
        profile.Pinned = PinnedApp.CloneAll(current.Dock.Pinned);
        profile.AccentStart = current.Appearance.AccentStart;
        profile.AccentEnd = current.Appearance.AccentEnd;
    }

    /// <summary>Active un profil : apps du dock, couleur d'accent et fond d'écran.</summary>
    public static void Switch(string id)
    {
        if (id == Settings.Active || Find(id) is not { } target) return;
        try
        {
            CaptureActive();
            SettingsStore.Update(s =>
            {
                s.Dock.Pinned = PinnedApp.CloneAll(target.Pinned);
                s.Appearance.AccentStart = target.AccentStart;
                s.Appearance.AccentEnd = target.AccentEnd;
                s.Profiles.Active = target.Id;
            });
            WallpaperService.ApplyFor(target);
        }
        catch (Exception ex)
        {
            Log.Error($"Profils : passage à « {target.Name} »", ex);
        }
    }

    /// <summary>Nouveau profil, copie du profil actif (mêmes apps, même couleur) : il n'y a plus qu'à l'ajuster.</summary>
    public static Profile Create()
    {
        CaptureActive();
        var source = Active;
        var profile = new Profile
        {
            Name = Loc.T("profiles.new"),
            Icon = "star",
            AccentStart = source?.AccentStart ?? Swatch.Hex(Swatch.AccentStart),
            AccentEnd = source?.AccentEnd ?? Swatch.Hex(Swatch.AccentEnd),
            Pinned = source != null ? PinnedApp.CloneAll(source.Pinned) : DockSettings.CreateDefaultPins(),
        };
        profile.Normalize();
        SettingsStore.Update(s => s.Profiles.Items.Add(profile));
        return profile;
    }

    /// <summary>Supprime un profil (jamais le dernier). Si c'était le profil actif, on passe au premier restant.</summary>
    public static void Delete(string id)
    {
        if (Settings.Items.Count <= 1 || Find(id) is not { } profile) return;
        if (id == Settings.Active) Switch(Settings.Items.First(p => p.Id != id).Id);
        SettingsStore.Update(s => s.Profiles.Items.Remove(profile));
    }

    public static void Rename(Profile profile, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == profile.Name) return;
        SettingsStore.Update(_ => profile.Name = name);
    }

    /// <param name="name">Nom Lucide (voir <see cref="Icons.ProfileChoices"/>).</param>
    public static void SetIcon(Profile profile, string name) => SettingsStore.Update(_ => profile.Icon = Icons.Normalize(name));

    /// <summary>Couleur d'un profil ; pour le profil actif, elle s'applique tout de suite.</summary>
    public static void SetAccent(Profile profile, string start, string end) => SettingsStore.Update(s =>
    {
        profile.AccentStart = start;
        profile.AccentEnd = end;
        if (profile.Id != s.Profiles.Active) return;
        s.Appearance.AccentStart = start;
        s.Appearance.AccentEnd = end;
    });

    /// <summary>Fond d'écran d'un profil (null = fond d'origine) ; pour le profil actif, il s'applique tout de suite.</summary>
    public static void SetWallpaper(Profile profile, string? path)
    {
        SettingsStore.Update(_ => profile.Wallpaper = path);
        if (profile.Id == Settings.Active) WallpaperService.ApplyFor(profile);
    }
}
