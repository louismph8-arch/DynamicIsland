using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland.Services;

/// <summary>
/// Suivi de la mémoire (7A) : une ligne dans le journal 2 minutes après le démarrage puis toutes les 30 minutes — mémoire
/// privée, tas .NET, objets GDI / USER, poignées. Sert à voir une fuite (ça monte sans redescendre) dans un rapport de bug.
/// Ne fait rien d'autre : pas de nettoyage forcé.
/// </summary>
internal static class MemoryWatch
{
    private static readonly TimeSpan First = TimeSpan.FromMinutes(2), Every = TimeSpan.FromMinutes(30);
    private static readonly DispatcherTimer Timer = new() { Interval = First };

    public static void Start()
    {
        Timer.Tick += (_, _) =>
        {
            Timer.Interval = Every;
            Log.Info(Describe());
        };
        Timer.Start();
    }

    public static string Describe()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        return $"Mémoire : privée {process.PrivateMemorySize64 / 1048576.0:0} Mo, ensemble de travail {process.WorkingSet64 / 1048576.0:0} Mo, " +
               $"tas .NET {gc.HeapSizeBytes / 1048576.0:0} Mo (réservé {gc.TotalCommittedBytes / 1048576.0:0} Mo), " +
               $"GDI {GetGuiResources(process.Handle, 0)}, USER {GetGuiResources(process.Handle, 1)}, poignées {process.HandleCount}, " +
               $"fils {process.Threads.Count}, depuis {(DateTime.Now - process.StartTime).TotalMinutes:0} min";
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
