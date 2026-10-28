using DynamicIsland.Services.Health;

namespace DynamicIsland.Services;

/// <summary>Auto-test de la santé du PC (6E) : tout est seulement LU (aucune appli n'est fermée, rien n'est écrit).</summary>
internal static partial class SelfTest
{
    private static void Health(List<string> lines)
    {
        var gpu = HealthService.Nvidia;
        var temperature = gpu.Temperature();
        Check(lines, !gpu.Available || temperature is > 0 and < 150,
            gpu.Available ? $"carte NVIDIA « {gpu.Name} » : {temperature} °C (NVML, sans droits administrateur)" : "pas de carte NVIDIA lisible : la température ne s'affiche pas");

        var disks = HealthService.Disks();
        Check(lines, disks.Count > 0 && disks.All(d => HealthService.FreePercent(d) is >= 0 and <= 100),
            "disques internes : " + string.Join(", ", disks.Select(d => $"{d.Name.TrimEnd('\\')} {HealthService.FreePercent(d):0} % libre")));

        var meter = new AppUsageMeter();
        meter.Sample();
        Thread.Sleep(600);
        var apps = meter.Sample();
        var top = apps.OrderByDescending(a => a.Memory).Take(3).Select(a => $"{a.Name} {a.Memory >> 20} Mo").ToList();
        Check(lines, apps.Count > 0 && apps.All(a => a.Cpu is >= 0 and <= 100 && a.Memory > 0)
                     && !apps.Any(a => a.Process is "explorer" or "dwm" or "csrss" or "svchost" or "DynamicIsland" or "vgc"),
            $"applis avec une fenêtre : {apps.Count} (Windows et l'anti-triche exclus) ; les plus gourmandes en mémoire : {string.Join(", ", top)}");
    }
}
