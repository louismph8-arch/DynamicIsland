using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// Éclairage Logitech par le kit d'éclairage (LED SDK) que G HUB installe et enregistre lui-même : rien à télécharger.
/// G HUB doit tourner, avec « Autoriser les jeux et applications à contrôler l'éclairage ». À l'arrêt
/// (<see cref="Stop"/>), G HUB reprend la main et remet ses propres effets.
/// Appelé depuis un seul fil à la fois (voir <see cref="RgbService"/>).
/// </summary>
public sealed class LogitechLighting : IDisposable
{
    /// <summary>Clé où G HUB indique son kit d'éclairage 64 bits.</summary>
    private const string ServerKey = @"CLSID\{a6519e67-7632-4375-afdf-caa889744403}\ServerBinary";
    private const int AllDevices = 7; // monochromes + RVB + RVB par touche

    private IntPtr _library;
    private InitWithNameDelegate? _init;
    private SetTargetDelegate? _target;
    private SetLightingDelegate? _set;
    private ShutdownDelegate? _shutdown;
    private bool _started;

    /// <summary>Chemin du kit d'éclairage de G HUB, null si G HUB n'est pas installé.</summary>
    public static string? SdkPath
    {
        get
        {
            using var key = Registry.ClassesRoot.OpenSubKey(ServerKey);
            return key?.GetValue(null) is string path && File.Exists(path) ? path : null;
        }
    }

    public bool IsRunning => _started;

    /// <summary>Prend la main sur l'éclairage ; faux si G HUB n'est pas installé ou ne répond pas (pas lancé).</summary>
    public bool Start()
    {
        if (_started) return true;
        try
        {
            if (_library == IntPtr.Zero)
            {
                if (SdkPath is not { } path) return false;
                _library = NativeLibrary.Load(path);
                _init = Export<InitWithNameDelegate>("LogiLedInitWithName");
                _target = Export<SetTargetDelegate>("LogiLedSetTargetDevice");
                _set = Export<SetLightingDelegate>("LogiLedSetLighting");
                _shutdown = Export<ShutdownDelegate>("LogiLedShutdown");
            }
            if (!_init!(AppInfo.Name)) return false; // G HUB pas lancé, ou éclairage par les applis interdit dans G HUB
            _target!(AllDevices);
            _started = true;
            Log.Info("RVB : G HUB connecté");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            Log.Error("RVB : kit d'éclairage de G HUB", ex);
            return false;
        }
    }

    /// <summary>Couleur de tous les appareils Logitech (en pourcentages, comme le veut le kit).</summary>
    public bool SetColor(byte red, byte green, byte blue) =>
        _started && _set!(Percent(red), Percent(green), Percent(blue));

    /// <summary>Rend l'éclairage à G HUB (ses propres effets reviennent).</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _shutdown?.Invoke();
        Log.Info("RVB : éclairage rendu à G HUB");
    }

    public void Dispose()
    {
        Stop();
        // La bibliothèque reste chargée : la décharger pendant que G HUB lui parle peut planter.
    }

    private static int Percent(byte value) => (int)Math.Round(value * 100 / 255.0);

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool InitWithNameDelegate([MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetTargetDelegate(int targetDevice);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetLightingDelegate(int redPercentage, int greenPercentage, int bluePercentage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownDelegate();
}
