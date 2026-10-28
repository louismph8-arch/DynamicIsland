using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Fond d'écran des profils et du fond dynamique (la même image sur tous les écrans). Avant de le changer pour la
/// première fois, on sauvegarde une copie du fond d'origine : un profil sans fond d'écran le remet.
/// Les appels à Windows se font hors du thread de l'interface (ils peuvent prendre une seconde) ; on passe par
/// IDesktopWallpaper (Windows enchaîne les fonds en fondu), sinon par SystemParametersInfo.
/// </summary>
public static class WallpaperService
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;
    private const string BackupName = "fond-d-origine";

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".jfif", ".webp"];

    public static void ApplyFor(Profile profile)
    {
        if (SettingsStore.IsReadOnly) return; // rendu --snapshot : le vrai fond d'écran n'est jamais touché
        if (SettingsStore.Current.Wallpaper.Dynamic) return; // le fond dynamique a la main (il remet celui du profil quand on le coupe)
        try
        {
            var settings = SettingsStore.Current.Profiles;
            string? target = profile.Wallpaper is { } path && File.Exists(path) ? path : null;
            bool restoring = target == null;
            // Profil sans fond : on remet le fond d'origine, sauf si Windows l'a toujours.
            if (restoring) target = settings.WallpaperChanged ? FindBackup() : null;
            if (target == null) return;

            string file = target;
            bool backupFirst = !settings.WallpaperChanged && !restoring;
            settings.WallpaperChanged = !restoring;
            SettingsStore.ScheduleSave();
            Task.Run(() =>
            {
                try
                {
                    if (backupFirst) Backup();
                    Set(file);
                }
                catch (Exception ex)
                {
                    Log.Error("Fond d'écran", ex);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("Fond d'écran", ex);
        }
    }

    /// <summary>Fond dynamique : met cette image (le fond d'origine est sauvé avant le premier changement).</summary>
    public static void Apply(string file)
    {
        if (SettingsStore.IsReadOnly) return;
        var settings = SettingsStore.Current.Profiles;
        bool backupFirst = !settings.WallpaperChanged;
        if (backupFirst)
        {
            settings.WallpaperChanged = true;
            SettingsStore.ScheduleSave();
        }
        Task.Run(() =>
        {
            try
            {
                if (backupFirst) Backup();
                Set(file);
            }
            catch (Exception ex)
            {
                Log.Error("Fond d'écran", ex);
            }
        });
    }

    /// <summary>Même image sur tous les écrans, en remplissage, avec le fondu de Windows si possible.</summary>
    private static void Set(string file)
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
                wallpaper.SetWallpaper(null, file);
                return;
            }
            finally
            {
                Marshal.ReleaseComObject(wallpaper);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Error("Fond d'écran (IDesktopWallpaper)", ex);
        }
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, file, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            Log.Info($"Fond d'écran refusé par Windows : {file} (erreur {Marshal.GetLastWin32Error()})");
    }

    /// <summary>
    /// Avant le premier changement : réglages du fond de Windows (chemin, ajustement) notés dans
    /// <see cref="WindowsBackup"/> (section « Wallpaper ») et copie de l'image dans %APPDATA%\DynamicIsland (rien à copier
    /// si c'est une couleur unie).
    /// </summary>
    private static void Backup()
    {
        WindowsBackup.SaveIfMissing(Section, Values);
        var current = new StringBuilder(1024);
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0)) return;
        var source = current.ToString();
        if (source.Length == 0 || !File.Exists(source)) return;
        if (IsOurs(source)) return; // c'est déjà la copie du fond d'origine (remise par un profil sans fond) : on la garde

        AppPaths.EnsureDataFolder();
        foreach (var old in Directory.GetFiles(AppPaths.DataFolder, BackupName + ".*")) File.Delete(old);
        var extension = Path.GetExtension(source);
        if (!ImageExtensions.Contains(extension.ToLowerInvariant())) extension = ".jpg"; // « TranscodedWallpaper » n'a pas d'extension
        File.Copy(source, Path.Combine(AppPaths.DataFolder, BackupName + extension), overwrite: true);
    }

    /// <summary>Image affichée par Windows (Wallpaper Engine y met une image de son fond animé), null si couleur unie.</summary>
    public static string? CurrentPath()
    {
        var current = new StringBuilder(1024);
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0)) return null;
        var path = current.ToString();
        return path.Length > 0 && File.Exists(path) ? path : null;
    }

    private static string? FindBackup() =>
        Directory.Exists(AppPaths.DataFolder) ? Directory.GetFiles(AppPaths.DataFolder, BackupName + ".*").FirstOrDefault() : null;

    // ------------------------------------------------------------------ désinstallation (5H)

    internal const string Section = "Wallpaper";
    private const string DesktopKey = @"Control Panel\Desktop";
    /// <summary>Ce que Windows retient du fond : l'image, son ajustement (remplir, ajuster…), la mosaïque.</summary>
    internal static readonly (string Key, string Name)[] Values =
        [(DesktopKey, "Wallpaper"), (DesktopKey, "WallpaperStyle"), (DesktopKey, "TileWallpaper")];

    /// <summary>
    /// Désinstallation (<c>--restore-windows</c>) : si le fond affiché vient de l'app (pack, fond dynamique, fond d'un
    /// profil), remet celui d'avant elle avec son ajustement — son fichier d'origine s'il existe encore, sinon la copie
    /// sauvée, recopiée dans le dossier Images (le dossier de l'app peut être supprimé). Un fond choisi ensuite dans
    /// Windows est laissé tel quel. Renvoie vrai si le fond a été remis.
    /// </summary>
    public static bool RestoreOriginal()
    {
        var saved = WindowsBackup.Get(Section);
        var plan = RestorePlan(saved, SettingsStore.Current.Profiles.WallpaperChanged, CurrentPath(), FindBackup(), IsOurs, File.Exists);
        if (plan == null)
        {
            WindowsBackup.Forget(Section); // rien à remettre, ou l'utilisateur a choisi un autre fond depuis
            return false;
        }
        string target = plan == BackupMarker ? KeepCopy(FindBackup()!) : plan;

        WindowsBackup.Restore(Section); // ajustement d'origine d'abord : Windows le relit en changeant l'image
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, target, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
        {
            Log.Info($"Fond d'origine refusé par Windows : {target} (erreur {Marshal.GetLastWin32Error()})");
            return false;
        }
        return true;
    }

    /// <summary><see cref="RestorePlan"/> : « remettre la copie sauvée » (recopiée d'abord dans Images).</summary>
    internal const string BackupMarker = "<copie>";

    /// <summary>
    /// Décision de la désinstallation, sans rien toucher (testée par <c>--self-test</c>) : null = ne rien remettre,
    /// "" = couleur unie, <see cref="BackupMarker"/> = la copie sauvée, sinon le fichier d'origine.
    /// </summary>
    internal static string? RestorePlan(IReadOnlyList<WindowsBackup.Value>? saved, bool changedFlag, string? current, string? backup,
        Func<string, bool> isOurs, Func<string, bool> exists)
    {
        if (!changedFlag && saved == null) return null;     // l'app n'a jamais changé le fond
        if (current == null || !isOurs(current)) return null; // fond choisi depuis dans Windows (ou couleur unie) : on le garde
        string? original = saved?.FirstOrDefault(v => v.Name == "Wallpaper")?.Data;
        if (saved != null && string.IsNullOrEmpty(original)) return "";
        if (original != null && exists(original) && !isOurs(original) && !IsTranscoded(original)) return original;
        return backup != null ? BackupMarker : null;
    }

    /// <summary>Image mise par l'app : dans son dossier (pack, fond dynamique, thèmes importés) ou fond d'un profil.</summary>
    internal static bool IsOurs(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(Path.GetFullPath(AppPaths.DataFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        return SettingsStore.Current.Profiles.Items.Any(p => p.Wallpaper is { } wallpaper && string.Equals(Path.GetFullPath(wallpaper), full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>« TranscodedWallpaper » : la copie interne de Windows, réécrite à chaque changement de fond.</summary>
    private static bool IsTranscoded(string path) => Path.GetFileName(path).StartsWith("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copie la sauvegarde dans Images\« Fond d'écran d'origine » pour qu'elle survive à la désinstallation.</summary>
    private static string KeepCopy(string backup)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Loc.T("wallpaper.original_file") + Path.GetExtension(backup));
        for (int i = 2; File.Exists(path) && !SameFile(path, backup); i++)
            path = Path.Combine(folder, $"{Loc.T("wallpaper.original_file")} ({i}){Path.GetExtension(backup)}");
        File.Copy(backup, path, overwrite: true);
        return path;
    }

    private static bool SameFile(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private enum DesktopWallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }

    /// <summary>Interface du Shell pour le fond d'écran (shobjidl.h) ; méthodes dans l'ordre de la vtable.</summary>
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Interop.NativeMethods.RECT rect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
    }

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass
    {
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint flags);
}
