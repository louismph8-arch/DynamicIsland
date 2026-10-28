using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// Temps de démarrage (7A) : du lancement du processus à la première image de l'île, puis jusqu'à ce que tous les modules
/// aient démarré (ils démarrent juste après la première image), avec ce que coûte chacun sur le thread de l'interface.
/// Une seule ligne dans le journal, une fois tout démarré.
/// </summary>
internal static class StartupTimer
{
    private static readonly Dictionary<string, double> Modules = [];
    private static DateTime _launch;
    private static TimeSpan _runtime;
    private static TimeSpan? _islandShown, _ready;
    private static bool _running;

    /// <summary>Au tout début de <c>OnStartup</c> (instance principale) : .NET et WPF sont prêts.</summary>
    public static void Begin()
    {
        using var process = Process.GetCurrentProcess();
        _launch = process.StartTime;
        _runtime = DateTime.Now - _launch;
        _running = true;
    }

    /// <summary>Durée d'un module lancé par <c>RunModule</c> pendant le démarrage (additionnée par nom).</summary>
    public static void Record(string name, long startTimestamp)
    {
        if (!_running) return;
        double ms = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        Modules[name] = Modules.GetValueOrDefault(name) + ms;
    }

    public static bool Running => _running;

    /// <summary>Première image de l'île.</summary>
    public static void IslandShown()
    {
        if (_islandShown != null || _launch == default) return;
        _islandShown = DateTime.Now - _launch;
        WriteWhenComplete();
    }

    /// <summary>Tous les modules ont démarré.</summary>
    public static void Finish()
    {
        if (!_running) return;
        _running = false;
        _ready = DateTime.Now - _launch;
        WriteWhenComplete();
    }

    /// <summary>Une ligne dans le journal quand on connaît les deux moments (dans n'importe quel ordre).</summary>
    private static void WriteWhenComplete()
    {
        if (_islandShown is not { } shown || _ready is not { } ready) return;
        double total = Modules.Values.Sum();
        var slowest = Modules.Where(m => m.Value >= 15).OrderByDescending(m => m.Value).Take(6)
                             .Select(m => $"{m.Key} {m.Value:0} ms");
        Log.Info($"Démarrage : île affichée {shown.TotalSeconds:0.00} s après le lancement, tout démarré à {ready.TotalSeconds:0.00} s " +
                 $"(.NET et WPF {_runtime.TotalSeconds:0.00} s, modules {total / 1000:0.00} s) ; les plus lents : {string.Join(", ", slowest)}");
    }
}
