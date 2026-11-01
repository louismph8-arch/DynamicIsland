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

        // Images par seconde effectives (bug « le halo lag sur l'île ») : la lumière bouge à CHAQUE image de WPF ; l'intervalle
        // entre deux images montre ce que coûte le rendu (fenêtre layered recopiée, halo flouté, verre).
        SettingsStore.Update(s => s.Appearance.LiquidGlass = true);
        await Task.Delay(400);
        island.BenchOnScreen(true); // sur l'écran mais invisible (cloaked) : vrai coût d'une fenêtre layered affichée
        await Task.Delay(600);
        lines.Add("Lumière qui bouge sur l'île ouverte (Musique), fenêtre sur l'écran mais invisible (cloaked) :");
        lines.Add("  « mises à jour de la lumière » = images où la lumière a vraiment changé (ce que l'œil voit bouger).");
        foreach (bool halo in new[] { true, false, true, false })
        {
            island.BenchHalo(halo);
            lines.Add($"  suivi réel, halo {(halo ? "AFFICHÉ" : "retiré")} : " + await FramesAsync(island.BenchGlass, island.BenchGlassSurface, real: true));
            lines.Add($"  à chaque image, halo {(halo ? "AFFICHÉ" : "retiré")} : " + await FramesAsync(island.BenchGlass, island.BenchGlassSurface, real: false));
        }
        island.BenchHalo(true);
        lines.Add("  au repos (curseur loin, halo affiché) : " + await FramesAsync(island.BenchGlass, null, real: true));
        island.BenchOnScreen(false);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Pendant <see cref="Duration"/>, déplace un curseur simulé autour de l'île (à chaque image de WPF) et mesure les mises à
    /// jour réelles de la lumière (<see cref="LiquidGlass.LightMoved"/>) : nombre par seconde, 95e centile et pire intervalle,
    /// CPU du processus. <paramref name="real"/> = la lumière suit ce curseur par son propre suivi (cadence et lissage réels) ;
    /// sinon elle y est posée directement à chaque image (rendu au plus vite).
    /// </summary>
    internal static async Task<string> FramesAsync(LiquidGlass glass, FrameworkElement? surface, bool real)
    {
        if (surface == null) glass.SimulateCursor(new Point(-5000, -5000), smooth: true); // repos : curseur simulé très loin
        await Task.Delay(400);
        var gaps = new List<double>(512);
        long last = 0;
        int frames = 0;
        var lastFrame = TimeSpan.Zero;
        var clock = Stopwatch.StartNew();
        void OnLight(Point light, Size size)
        {
            long now = clock.ElapsedTicks;
            if (last != 0) gaps.Add((now - last) * 1000.0 / Stopwatch.Frequency);
            last = now;
        }
        void OnFrame(object? sender, EventArgs e)
        {
            var time = ((System.Windows.Media.RenderingEventArgs)e).RenderingTime;
            if (time == lastFrame) return; // Rendering peut être levé plusieurs fois par image
            lastFrame = time;
            frames++;
            if (surface == null) return;
            double angle = clock.Elapsed.TotalSeconds * Math.PI;
            glass.SimulateCursor(new Point(surface.ActualWidth * (0.5 + 0.45 * Math.Cos(angle)),
                                           surface.ActualHeight * (0.5 + 0.8 * Math.Sin(angle))), smooth: real);
        }
        using var gpu = new GpuMeter($"pid_{Environment.ProcessId}_*engtype_3D", "Mesure du verre : GPU");
        gpu.Sample();
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var start = clock.Elapsed;
        glass.LightMoved += OnLight;
        System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        await Task.Delay(Duration);
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
        glass.LightMoved -= OnLight;
        var elapsed = clock.Elapsed - start;
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / elapsed.TotalMilliseconds * 100;
        double? gpuPercent = gpu.Sample();
        glass.SimulateCursor(null);
        string cost = $"CPU {cpu:0.0} % d'un cœur, GPU 3D {(gpuPercent is double g ? $"{g:0.0} %" : "indisponible")}, "
                      + $"{frames / elapsed.TotalSeconds:0} images WPF/s";
        if (gaps.Count == 0) return "aucune mise à jour de la lumière, " + cost;
        double rate = gaps.Count / (gaps.Sum() / 1000);
        gaps.Sort();
        double p95 = gaps[(int)(gaps.Count * 0.95)];
        return $"{gaps.Count + 1} mises à jour de la lumière ({rate:0}/s pendant qu'elle bouge), 95 % en ≤ {p95:0.0} ms, "
               + $"pire {gaps[^1]:0.0} ms, " + cost;
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
