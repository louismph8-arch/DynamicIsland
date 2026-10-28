using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Services.Health;

/// <summary>
/// Température de la carte graphique NVIDIA (6E) par NVML, la bibliothèque que le pilote NVIDIA installe dans System32 :
/// sans droits administrateur, sans rien installer, sans pilote de bas niveau (rien que l'anti-triche des jeux bloque).
/// Pas de carte NVIDIA (ou pilote trop ancien) : <see cref="Available"/> reste faux, rien ne s'affiche.
/// </summary>
public sealed class NvidiaGpu : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;

    private readonly IntPtr _library;
    private readonly IntPtr _device;
    private readonly GetTemperatureDelegate? _temperature;
    private readonly ShutdownDelegate? _shutdown;

    public bool Available { get; }
    public string Name { get; } = "";

    public NvidiaGpu()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out _library)) return;
            var init = Export<InitDelegate>("nvmlInit_v2");
            if (init() != Success) return;
            _shutdown = Export<ShutdownDelegate>("nvmlShutdown");
            if (Export<GetCountDelegate>("nvmlDeviceGetCount_v2")(out uint count) != Success || count == 0) return;
            if (Export<GetHandleDelegate>("nvmlDeviceGetHandleByIndex_v2")(0, out _device) != Success) return;
            _temperature = Export<GetTemperatureDelegate>("nvmlDeviceGetTemperature");
            var name = new byte[96];
            if (Export<GetNameDelegate>("nvmlDeviceGetName")(_device, name, (uint)name.Length) == Success)
                Name = Encoding.ASCII.GetString(name).TrimEnd('\0');
            Available = true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or BadImageFormatException)
        {
            Log.Error("Santé : NVML", ex);
        }
    }

    /// <summary>Température du GPU en °C, null si elle n'est pas lisible.</summary>
    public int? Temperature() =>
        Available && _temperature!(_device, TemperatureGpu, out uint celsius) == Success && celsius is > 0 and < 150 ? (int)celsius : null;

    public void Dispose()
    {
        if (Available) _shutdown?.Invoke();
        // nvml.dll reste chargée : la décharger pendant que le pilote lui parle est inutile et risqué.
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCountDelegate(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHandleDelegate(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetTemperatureDelegate(IntPtr device, int sensor, out uint celsius);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetNameDelegate(IntPtr device, byte[] name, uint length);
}
