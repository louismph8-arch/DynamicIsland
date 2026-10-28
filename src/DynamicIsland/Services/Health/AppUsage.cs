using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Services.Health;

/// <summary>Une appli (tous ses processus du même nom) : processeur en % de tout le PC, mémoire en octets.</summary>
public sealed record AppUsage(string Process, string Name, double Cpu, long Memory, int Count);

/// <summary>
/// Applis qui utilisent le plus le processeur ou la mémoire en ce moment (6E). Seulement les applis qui ont une fenêtre (pas
/// les services ni Windows lui-même), tous leurs processus additionnés (Chrome, Discord…). Le processeur se calcule entre
/// deux relevés. Ne tourne que pendant que l'onglet « Santé » est ouvert.
/// </summary>
public sealed class AppUsageMeter
{
    /// <summary>Jamais proposés à la fermeture : Windows, l'anti-triche des jeux, Opaline.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "svchost", "sihost", "ctfmon", "conhost",
        "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "SystemSettings",
        "LockApp", "taskmgr", "vgc", "vgtray", "DynamicIsland",
    };

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, TimeSpan> _cpuBefore = [];
    private DateTime _before = DateTime.MinValue;

    public List<AppUsage> Sample()
    {
        var now = DateTime.UtcNow;
        double elapsedMs = _before == DateTime.MinValue ? 0 : (now - _before).TotalMilliseconds;
        var cpuNow = new Dictionary<int, TimeSpan>();
        var groups = new Dictionary<string, (double CpuMs, long Memory, int Count, bool Window, Process? Sample)>(StringComparer.OrdinalIgnoreCase);
        int own = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                if (process.Id is 0 or 4 || process.Id == own || Protected.Contains(process.ProcessName)) continue;
                long memory = process.WorkingSet64;
                double cpuMs = 0;
                try
                {
                    var cpu = process.TotalProcessorTime;
                    cpuNow[process.Id] = cpu;
                    if (_cpuBefore.TryGetValue(process.Id, out var before)) cpuMs = Math.Max(0, (cpu - before).TotalMilliseconds);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    // processus protégé : mémoire seulement
                }
                bool window = process.MainWindowHandle != IntPtr.Zero;
                var name = process.ProcessName;
                groups.TryGetValue(name, out var group);
                keep = group.Sample == null && window;
                groups[name] = (group.CpuMs + cpuMs, group.Memory + memory, group.Count + 1, group.Window || window, keep ? process : group.Sample);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // processus terminé entre-temps
            }
            finally
            {
                if (!keep) process.Dispose();
            }
        }
        _cpuBefore = cpuNow;
        _before = now;

        var result = new List<AppUsage>();
        foreach (var (process, group) in groups)
        {
            if (!group.Window) continue;
            double cpu = elapsedMs > 0 ? group.CpuMs / elapsedMs / Environment.ProcessorCount * 100 : 0;
            result.Add(new AppUsage(process, DisplayName(process, group.Sample), Math.Min(100, cpu), group.Memory, group.Count));
            group.Sample?.Dispose();
        }
        return result;
    }

    /// <summary>Nom lu dans l'exe (« Google Chrome »), sinon celui du processus ; gardé en mémoire.</summary>
    private static string DisplayName(string process, Process? sample)
    {
        if (Names.TryGetValue(process, out var known)) return known;
        string name = process;
        try
        {
            if (sample?.MainModule?.FileName is { } path && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 1 } description)
                name = description.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // appli protégée : on garde le nom du processus
        }
        return Names[process] = name;
    }

    /// <summary>
    /// Ferme l'appli comme le bouton × de ses fenêtres (elle peut proposer d'enregistrer). Renvoie le nombre de fenêtres qui
    /// ont reçu la demande (0 : rien à fermer proprement).
    /// </summary>
    public static int Close(string process)
    {
        if (Protected.Contains(process)) return 0;
        int closed = 0;
        foreach (var running in Process.GetProcessesByName(process))
        {
            using (running)
            {
                try
                {
                    if (running.MainWindowHandle != IntPtr.Zero && running.CloseMainWindow()) closed++;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        Log.Info($"Santé : fermeture demandée à une appli ({closed} fenêtre(s))");
        return closed;
    }

    /// <summary>Fermeture forcée (deuxième confirmation) : le travail non enregistré est perdu.</summary>
    public static void Kill(string process)
    {
        if (Protected.Contains(process)) return;
        foreach (var running in Process.GetProcessesByName(process))
        {
            using (running)
            {
                try
                {
                    running.Kill();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    Log.Info($"Santé : fermeture forcée refusée ({ex.GetType().Name})");
                }
            }
        }
        Log.Info("Santé : fermeture forcée d'une appli");
    }
}
