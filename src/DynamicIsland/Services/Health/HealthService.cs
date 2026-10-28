using System.IO;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services.Health;

/// <summary>
/// Santé du PC (6E, module « health ») : surveille l'espace libre des disques internes toutes les 5 minutes et prévient une
/// fois quand l'un passe sous 10 % (de nouveau seulement s'il est remonté au-dessus de 12 %). Donne aussi la carte NVIDIA
/// (température) à l'onglet « Santé » de l'île.
/// </summary>
public static class HealthService
{
    public const double LowDiskPercent = 10;
    private const double ClearDiskPercent = 12;
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);

    private static readonly Lazy<NvidiaGpu> Gpu = new(() => new NvidiaGpu());
    private static readonly HashSet<string> Warned = new(StringComparer.OrdinalIgnoreCase);
    private static DispatcherTimer? _timer;

    /// <summary>Un disque vient de passer sous 10 % d'espace libre (thread de l'interface).</summary>
    public static event Action<DriveInfo>? LowDisk;

    /// <summary>Carte NVIDIA (créée au premier usage, une seule fois).</summary>
    public static NvidiaGpu Nvidia => Gpu.Value;

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = FirstCheck };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = CheckEvery;
            Check();
        };
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        if (Gpu.IsValueCreated) Gpu.Value.Dispose();
    }

    /// <summary>Disques internes prêts (pas les clés USB ni les lecteurs réseau).</summary>
    public static IReadOnlyList<DriveInfo> Disks()
    {
        var result = new List<DriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady && drive.TotalSize > 0) result.Add(drive);
            }
            catch (IOException)
            {
                // disque qui disparaît pendant la lecture
            }
        }
        return result;
    }

    public static double FreePercent(DriveInfo drive) => drive.TotalSize > 0 ? 100.0 * drive.AvailableFreeSpace / drive.TotalSize : 100;

    private static void Check()
    {
        if (!SettingsStore.Current.Health.DiskAlert) return;
        foreach (var disk in Disks())
        {
            double free = FreePercent(disk);
            if (free < LowDiskPercent && Warned.Add(disk.Name))
            {
                Log.Info($"Santé : disque {disk.Name} sous {LowDiskPercent} % d'espace libre");
                LowDisk?.Invoke(disk);
            }
            else if (free > ClearDiskPercent)
            {
                Warned.Remove(disk.Name);
            }
        }
    }
}
