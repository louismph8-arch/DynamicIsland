using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>Une mesure : CPU, RAM, GPU (en %) et débit réseau (octets/s). null = pas encore disponible.</summary>
public sealed record SystemStats(double? Cpu, double? Ram, double? Gpu, double? Download, double? Upload);

/// <summary>
/// Statistiques système sans dépendance : CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx), GPU (compteur
/// « GPU Engine » de Windows via PDH, si disponible) et réseau (octets des cartes actives). Les débits et
/// pourcentages sont calculés entre deux appels à <see cref="Sample"/> : on n'échantillonne que quand
/// l'onglet Widgets est affiché.
/// </summary>
public sealed class SystemStatsService : IDisposable
{
    private (ulong Idle, ulong Kernel, ulong User)? _lastCpu;
    private (long Received, long Sent, DateTime At)? _lastNet;
    private readonly GpuMeter _gpu = new("*engtype_3D", "Widgets : GPU");

    public SystemStats Sample()
    {
        var (download, upload) = SampleNetwork();
        return new SystemStats(SampleCpu(), SampleRam(), _gpu.Sample(), download, upload);
    }

    private double? SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var now = (Idle: idle, Kernel: kernel, User: user);
        double? usage = null;
        if (_lastCpu is { } last)
        {
            double total = (now.Kernel - last.Kernel) + (now.User - last.User); // le temps noyau inclut l'inactivité
            double busy = total - (now.Idle - last.Idle);
            if (total > 0) usage = Math.Clamp(busy / total * 100, 0, 100);
        }
        _lastCpu = now;
        return usage;
    }

    private static double? SampleRam()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : null;
    }

    private (double? Download, double? Upload) SampleNetwork()
    {
        try
        {
            long received = 0, sent = 0;
            foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (card.OperationalStatus != OperationalStatus.Up ||
                    card.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = card.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            var now = DateTime.UtcNow;
            (double?, double?) result = (null, null);
            if (_lastNet is { } last && now > last.At)
            {
                double seconds = (now - last.At).TotalSeconds;
                result = (Math.Max(0, (received - last.Received) / seconds), Math.Max(0, (sent - last.Sent) / seconds));
            }
            _lastNet = (received, sent, now);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Widgets : réseau", ex);
            return (null, null);
        }
    }

    /// <summary>Remet à zéro les références : la prochaine mesure (après une pause) repartira proprement.</summary>
    public void Reset()
    {
        _lastCpu = null;
        _lastNet = null;
    }

    public void Dispose()
    {
        _gpu.Dispose();
    }

    /// <summary>Formate un débit : « 850 Ko/s », « 12,4 Mo/s ».</summary>
    public static string FormatRate(double? bytesPerSecond) => bytesPerSecond switch
    {
        null => "…",
        < 1024 * 1024 => Loc.F("rate.kb", Math.Round(bytesPerSecond.Value / 1024)),
        _ => Loc.F("rate.mb", Math.Round(bytesPerSecond.Value / (1024 * 1024), 1)),
    };

    // --- Interop ---
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
