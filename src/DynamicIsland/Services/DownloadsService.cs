using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Dossier Téléchargements (6B) : derniers éléments arrivés, sans les fichiers encore en cours de téléchargement. Surveillé
/// (FileSystemWatcher, regroupé sur 0,5 s) seulement quand la pile du dock est affichée ; servira aussi à l'annonce
/// « téléchargement terminé » de la 6C.
/// </summary>
public static class DownloadsService
{
    /// <summary>Extensions des fichiers partiels des navigateurs et gestionnaires de téléchargement.</summary>
    private static readonly HashSet<string> Partial = new(StringComparer.OrdinalIgnoreCase)
        { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".aria2" };
    private static readonly DispatcherTimer Debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static FileSystemWatcher? _watcher;
    private static bool _started;

    /// <summary>Élément récent : chemin, nom, dossier ou fichier, date d'arrivée.</summary>
    public sealed record Entry(string Path, string Name, bool IsFolder, DateTime When);

    /// <summary>Le contenu a changé (thread UI).</summary>
    public static event Action? Changed;

    /// <summary>Moment où l'utilisateur a ouvert la pile pour la dernière fois (point « nouveau » sur la cellule).</summary>
    public static DateTime SeenUntil { get; set; } = DateTime.Now;

    public static string Folder { get; } = FindFolder();

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Debounce.Tick += (_, _) =>
        {
            Debounce.Stop();
            Changed?.Invoke();
        };
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        SettingsStore.Changed -= Apply;
        Watch(false);
        _started = false;
    }

    private static void Apply() =>
        Watch(!ModuleGuard.IsDisabled(ModuleGuard.Status) && SettingsStore.Current.Dock.ShowsStatus(StatusItem.Downloads));

    private static void Watch(bool on)
    {
        if (on == (_watcher != null)) return;
        if (!on)
        {
            _watcher!.Dispose();
            _watcher = null;
            return;
        }
        if (!Directory.Exists(Folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => Application.Current?.Dispatcher.BeginInvoke(Restart);
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Changed += changed;
            _watcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Restart);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error("Téléchargements : surveillance", ex);
            _watcher = null;
        }
    }

    private static void Restart()
    {
        Debounce.Stop();
        Debounce.Start();
    }

    /// <summary>Les <paramref name="count"/> derniers éléments (fichiers et dossiers), du plus récent au plus ancien.</summary>
    public static List<Entry> Recent(int count, string? folder = null)
    {
        folder ??= Folder;
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(IsComplete)
                .Select(info => new Entry(info.FullName, info.Name, info is DirectoryInfo,
                    info.CreationTime > info.LastWriteTime ? info.CreationTime : info.LastWriteTime))
                .OrderByDescending(e => e.When)
                .Take(count)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Téléchargements : lecture", ex);
            return [];
        }
    }

    /// <summary>Ni caché, ni système, ni encore en cours de téléchargement.</summary>
    internal static bool IsComplete(FileSystemInfo info) =>
        (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
        && !(info is FileInfo && Partial.Contains(info.Extension))
        && !info.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    public static void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Téléchargements : ouverture de « {System.IO.Path.GetFileName(path)} »", ex);
        }
    }

    public static void ShowInExplorer(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();

    private static string FindFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) == 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) return path;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
