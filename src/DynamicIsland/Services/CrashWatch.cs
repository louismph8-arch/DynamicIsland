using System.IO;

namespace DynamicIsland.Services;

/// <summary>
/// Redémarrage après un plantage (correctif urgent 1). L'app note « running &lt;pid&gt; » au démarrage et « clean » quand elle se
/// ferme normalement (Quitter, mise à jour, redémarrage, désinstallation). Le processus gardien (<see cref="ShellGuard"/>) attend sa
/// fin : si elle n'est PAS notée « clean » et que le code de sortie est celui d'un plantage (pas 0, pas 1 = « Fin de tâche » du
/// Gestionnaire des tâches), il la relance — 3 fois au plus en 10 minutes, puis en mode sans échec avec un message clair.
/// </summary>
public static class CrashWatch
{
    public const string AfterCrashArgument = "--after-crash";
    public const int MaxRestarts = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private static string Marker => Path.Combine(AppPaths.DataFolder, "session.txt");
    private static string History => Path.Combine(AppPaths.DataFolder, "plantages.txt");

    /// <summary>
    /// Relancée après au moins 2 plantages en 10 minutes : verre liquide et fond animé coupés pour cette session (un plantage du
    /// pilote graphique ne peut pas être attrapé ; les réglages ne sont pas modifiés).
    /// </summary>
    public static bool ReducedEffects { get; private set; }

    /// <summary>Lancement par le gardien (« --after-crash ») : décide des effets réduits.</summary>
    public static void AfterCrash(DateTime now)
    {
        ReducedEffects = RecentCrashes(now) >= 2;
        if (ReducedEffects) Log.Info("Gardien : plusieurs plantages rapprochés, verre liquide et fond animé coupés pour cette session");
    }

    /// <summary>Démarrage de l'instance principale.</summary>
    public static void SessionStarted() => Write(Marker, $"running {Environment.ProcessId}");

    /// <summary>Fermeture normale : le gardien ne relancera rien.</summary>
    public static void SessionEnded() => Write(Marker, "clean");

    /// <summary>
    /// Dans le gardien, l'app étant terminée : arguments de relance (« --after-crash », + « --safe-mode » à partir du 4e plantage en
    /// 10 min), ou null s'il ne faut rien relancer (fermeture normale, fin de tâche voulue).
    /// </summary>
    public static string? DecideRestart(int processId, int exitCode, DateTime now)
    {
        string marker;
        try
        {
            marker = File.Exists(Marker) ? File.ReadAllText(Marker).Trim() : "";
        }
        catch (IOException)
        {
            return null;
        }
        var (arguments, recent) = Decide(marker, processId, exitCode, ReadHistory(), now);
        if (arguments == null) return null;
        Write(History, string.Join(Environment.NewLine, recent.Select(t => t.ToString("O"))));
        Log.Info($"Gardien : l'app s'est arrêtée brutalement (code 0x{exitCode:X8}), {recent.Count} fois en {Window.TotalMinutes:0} min");
        return arguments;
    }

    /// <summary>Décision seule (sans fichier) : vérifiée par l'auto-test.</summary>
    internal static (string? Arguments, List<DateTime> Recent) Decide(string marker, int processId, int exitCode, IEnumerable<DateTime> history, DateTime now)
    {
        if (marker != $"running {processId}" || !IsCrashCode(exitCode)) return (null, []);
        var recent = history.Where(t => now - t < Window).Append(now).ToList();
        return (recent.Count <= MaxRestarts ? AfterCrashArgument : $"{AfterCrashArgument} --safe-mode", recent);
    }

    /// <summary>0 = fermeture normale, 1 = « Fin de tâche » / taskkill : voulu par l'utilisateur, on ne relance pas.</summary>
    internal static bool IsCrashCode(int exitCode) => exitCode is not (0 or 1);

    /// <summary>Plantages récents (pour le message du mode sans échec).</summary>
    public static int RecentCrashes(DateTime now) => ReadHistory().Count(t => now - t < Window);

    private static List<DateTime> ReadHistory()
    {
        try
        {
            if (!File.Exists(History)) return [];
            return File.ReadAllLines(History)
                .Select(line => DateTime.TryParse(line, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : (DateTime?)null)
                .Where(t => t != null).Select(t => t!.Value).ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void Write(string path, string text)
    {
        try
        {
            AppPaths.EnsureDataFolder();
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Gardien : fichier de session", ex);
        }
    }
}
