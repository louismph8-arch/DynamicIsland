using System.IO;
using System.Text.Json;

namespace DynamicIsland.Services;

/// <summary>
/// Étagère à fichiers : de simples <b>références</b> (chemins) vers des fichiers ou dossiers, jamais de
/// copie. Gardée dans %APPDATA%\DynamicIsland\shelf.json pour survivre à un redémarrage ; les éléments
/// qui n'existent plus sont retirés au chargement.
/// </summary>
public static class ShelfService
{
    private static readonly List<string> Paths = [];
    private static bool _temporary;

    public static IReadOnlyList<string> Items => Paths;

    /// <summary>Levé sur le thread UI quand l'étagère change.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Shelf)) return;
            var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.Shelf)) ?? [];
            Paths.Clear();
            Paths.AddRange(saved.Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error("Étagère : lecture", ex);
        }
    }

    /// <summary>Ajoute des fichiers/dossiers (sans doublon) ; renvoie le nombre réellement ajoutés.</summary>
    public static int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!Exists(path) || Paths.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            Paths.Add(path);
            added++;
        }
        if (added > 0) Commit();
        return added;
    }

    public static void Remove(string path)
    {
        if (Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) Commit();
    }

    public static void Clear()
    {
        if (Paths.Count == 0) return;
        Paths.Clear();
        Commit();
    }

    /// <summary>Mode --snapshot : contenu d'exemple, jamais sauvegardé.</summary>
    public static void UseTemporaryItems(IEnumerable<string> paths)
    {
        _temporary = true;
        Paths.Clear();
        Paths.AddRange(paths.Where(Exists));
        Changed?.Invoke();
    }

    /// <summary>Retire ce qui a disparu (fichier supprimé ou déplacé entre-temps).</summary>
    public static void Prune()
    {
        if (Paths.RemoveAll(p => !Exists(p)) > 0) Commit();
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Commit()
    {
        Changed?.Invoke();
        if (_temporary) return;
        try
        {
            AppPaths.EnsureDataFolder();
            File.WriteAllText(AppPaths.Shelf, JsonSerializer.Serialize(Paths));
        }
        catch (Exception ex)
        {
            Log.Error("Étagère : sauvegarde", ex);
        }
    }
}
