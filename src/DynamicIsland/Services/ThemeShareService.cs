using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DynamicIsland.Dock;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>Contenu d'un fichier de thème .opaline (ex-.bloomtheme, même format JSON).</summary>
public sealed class ThemeFile
{
    public int Format { get; set; } = 1;
    public string Name { get; set; } = "";
    public string? AppVersion { get; set; }
    public DateTime Created { get; set; }
    public ThemeLook Look { get; set; } = new();
    /// <summary>Nom du profil à activer après l'import.</summary>
    public string? ActiveProfile { get; set; }
    public List<ThemeProfile> Profiles { get; set; } = [];
    /// <summary>Images embarquées (fonds d'écran, icônes des raccourcis web) : nom → contenu en base64.</summary>
    public Dictionary<string, string> Files { get; set; } = new();
}

/// <summary>Style partagé : transparence, arrondis, taille et disposition du dock…</summary>
public sealed class ThemeLook
{
    public double GlowIntensity { get; set; } = 0.4;
    public double Opacity { get; set; } = 0.94;
    public double DockOpacity { get; set; } = 0.8;
    public bool DockBlur { get; set; } = true;
    public double Roundness { get; set; } = 1;
    public double DockIconSize { get; set; } = 40;
    public double AnimationSpeed { get; set; } = 1;
    public DockLayout DockLayout { get; set; } = DockLayout.Bar;
    public double Magnification { get; set; } = 1.4;
    public bool MinimizeEffect { get; set; } = true;
    /// <summary>Verre liquide (1.2.0+) ; absent des thèmes plus anciens : on garde alors le réglage actuel.</summary>
    public bool? LiquidGlass { get; set; }
    public double? GlassIntensity { get; set; }
    /// <summary>Couleurs du fond d'écran (1.3.0+). La synchro de Windows et le thème auto ne sont jamais importés.</summary>
    public bool? AccentFromWallpaper { get; set; }
    public bool? TintFromWallpaper { get; set; }

    public static ThemeLook From(AppSettings settings) => new()
    {
        GlowIntensity = settings.Appearance.GlowIntensity,
        Opacity = settings.Appearance.Opacity,
        DockOpacity = settings.Appearance.DockOpacity,
        DockBlur = settings.Appearance.DockBlur,
        Roundness = settings.Appearance.Roundness,
        DockIconSize = settings.Appearance.DockIconSize,
        AnimationSpeed = settings.Appearance.AnimationSpeed,
        DockLayout = settings.Dock.Layout,
        Magnification = settings.Dock.Magnification,
        MinimizeEffect = settings.Dock.MinimizeEffect,
        LiquidGlass = settings.Appearance.LiquidGlass,
        GlassIntensity = settings.Appearance.GlassIntensity,
        AccentFromWallpaper = settings.Appearance.AccentFromWallpaper,
        TintFromWallpaper = settings.Appearance.TintFromWallpaper,
    };

    /// <summary>Les valeurs hors bornes sont recadrées ensuite par <see cref="AppSettings.Normalize"/>.</summary>
    public void ApplyTo(AppSettings settings)
    {
        settings.Appearance.GlowIntensity = GlowIntensity;
        settings.Appearance.Opacity = Opacity;
        settings.Appearance.DockOpacity = DockOpacity;
        settings.Appearance.DockBlur = DockBlur;
        settings.Appearance.Roundness = Roundness;
        settings.Appearance.DockIconSize = DockIconSize;
        settings.Appearance.AnimationSpeed = AnimationSpeed;
        settings.Dock.Layout = Enum.IsDefined(DockLayout) ? DockLayout : settings.Dock.Layout;
        settings.Dock.Magnification = Magnification;
        settings.Dock.MinimizeEffect = MinimizeEffect;
        if (LiquidGlass is bool glass) settings.Appearance.LiquidGlass = glass;
        if (GlassIntensity is double intensity) settings.Appearance.GlassIntensity = intensity;
        if (AccentFromWallpaper is bool fromWallpaper) settings.Appearance.AccentFromWallpaper = fromWallpaper;
        if (TintFromWallpaper is bool tint) settings.Appearance.TintFromWallpaper = tint;
    }
}

public sealed class ThemeProfile
{
    public string Name { get; set; } = "";
    /// <summary>Nom Lucide (les thèmes d'avant la 5A contiennent un glyphe Segoe, converti à l'import).</summary>
    public string Icon { get; set; } = "star";
    public string AccentStart { get; set; } = Swatch.Hex(Swatch.AccentStart);
    public string AccentEnd { get; set; } = Swatch.Hex(Swatch.AccentEnd);
    /// <summary>Clé dans <see cref="ThemeFile.Files"/>.</summary>
    public string? Wallpaper { get; set; }
    public List<ThemeApp> Apps { get; set; } = [];
}

/// <summary>Appli du dock telle qu'on la partage : retrouvée sur l'autre PC par son chemin, son ID ou son nom.</summary>
public sealed class ThemeApp
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? AppId { get; set; }
    /// <summary>Raccourci web (http/https) : recréé chez le destinataire, avec son icône.</summary>
    public string? Url { get; set; }
    public string? IconFile { get; set; }
    /// <summary>Dossier du dock.</summary>
    public List<ThemeApp>? Items { get; set; }
}

/// <summary>Bilan d'un import.</summary>
public sealed record ThemeImportResult(List<string> Profiles, int Apps, List<string> Missing, string? Activated);

/// <summary>
/// Thèmes à partager (.opaline) : export de l'apparence, du style du dock et des profils (avec leurs apps,
/// leurs raccourcis web et, au choix, leurs fonds d'écran) ; import qui AJOUTE les profils à côté de ceux
/// de l'utilisateur. Le fichier vient de quelqu'un d'autre : tout est vérifié (tailles, liens http/https
/// seulement, applis gardées seulement si elles existent sur ce PC).
/// </summary>
public static partial class ThemeShareService
{
    /// <summary>Extension des thèmes exportés (5H) ; les anciens .bloomtheme (même contenu) s'ouvrent toujours.</summary>
    public const string Extension = ".opaline";
    public static readonly string[] Extensions = [Extension, ".bloomtheme"];
    private const string ProgId = "DynamicIsland.Theme";
    private static string TypeName => Loc.F("theme.type", AppInfo.Name);
    private const long MaxThemeBytes = 64L * 1024 * 1024;
    private const long MaxWallpaperBytes = 12L * 1024 * 1024;
    private const long MaxIconBytes = 1024 * 1024;
    private static readonly string[] IconExtensions = [".ico", ".png"];
    private static readonly string[] AppExtensions = [".exe", ".lnk", ".url"];
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex(@"^\{(?<guid>[0-9A-Fa-f-]{36})\}\\(?<rest>.+)$")]
    private static partial Regex KnownFolderPath();

    // ------------------------------------------------------------------ export

    /// <param name="allProfiles">Tous les profils, sinon seulement le profil actif.</param>
    /// <param name="wallpapers">Embarquer les fonds d'écran (fichier plus lourd).</param>
    public static void Export(string path, bool allProfiles, bool wallpapers)
    {
        ProfileService.CaptureActive();
        var settings = SettingsStore.Current;
        var theme = new ThemeFile
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            AppVersion = UpdateService.CurrentText,
            Created = DateTime.Now,
            Look = ThemeLook.From(settings),
            ActiveProfile = ProfileService.Active?.Name,
        };
        var embedded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // fichier → clé

        var profiles = allProfiles ? ProfileService.All.ToList() : ProfileService.Active is { } active ? [active] : [];
        foreach (var profile in profiles)
        {
            theme.Profiles.Add(new ThemeProfile
            {
                Name = profile.Name,
                Icon = profile.Icon,
                AccentStart = profile.AccentStart,
                AccentEnd = profile.AccentEnd,
                Wallpaper = wallpapers ? Embed(theme, embedded, profile.Wallpaper, MaxWallpaperBytes) : null,
                Apps = profile.Pinned.Select(pin => ToThemeApp(theme, embedded, pin)).ToList(),
            });
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(theme, Json), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private static ThemeApp ToThemeApp(ThemeFile theme, Dictionary<string, string> embedded, PinnedApp pin)
    {
        if (pin.IsFolder)
            return new ThemeApp { Name = pin.Name, Items = pin.Items!.Select(item => ToThemeApp(theme, embedded, item)).ToList() };

        // Raccourci web : son chemin n'existe que chez nous, on partage l'adresse et l'icône.
        if (pin.Path is { } path && path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            && ReadInternetShortcut(path) is ({ } url, var icon))
            return new ThemeApp { Name = pin.Name, Url = url, IconFile = Embed(theme, embedded, icon, MaxIconBytes) };

        return new ThemeApp { Name = pin.Name, Path = pin.Path, AppId = pin.AppId };
    }

    private static string? Embed(ThemeFile theme, Dictionary<string, string> embedded, string? file, long maxBytes)
    {
        if (file == null || !File.Exists(file) || new FileInfo(file).Length > maxBytes) return null;
        if (embedded.TryGetValue(file, out var existing)) return existing;
        var key = $"{theme.Files.Count + 1}{System.IO.Path.GetExtension(file).ToLowerInvariant()}";
        theme.Files[key] = Convert.ToBase64String(File.ReadAllBytes(file));
        embedded[file] = key;
        return key;
    }

    private static (string? Url, string? Icon) ReadInternetShortcut(string path)
    {
        string? url = null, icon = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) icon = line[9..].Trim();
        }
        return (url, icon);
    }

    // ------------------------------------------------------------------ lecture

    /// <summary>Lit et vérifie un thème (exception avec un message lisible s'il n'est pas valable).</summary>
    public static ThemeFile Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException(Loc.T("theme.error.not_found"));
        if (info.Length > MaxThemeBytes) throw new InvalidDataException(Loc.T("theme.error.too_big"));

        ThemeFile? theme;
        try
        {
            theme = JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            throw new InvalidDataException(Loc.F("theme.error.invalid", AppInfo.Name));
        }
        if (theme == null) throw new InvalidDataException(Loc.T("theme.error.empty"));
        if (theme.Format != 1) throw new InvalidDataException(Loc.T("theme.error.newer"));

        theme.Name = Clip(string.IsNullOrWhiteSpace(theme.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : theme.Name, 40);
        theme.Look ??= new ThemeLook();
        theme.Files ??= new();
        theme.Profiles = (theme.Profiles ?? []).Where(p => p != null).Take(20).ToList();
        foreach (var profile in theme.Profiles) profile.Apps = (profile.Apps ?? []).Where(a => a != null).Take(60).ToList();
        if (theme.Profiles.Count == 0) throw new InvalidDataException(Loc.T("theme.error.no_profile"));
        return theme;
    }

    /// <summary>Résumé pour la confirmation avant import.</summary>
    public static string Describe(ThemeFile theme)
    {
        var profiles = string.Join(", ", theme.Profiles.Select(p => Loc.F("theme.preview.profile", Clip(p.Name, 30), Count(p.Apps))));
        bool wallpapers = theme.Profiles.Any(p => p.Wallpaper != null && theme.Files.ContainsKey(p.Wallpaper));
        return Loc.F(theme.Profiles.Count > 1 ? "theme.preview.many" : "theme.preview.one", theme.Name, theme.Profiles.Count, profiles)
               + (wallpapers ? " · " + Loc.T("theme.preview.wallpapers") : "");
    }

    private static int Count(IEnumerable<ThemeApp> apps) => apps.Sum(a => a.Items != null ? a.Items.Count : 1);

    // ------------------------------------------------------------------ import

    /// <summary>
    /// Ajoute les profils du thème (jamais de suppression), applique son style et active son profil
    /// principal. Une copie des réglages est gardée avant (settings.avant-import.json).
    /// </summary>
    public static async Task<ThemeImportResult> ImportAsync(ThemeFile theme)
    {
        await AppCatalog.RefreshAsync(); // pour retrouver les applis par leur nom
        if (!SettingsStore.IsReadOnly) BackupSettings();

        var folder = System.IO.Path.Combine(DataRoot, "Thèmes", SafeName(theme.Name) + DateTime.Now.ToString("-yyyyMMdd-HHmmss"));
        var importer = new Importer(theme, folder);
        var created = new List<Profile>();
        foreach (var source in theme.Profiles)
        {
            var profile = new Profile
            {
                Name = UniqueName(Clip(source.Name, 30), created),
                Icon = Icons.Normalize(source.Icon), // icône inconnue → étoile
                AccentStart = source.AccentStart,
                AccentEnd = source.AccentEnd,
                Wallpaper = importer.WriteFile(source.Wallpaper, WallpaperService.ImageExtensions),
                Pinned = source.Apps.Select(importer.Resolve).OfType<PinnedApp>().ToList(),
            };
            profile.Normalize();
            created.Add(profile);
        }

        SettingsStore.Update(s =>
        {
            theme.Look.ApplyTo(s);
            s.Profiles.Items.AddRange(created);
        });
        int index = theme.Profiles.FindIndex(p => p.Name == theme.ActiveProfile);
        var target = created[Math.Max(0, index)];
        ProfileService.Switch(target.Id);
        Log.Info($"Thème importé : {theme.Name} ({created.Count} profils, {importer.Apps} apps, {importer.Missing.Count} introuvables)");
        return new ThemeImportResult(created.Select(p => p.Name).ToList(), importer.Apps, importer.Missing.Distinct().ToList(), target.Name);
    }

    private static string UniqueName(string name, List<Profile> pending)
    {
        if (name.Length == 0) name = Loc.T("theme.default_profile");
        bool Taken(string candidate) =>
            ProfileService.All.Concat(pending).Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        for (int i = 1; ; i++)
        {
            var candidate = i == 1 ? Loc.F("theme.imported", name) : Loc.F("theme.imported_n", name, i);
            if (!Taken(candidate)) return candidate;
        }
    }

    private static void BackupSettings()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                File.Copy(AppPaths.Settings, System.IO.Path.Combine(AppPaths.DataFolder, "settings.avant-import.json"), overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Error("Thème : copie des réglages avant import", ex);
        }
    }

    /// <summary>Retrouve les applis du thème sur ce PC et écrit les fichiers embarqués (une seule fois chacun).</summary>
    private sealed class Importer(ThemeFile theme, string folder)
    {
        private readonly Dictionary<string, string?> _written = new();

        public int Apps { get; private set; }
        public List<string> Missing { get; } = [];

        public PinnedApp? Resolve(ThemeApp app)
        {
            var name = Clip(app.Name, 60);
            if (app.Items != null)
            {
                var items = app.Items.Where(i => i is { Items: null }).Take(40).Select(Resolve).OfType<PinnedApp>().ToList();
                return items.Count switch
                {
                    0 => null,
                    1 => items[0],
                    _ => PinnedApp.Folder(name.Length > 0 ? name : Loc.T("dock.folder.default"), items),
                };
            }

            PinnedApp? pin = null;
            if (app.Url != null)
            {
                pin = CreateWebShortcut(name, app.Url, WriteFile(app.IconFile, IconExtensions));
            }
            else if (app.Path is { } path && File.Exists(path)
                     && AppExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
            {
                pin = new PinnedApp { Name = name, Path = path };
            }
            else if (app.AppId is { } id && AppCatalog.Apps.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                pin = new PinnedApp { Name = name, AppId = id };
            }
            else if (AppCatalog.Apps.FirstOrDefault(a => Compare.Compare(a.Name, name, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0) is { } entry)
            {
                // Installée ailleurs sur ce PC : on la retrouve par son nom dans le menu Démarrer.
                pin = CatalogPath(entry.Id) is { } exe ? new PinnedApp { Name = name, Path = exe } : new PinnedApp { Name = name, AppId = entry.Id };
            }

            if (pin == null) Missing.Add(name);
            else Apps++;
            return pin;
        }

        /// <summary>Écrit un fichier embarqué (extension autorisée seulement) et renvoie son chemin.</summary>
        public string? WriteFile(string? key, string[] allowedExtensions)
        {
            if (key == null || !theme.Files.TryGetValue(key, out var data)) return null;
            if (_written.TryGetValue(key, out var done)) return done;
            string? path = null;
            var extension = System.IO.Path.GetExtension(key).ToLowerInvariant();
            if (allowedExtensions.Contains(extension))
            {
                try
                {
                    var bytes = Convert.FromBase64String(data);
                    Directory.CreateDirectory(folder);
                    path = System.IO.Path.Combine(folder, $"{_written.Count + 1}{extension}");
                    File.WriteAllBytes(path, bytes);
                }
                catch (FormatException)
                {
                    path = null; // contenu abîmé : on ignore ce fichier
                }
            }
            _written[key] = path;
            return path;
        }

        /// <summary>Raccourci web (http/https uniquement) dans %APPDATA%\DynamicIsland\Raccourcis, réutilisé s'il existe déjà.</summary>
        private static PinnedApp? CreateWebShortcut(string name, string url, string? icon)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
            var shortcuts = SettingsStore.IsReadOnly ? System.IO.Path.Combine(DataRoot, "Raccourcis") : AppPaths.Shortcuts;
            Directory.CreateDirectory(shortcuts);
            foreach (var existing in Directory.GetFiles(shortcuts, "*.url"))
                if (ReadInternetShortcut(existing).Url == uri.AbsoluteUri || ReadInternetShortcut(existing).Url == url)
                    return new PinnedApp { Name = name, Path = existing };

            var baseName = SafeName(name.Length > 0 ? name : uri.Host);
            var path = System.IO.Path.Combine(shortcuts, baseName + ".url");
            for (int i = 2; File.Exists(path); i++) path = System.IO.Path.Combine(shortcuts, $"{baseName} {i}.url");
            if (icon != null)
            {
                var iconPath = System.IO.Path.ChangeExtension(path, System.IO.Path.GetExtension(icon));
                File.Copy(icon, iconPath, overwrite: true);
                icon = iconPath;
            }
            var content = new StringBuilder("[InternetShortcut]\r\n").Append("URL=").Append(uri.AbsoluteUri).Append("\r\n");
            if (icon != null) content.Append("IconFile=").Append(icon).Append("\r\nIconIndex=0\r\n");
            File.WriteAllText(path, content.ToString(), Encoding.UTF8);
            return new PinnedApp { Name = name, Path = path };
        }

        /// <summary>Chemin réel d'une appli du menu Démarrer (« C:\…\app.exe » ou « {dossier connu}\app.exe »).</summary>
        private static string? CatalogPath(string id)
        {
            if (System.IO.Path.IsPathFullyQualified(id)) return File.Exists(id) ? id : null;
            var match = KnownFolderPath().Match(id);
            if (!match.Success || !Guid.TryParse(match.Groups["guid"].Value, out var guid)) return null;
            if (SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out var root) != 0) return null;
            var path = System.IO.Path.Combine(root, match.Groups["rest"].Value);
            return File.Exists(path) ? path : null;
        }
    }

    // ------------------------------------------------------------------ double-clic sur un thème

    /// <summary>
    /// Associe les thèmes (.opaline, et les anciens .bloomtheme) à l'app (registre de l'utilisateur seulement), si besoin :
    /// aussi quand l'exe a bougé ou que le nom affiché a changé.
    /// </summary>
    public static void RegisterFileType()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var command = $"\"{exe}\" --import \"%1\"";
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        using (var current = classes.OpenSubKey($@"{ProgId}\shell\open\command"))
        using (var type = classes.OpenSubKey(ProgId))
        using (var extension = classes.OpenSubKey(Extension))
        {
            if (current?.GetValue(null) as string == command && type?.GetValue(null) as string == TypeName
                && extension?.GetValue(null) as string == ProgId) return;
        }

        foreach (var name in Extensions)
            using (var extension = classes.CreateSubKey(name)) extension.SetValue(null, ProgId);
        using (var type = classes.CreateSubKey(ProgId))
        {
            type.SetValue(null, TypeName);
            using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using (var open = type.CreateSubKey(@"shell\open\command")) open.SetValue(null, command);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED : l'Explorateur met à jour les icônes
    }

    /// <summary>Thème passé à l'exe (« --import fichier » ou fichier seul).</summary>
    public static string? ArgumentFile(string[] args)
    {
        int index = Array.IndexOf(args, "--import");
        if (index >= 0 && index + 1 < args.Length) return args[index + 1];
        return args.FirstOrDefault(a => Extensions.Any(e => a.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Filtre des boîtes de dialogue « Ouvrir » : les deux extensions.</summary>
    public static string OpenFilter => $"{TypeName} ({string.Join(", ", Extensions.Select(e => "*" + e))})|{string.Join(";", Extensions.Select(e => "*" + e))}";

    /// <summary>Filtre de la boîte « Enregistrer » : la nouvelle extension seulement.</summary>
    public static string SaveFilter => $"{TypeName} (*{Extension})|*{Extension}";

    // ------------------------------------------------------------------ utilitaires

    /// <summary>Dossier des fichiers importés (dossier temporaire en mode --snapshot).</summary>
    private static string DataRoot => SettingsStore.IsReadOnly
        ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DynamicIsland-snapshot")
        : AppPaths.DataFolder;

    private static string Clip(string? text, int max)
    {
        text = (text ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static string SafeName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return safe.Length == 0 ? Loc.T("theme.default_name") : Clip(safe, 40);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
