using System.Diagnostics;
using System.Windows;
using DynamicIsland.Controls;
using DynamicIsland.Dock;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Mode développeur <c>--snapshot &lt;dossier&gt; --glass-bench &lt;fichier&gt;</c> : coût du verre liquide. Île
/// (ouverte sur Musique) et dock hors écran ; une lumière simulée tourne autour de chaque forme au rythme du
/// suivi réel (<see cref="Themes.Glass.ActiveTick"/>, pire cas : souris qui bouge sans arrêt tout près), verre
/// coupé puis actif. CPU et GPU 3D de l'app
/// seulement. La vraie souris n'est jamais touchée et les réglages restent en lecture seule.
/// </summary>
internal static class GlassBench
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    public static async Task<string> RunAsync(IslandWindow island, DockWindow dock)
    {
        await Task.Delay(2500); // services (média, écrans) et mise en page
        island.PrepareGlassBench();
        await Task.Delay(500); // mise en page de l'île ouverte
        var lines = new List<string>
        {
            $"Verre liquide — mesure du {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.ProcessorCount} cœurs logiques",
            $"Lumière déplacée toutes les {Themes.Glass.ActiveTick.TotalMilliseconds:0} ms autour de la forme (souris qui bouge sans arrêt tout près).",
        };
        foreach (var (name, glass, surface) in new[]
                 {
                     ("Île ouverte (Musique)", island.BenchGlass, island.BenchGlassSurface),
                     ("Dock", dock.BenchGlass, dock.BenchGlassSurface),
                 })
        {
            lines.Add($"{name} : {surface.ActualWidth:0}×{surface.ActualHeight:0} DIP");
            // En alternance (coupé, actif, coupé, actif) : le bruit du reste de l'app se voit et s'équilibre.
            foreach (bool on in new[] { false, true, false, true }) lines.Add("  " + await MeasureAsync(glass, surface, on));
        }
        lines.Add("Repos (verre actif, curseur loin) : " + await MeasureAsync(null, null, on: true));
        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> MeasureAsync(LiquidGlass? glass, FrameworkElement? surface, bool on)
    {
        SettingsStore.Update(s => s.Appearance.LiquidGlass = on);
        await Task.Delay(400);
        using var gpu = new GpuMeter($"pid_{Environment.ProcessId}_*engtype_3D", "Mesure du verre : GPU");
        gpu.Sample(); // référence
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        int steps = 0;
        while (clock.Elapsed < Duration)
        {
            if (glass != null && surface != null)
            {
                double angle = clock.Elapsed.TotalSeconds * Math.PI; // un tour toutes les 2 s
                glass.SimulateCursor(new Point(surface.ActualWidth * (0.5 + 0.45 * Math.Cos(angle)),
                                               surface.ActualHeight * (0.5 + 0.8 * Math.Sin(angle))));
                steps++;
            }
            await Task.Delay(Themes.Glass.ActiveTick);
        }
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100;
        double? gpuPercent = gpu.Sample();
        glass?.SimulateCursor(null);
        string updates = steps > 0 ? $", {steps / clock.Elapsed.TotalSeconds:0} mises à jour/s" : "";
        return $"verre {(on ? "ACTIF" : "coupé")} : CPU {cpu:0.0} % d'un cœur ({cpu / Environment.ProcessorCount:0.00} % du PC), "
               + $"GPU 3D {(gpuPercent is double g ? $"{g:0.0} %" : "indisponible")}{updates}";
    }
}
