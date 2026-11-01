using System.IO;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// Corsair : SDK officiel d'iCUE (CUESDK v4). Corsair fournit la bibliothèque iCUESDK.x64_2019.dll avec son SDK : Opaline ne
/// l'embarque pas et ne la télécharge pas, elle la cherche (à côté de l'exe, dans %APPDATA%\DynamicIsland, ou dans le dossier
/// d'iCUE) et la charge par NativeLibrary. iCUE doit tourner, avec le SDK autorisé dans ses réglages. Connexion asynchrone
/// (CorsairConnect + rappel d'état), appareils (CorsairGetDevices), LED de chacun (CorsairGetLedPositions), même couleur sur
/// toutes (CorsairSetLedColors) ; contrôle exclusif demandé seulement si iCUE répond « pas de contrôle ». CorsairDisconnect à
/// l'arrêt : iCUE reprend ses effets.
/// </summary>
internal sealed class CorsairIcue : IRgbBackend
{
    public const string DllName = "iCUESDK.x64_2019.dll";
    private const int StringSize = 128, DeviceCountMax = 64, LedCountMax = 512;
    private const int ErrorSuccess = 0, ErrorNoControl = 2;
    private const int StateConnected = 6, StateConnectionRefused = 4;
    private const int AccessExclusiveLighting = 1;
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(3);

    private IntPtr _library;
    private ConnectDelegate? _connect;
    private GetDevicesDelegate? _getDevices;
    private GetLedPositionsDelegate? _getLeds;
    private SetLedColorsDelegate? _setColors;
    private RequestControlDelegate? _requestControl;
    private DisconnectDelegate? _disconnect;
    private StateChangedHandler? _handler; // gardé en vie : iCUE l'appelle depuis son propre fil
    private readonly ManualResetEventSlim _stateChanged = new();
    private volatile int _sessionState;
    private bool _connected;
    private readonly List<(string Id, uint[] Leds)> _devices = [];

    public string Name => "Corsair iCUE";
    public RgbBrandState State { get; private set; }
    public bool IsConnected => _connected;

    /// <summary>Dossiers où chercher le kit, dans l'ordre (la DLL n'est jamais copiée ni téléchargée par Opaline).</summary>
    internal static IEnumerable<string> SearchFolders()
    {
        yield return AppContext.BaseDirectory;
        yield return AppPaths.DataFolder;
        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var corsair = Path.Combine(Environment.GetFolderPath(programs), "Corsair");
            if (!Directory.Exists(corsair)) continue;
            yield return corsair;
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(corsair).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in children)
            {
                yield return child;
                // iCUE 4 / 5 : « Corsair iCUE5 Software », parfois avec un sous-dossier.
                IEnumerable<string> grandChildren;
                try
                {
                    grandChildren = Directory.EnumerateDirectories(child).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var grandChild in grandChildren) yield return grandChild;
            }
        }
    }

    /// <summary>Chemin du kit iCUE, null s'il est introuvable. Seulement cherché : rien n'est chargé.</summary>
    public static string? FindDll()
    {
        foreach (var folder in SearchFolders())
        {
            var path = Path.Combine(folder, DllName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static bool IsIcueRunning() => RgbHttp.AnyProcess("iCUE");

    public void Start()
    {
        if (_connected) return;
        if (!IsIcueRunning())
        {
            State = RgbBrandState.NotRunning;
            return;
        }
        try
        {
            if (_library == IntPtr.Zero)
            {
                if (FindDll() is not { } path)
                {
                    State = RgbBrandState.Missing;
                    return;
                }
                _library = NativeLibrary.Load(path);
                _connect = Export<ConnectDelegate>("CorsairConnect");
                _getDevices = Export<GetDevicesDelegate>("CorsairGetDevices");
                _getLeds = Export<GetLedPositionsDelegate>("CorsairGetLedPositions");
                _setColors = Export<SetLedColorsDelegate>("CorsairSetLedColors");
                _requestControl = Export<RequestControlDelegate>("CorsairRequestControl");
                _disconnect = Export<DisconnectDelegate>("CorsairDisconnect");
                _handler = OnStateChanged;
            }
            _stateChanged.Reset();
            _sessionState = 0;
            if (_connect!(_handler!, IntPtr.Zero) != ErrorSuccess)
            {
                State = RgbBrandState.Refused;
                return;
            }
            var deadline = DateTime.UtcNow + ConnectWait;
            while (_sessionState != StateConnected && _sessionState != StateConnectionRefused && DateTime.UtcNow < deadline)
            {
                _stateChanged.Wait(TimeSpan.FromMilliseconds(200));
                _stateChanged.Reset();
            }
            if (_sessionState != StateConnected)
            {
                // Refusé = SDK coupé dans les réglages d'iCUE ; sinon iCUE ne répond pas.
                _disconnect!();
                State = RgbBrandState.Refused;
                Log.Info($"RVB : iCUE refuse la connexion (état {_sessionState})");
                return;
            }
            ReadDevices();
            _connected = true;
            State = RgbBrandState.Connected;
            Log.Info($"RVB : iCUE connecté, {_devices.Count} appareil(s)");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            State = RgbBrandState.Missing;
            Log.Error("RVB : kit iCUE", ex);
        }
    }

    private void OnStateChanged(IntPtr context, IntPtr eventData)
    {
        try
        {
            if (eventData != IntPtr.Zero) _sessionState = Marshal.ReadInt32(eventData);
            _stateChanged.Set();
            if (_sessionState != StateConnected && _connected) _connected = false; // iCUE fermé : nouvel essai plus tard
        }
        catch
        {
            // Un rappel natif ne doit jamais laisser passer d'exception.
        }
    }

    private void ReadDevices()
    {
        _devices.Clear();
        var filter = new DeviceFilter { DeviceTypeMask = unchecked((int)0xFFFFFFFF) };
        var infos = new DeviceInfo[DeviceCountMax];
        if (_getDevices!(ref filter, DeviceCountMax, infos, out int count) != ErrorSuccess) return;
        for (int i = 0; i < Math.Min(count, DeviceCountMax); i++)
        {
            var id = infos[i].Id;
            var positions = new LedPosition[LedCountMax];
            if (_getLeds!(id, LedCountMax, positions, out int leds) != ErrorSuccess || leds <= 0) continue;
            _devices.Add((id, [.. positions.Take(Math.Min(leds, LedCountMax)).Select(p => p.Id)]));
        }
    }

    public bool SetColor(byte red, byte green, byte blue)
    {
        if (!_connected)
        {
            if (State == RgbBrandState.Connected) State = RgbBrandState.Refused;
            return false;
        }
        foreach (var (id, leds) in _devices)
        {
            var colors = leds.Select(led => new LedColor { Id = led, R = red, G = green, B = blue, A = 255 }).ToArray();
            int result = _setColors!(id, colors.Length, colors);
            if (result == ErrorNoControl && _requestControl!(id, AccessExclusiveLighting) == ErrorSuccess)
                result = _setColors(id, colors.Length, colors);
            if (result != ErrorSuccess) Log.Info($"RVB : iCUE refuse la couleur (erreur {result})");
        }
        return true;
    }

    public bool Heartbeat()
    {
        if (_connected) return true;
        if (State == RgbBrandState.Connected) State = RgbBrandState.Refused; // perdu entre-temps (rappel d'iCUE)
        return false;
    }

    public void Stop()
    {
        bool was = _connected;
        _connected = false;
        _devices.Clear();
        if (State == RgbBrandState.Connected) State = RgbBrandState.Off;
        if (!was) return;
        _disconnect?.Invoke();
        Log.Info("RVB : éclairage rendu à iCUE");
        // La bibliothèque reste chargée : iCUE peut encore appeler le rappel d'état.
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    // ------------------------------------------------------------------ CUESDK v4 (iCUESDK.h)

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceFilter
    {
        public int DeviceTypeMask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DeviceInfo
    {
        public int Type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = StringSize)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = StringSize)] public string Serial;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = StringSize)] public string Model;
        public int LedCount;
        public int ChannelCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LedPosition
    {
        public uint Id;
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LedColor
    {
        public uint Id;
        public byte R, G, B, A;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StateChangedHandler(IntPtr context, IntPtr eventData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConnectDelegate(StateChangedHandler handler, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDevicesDelegate(ref DeviceFilter filter, int sizeMax, [Out] DeviceInfo[] devices, out int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int GetLedPositionsDelegate([MarshalAs(UnmanagedType.LPStr)] string deviceId, int sizeMax, [Out] LedPosition[] positions, out int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int SetLedColorsDelegate([MarshalAs(UnmanagedType.LPStr)] string deviceId, int size, [In] LedColor[] colors);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int RequestControlDelegate([MarshalAs(UnmanagedType.LPStr)] string deviceId, int accessLevel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DisconnectDelegate();
}
