using System.IO;

namespace DynamicIsland;

/// <summary>
/// Journal d'erreurs et de diagnostic (6A) : <c>%APPDATA%\DynamicIsland\Journaux\opaline.log</c>, avec rotation — à 1 Mo
/// il devient <c>opaline.1.log</c> (les plus anciens : .2 … .5, au-delà effacés). Repris tel quel (nettoyé des données
/// personnelles) par « Exporter un rapport de bug ». Toujours en français : c'est Louis qui le lit.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int KeptFiles = 5;
    private static readonly object Gate = new();

    public static string Folder { get; private set; } = Path.Combine(AppPaths.DataFolder, "Journaux");
    public static string FilePath { get; private set; } = Path.Combine(Folder, "opaline.log");

    /// <summary>
    /// Modes développeur (--snapshot, --self-test, mesures) : journal à part dans %TEMP%\Opaline-dev, pour que les essais
    /// (faux téléphone, faux serveur OpenRGB…) n'apparaissent jamais dans le journal de l'app ni dans un rapport de bug.
    /// </summary>
    public static void UseDeveloperFile()
    {
        Folder = Path.Combine(Path.GetTempPath(), "Opaline-dev");
        FilePath = Path.Combine(Folder, "opaline.log");
    }

    public static void Error(string context, Exception ex) => Write($"{context} : {ex}");

    /// <summary>Information de diagnostic (rare : une ligne au démarrage, par exemple).</summary>
    public static void Info(string message) => Write("INFO " + message);

    /// <summary>Fichiers du journal, du plus récent au plus ancien.</summary>
    public static IEnumerable<string> Files() =>
        new[] { FilePath }.Concat(Enumerable.Range(1, KeptFiles).Select(Rotated)).Where(File.Exists);

    private static string Rotated(int index) => Path.Combine(Folder, $"opaline.{index}.log");

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) Rotate();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le journal ne doit jamais faire planter l'app (fichier pris par l'autre processus, disque plein…).
        }
    }

    /// <summary>opaline.log → .1, .1 → .2 … ; le plus ancien est effacé.</summary>
    private static void Rotate()
    {
        if (File.Exists(Rotated(KeptFiles))) File.Delete(Rotated(KeptFiles));
        for (int i = KeptFiles - 1; i >= 1; i--)
            if (File.Exists(Rotated(i))) File.Move(Rotated(i), Rotated(i + 1));
        File.Move(FilePath, Rotated(1));
    }
}
