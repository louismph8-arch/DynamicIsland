using System.Diagnostics;
using System.Globalization;

namespace DynamicIsland.Services;

/// <summary>Une appli de la liste « Toutes les applis » du menu Démarrer.</summary>
/// <param name="Id">Identifiant dans shell:AppsFolder (AppUserModelID ou chemin).</param>
public sealed record AppEntry(string Name, string Id)
{
    public string ParsingName => @"shell:AppsFolder\" + Id;

    public void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", ParsingName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Recherche : lancement de « {Name} »", ex);
        }
    }
}

/// <summary>
/// Catalogue des applis installées (bureau + Store), lu dans shell:AppsFolder comme le menu Démarrer.
/// Chargé sur un thread STA dédié (l'objet Shell l'exige) puis mis en cache 5 minutes.
/// </summary>
public static class AppCatalog
{
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private static IReadOnlyList<AppEntry> _apps = [];
    private static DateTime _loadedAt = DateTime.MinValue;
    private static Task? _loading;

    public static IReadOnlyList<AppEntry> Apps => _apps;

    /// <summary>Recharge la liste si elle a plus de 5 minutes (sans bloquer l'interface).</summary>
    public static Task RefreshAsync()
    {
        if (_loading is { IsCompleted: false }) return _loading;
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(5)) return Task.CompletedTask;

        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                _apps = Load();
                _loadedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error("Recherche : liste des applis", ex);
            }
            done.SetResult();
        }) { IsBackground = true, Name = "Catalogue des applis" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _loading = done.Task;
        return done.Task;
    }

    private static List<AppEntry> Load()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application introuvable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        dynamic items = folder.Items();
        int count = items.Count;

        var apps = new List<AppEntry>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            dynamic item = items.Item(i);
            string name = item.Name;
            string id = item.Path;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            apps.Add(new AppEntry(name, id));
        }
        return apps;
    }

    /// <summary>Meilleures correspondances (sans tenir compte des majuscules ni des accents).</summary>
    public static List<AppEntry> Search(string query, int max)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        return _apps
            .Select(app => (App: app, Score: MatchScore(app.Name, query)))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.App.Name.Length)
            .Take(max)
            .Select(m => m.App)
            .ToList();
    }

    /// <summary>4 = début du nom, 3 = début d'un mot, 2 = initiales (« vsc »), 1 = contient.</summary>
    public static int MatchScore(string name, string query)
    {
        if (Compare.IsPrefix(name, query, Loose)) return 4;
        int index = Compare.IndexOf(name, query, Loose);
        if (index > 0 && !char.IsLetterOrDigit(name[index - 1])) return 3;
        var initials = string.Concat(name.Split([' ', '-', '.', '_'], StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]));
        if (query.Length >= 2 && Compare.IsPrefix(initials, query, Loose)) return 2;
        return index >= 0 ? 1 : 0;
    }
}
