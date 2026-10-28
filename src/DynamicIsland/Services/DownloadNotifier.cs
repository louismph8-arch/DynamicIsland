using System.IO;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// « Téléchargement terminé » (6C) : un nouveau fichier complet dans Téléchargements. Les navigateurs écrivent d'abord un
/// fichier partiel (.crdownload, .part…) puis le renomment ; d'autres écrivent directement : on attend donc que le fichier ne
/// soit plus ouvert en écriture (ouverture en lecture qui refuse les écrivains) avant de l'annoncer. Plusieurs fichiers en
/// même temps (archive décompressée…) = une seule annonce.
/// </summary>
public static class DownloadNotifier
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly Dictionary<string, DateTime> Candidates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> Announced = new(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher? _watcher;

    /// <summary>Fichiers terminés (thread UI), du plus récent au plus ancien.</summary>
    public static event Action<IReadOnlyList<string>>? Finished;

    public static void Start()
    {
        Timer.Tick -= OnTick;
        Timer.Tick += OnTick;
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        SettingsStore.Changed -= Apply;
        Watch(false);
    }

    private static void Apply() =>
        Watch(SettingsStore.Current.Island.DownloadNotices && !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Notices));

    private static void Watch(bool on)
    {
        if (on == (_watcher != null)) return;
        if (!on)
        {
            _watcher!.Dispose();
            _watcher = null;
            Timer.Stop();
            Candidates.Clear();
            return;
        }
        if (!Directory.Exists(DownloadsService.Folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(DownloadsService.Folder)
            {
                // Créations et renommages seulement : un fichier modifié plus tard (document enregistré là) n'est pas un téléchargement.
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            _watcher.Created += (_, e) => Candidate(e.FullPath);
            _watcher.Renamed += (_, e) => Candidate(e.FullPath);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error("Téléchargements : surveillance des annonces", ex);
            _watcher = null;
        }
    }

    /// <summary>Événement du système de fichiers (thread de travail) : à vérifier un peu plus tard.</summary>
    private static void Candidate(string path)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_watcher == null) return;
            Candidates[path] = DateTime.UtcNow;
            if (!Timer.IsEnabled) Timer.Start();
        });
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var old in Announced.Where(a => now - a.Value > TimeSpan.FromMinutes(2)).Select(a => a.Key).ToList()) Announced.Remove(old);

        var done = new List<string>();
        foreach (var (path, seen) in Candidates.ToList())
        {
            if (now - seen < Settle) continue; // encore des événements récents : on attend
            switch (State(path))
            {
                case FileState.Gone:
                    Candidates.Remove(path);
                    break;
                case FileState.Complete:
                    Candidates.Remove(path);
                    if (Announced.TryAdd(path, now)) done.Add(path);
                    break;
                case FileState.Busy:
                    Candidates[path] = now; // toujours en cours d'écriture
                    break;
            }
        }
        if (Candidates.Count == 0) Timer.Stop();
        if (done.Count > 0) Finished?.Invoke(done);
    }

    internal enum FileState { Gone, Busy, Complete }

    /// <summary>Fichier terminé : existe, ni partiel ni caché, non vide, et plus personne ne l'écrit.</summary>
    internal static FileState State(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || !DownloadsService.IsComplete(info)) return FileState.Gone;
            if (info.Length == 0) return FileState.Busy; // Firefox crée le fichier vide au début du téléchargement
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); // échoue si quelqu'un écrit encore
            return FileState.Complete;
        }
        catch (IOException)
        {
            return File.Exists(path) ? FileState.Busy : FileState.Gone;
        }
        catch (UnauthorizedAccessException)
        {
            return FileState.Gone;
        }
    }
}
