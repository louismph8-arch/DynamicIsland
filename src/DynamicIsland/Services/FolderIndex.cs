using System.IO;

namespace DynamicIsland.Services;

/// <summary>Un dossier proposé par la palette.</summary>
public sealed record FolderEntry(string Name, string Path, int Depth);

/// <summary>
/// Dossiers proposés par la palette : les dossiers habituels (Bureau, Documents, Téléchargements…) et leurs
/// sous-dossiers sur deux niveaux. Parcourus en arrière-plan (dossiers cachés, système et liens ignorés),
/// gardés 10 minutes.
/// </summary>
public static class FolderIndex
{
    private const int MaxEntries = 6000;
    private static readonly string[] Skipped = ["node_modules", "bin", "obj", ".git", "$RECYCLE.BIN", "AppData"];

    private static IReadOnlyList<FolderEntry> _folders = [];
    private static DateTime _loadedAt = DateTime.MinValue;
    private static Task? _loading;

    public static Task RefreshAsync()
    {
        if (_loading is { IsCompleted: false }) return _loading;
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(10)) return Task.CompletedTask;
        _loading = Task.Run(() =>
        {
            try
            {
                _folders = Load();
                _loadedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error("Palette : liste des dossiers", ex);
            }
        });
        return _loading;
    }

    /// <summary>Meilleures correspondances (même classement que les applis), les dossiers proches de la racine d'abord.</summary>
    public static List<FolderEntry> Search(string query, int max)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        return _folders
            .Select(folder => (Folder: folder, Score: AppCatalog.MatchScore(folder.Name, query)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Folder.Depth)
            .ThenBy(match => match.Folder.Name.Length)
            .Take(max)
            .Select(match => match.Folder)
            .ToList();
    }

    private static List<FolderEntry> Load()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string Name, string Path, int Levels)[] roots =
        [
            (Loc.T("folders.desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 2),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 2),
            (Loc.T("folders.downloads"), Path.Combine(home, "Downloads"), 2),
            (Loc.T("folders.pictures"), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 1),
            (Loc.T("folders.music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), 1),
            (Loc.T("folders.videos"), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), 1),
            ("OneDrive", Environment.GetEnvironmentVariable("OneDrive") ?? "", 1),
            (Loc.T("folders.home"), home, 1),
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<FolderEntry>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };

        foreach (var (name, root, levels) in roots)
        {
            if (root.Length == 0 || !Directory.Exists(root) || !seen.Add(root)) continue;
            folders.Add(new FolderEntry(name, root, 0));
            var level = new List<string> { root };
            for (int depth = 1; depth <= levels && folders.Count < MaxEntries; depth++)
            {
                var next = new List<string>();
                foreach (var parent in level)
                {
                    foreach (var child in Directory.EnumerateDirectories(parent, "*", options))
                    {
                        var childName = Path.GetFileName(child);
                        if (childName.StartsWith('.') || Skipped.Contains(childName, StringComparer.OrdinalIgnoreCase) || !seen.Add(child)) continue;
                        folders.Add(new FolderEntry(childName, child, depth));
                        next.Add(child);
                        if (folders.Count >= MaxEntries) break;
                    }
                    if (folders.Count >= MaxEntries) break;
                }
                level = next;
            }
        }
        return folders;
    }
}
