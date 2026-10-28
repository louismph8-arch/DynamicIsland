using System.Diagnostics;
using DynamicIsland.Dock;
using DynamicIsland.Services.Seasons;

namespace DynamicIsland.Services;

/// <summary>
/// Mode développeur <c>--snapshot &lt;dossier&gt; --perf-bench &lt;fichier&gt;</c> (7A) : ce que coûte chaque chose qui peut
/// tourner « au repos », mesurée à part dans un processus neuf (île et dock hors écran, réglages en lecture seule). Chaque
/// mesure est encadrée par une mesure de référence (rien d'animé) pour que le bruit du reste se voie. CPU et GPU 3D de
/// l'app seulement : Windows, le bureau et l'app installée ne sont jamais touchés.
/// </summary>
internal static class PerfBench
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);

    public static async Task<string> RunAsync(IslandWindow island, DockWindow dock)
    {
        await Task.Delay(3000); // services (média, écrans), mise en page et intro terminées
        island.BenchSeason(Season.None, run: false);
        var lines = new List<string>
        {
            $"Audit de performance — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.ProcessorCount} cœurs logiques, " +
            $"rendu WPF niveau {System.Windows.Media.RenderCapability.Tier >> 16}",
            $"Chaque mesure dure {Duration.TotalSeconds:0} s. « % d'un cœur » : 100 % = un cœur logique entier.",
            "Référence (île en pilule, dock, rien d'animé) : " + await MeasureAsync(),
        };

        foreach (var season in new[] { Season.Halloween, Season.Christmas })
        {
            island.BenchSeason(season, run: true);
            lines.Add($"Île en pilule + {season} (juste après l'apparition) : " + await MeasureAsync());
            island.BenchSeasonSettle();
            lines.Add($"Île en pilule + {season} (au repos depuis un moment) : " + await MeasureAsync());
            island.BenchSeason(Season.None, run: false);
            lines.Add("  référence : " + await MeasureAsync());
        }

        island.BenchSeason(Season.Halloween, run: true);
        island.BenchExpand(true);
        lines.Add("Île ouverte + Halloween : " + await MeasureAsync());
        island.BenchExpand(false);
        island.BenchSeason(Season.None, run: false);
        lines.Add("  référence : " + await MeasureAsync());

        island.BenchSeason(Season.Halloween, run: true);
        island.BenchGaming(true);
        lines.Add("Mode Gaming (Halloween demandé) : " + await MeasureAsync());
        island.BenchGaming(false);
        island.BenchSeason(Season.None, run: false);
        lines.Add("  référence : " + await MeasureAsync());

        lines.Add(await WallpaperBench.RunAsync());
        lines.Add(Memory());
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> MeasureAsync()
    {
        await Task.Delay(500); // laisse retomber le changement d'avant
        using var gpu = new GpuMeter($"pid_{Environment.ProcessId}_*engtype_3D", "Audit : GPU");
        gpu.Sample(); // référence
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Task.Delay(Duration);
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100;
        double? gpuPercent = gpu.Sample();
        return $"CPU {cpu:0.0} % d'un cœur ({cpu / Environment.ProcessorCount:0.00} % du PC), " +
               $"GPU 3D {(gpuPercent is double g ? $"{g:0.0} %" : "indisponible")}";
    }

    /// <summary>Mémoire du processus de mesure : tas .NET, total privé, ensemble de travail.</summary>
    internal static string Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var info = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        return $"Mémoire : tas .NET {GC.GetTotalMemory(false) / 1048576.0:0} Mo (réservé {info.TotalCommittedBytes / 1048576.0:0} Mo), " +
               $"privée {process.PrivateMemorySize64 / 1048576.0:0} Mo, ensemble de travail {process.WorkingSet64 / 1048576.0:0} Mo";
    }
}
