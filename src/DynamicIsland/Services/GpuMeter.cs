using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Utilisation des moteurs 3D du GPU via les compteurs « GPU Engine » de Windows (PDH), comme le Gestionnaire
/// des tâches : somme des instances choisies, plafonnée à 100 %. La première mesure sert de référence (null).
/// </summary>
public sealed class GpuMeter : IDisposable
{
    private readonly string _instances;
    private readonly string _logContext;
    private IntPtr _query;
    private IntPtr _counter;
    private bool _unavailable;

    /// <param name="instances">Instances du compteur : « *engtype_3D » (tout le PC) ou « pid_1234_*engtype_3D » (une appli).</param>
    /// <param name="logContext">Libellé des erreurs dans le journal.</param>
    public GpuMeter(string instances, string logContext)
    {
        _instances = instances;
        _logContext = logContext;
    }

    public double? Sample()
    {
        if (_unavailable) return null;
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0 ||
                    PdhAddEnglishCounter(_query, $@"\GPU Engine({_instances})\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
                {
                    _unavailable = true;
                    return null;
                }
                PdhCollectQueryData(_query); // la première mesure sert de référence
                return null;
            }

            if (PdhCollectQueryData(_query) != 0) return null;
            uint size = 0;
            uint status = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return 0;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, out count, buffer) != 0) return null;
                int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                double total = 0;
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                    if (item.Value.CStatus == 0) total += item.Value.DoubleValue;
                }
                return Math.Clamp(total, 0, 100);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Error(_logContext, ex);
            _unavailable = true;
            return null;
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    // --- Interop ---
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double DoubleValue; // union (aligné sur 8 octets)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name;
        public PDH_FMT_COUNTERVALUE Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
