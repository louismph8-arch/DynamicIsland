using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

/// <summary>
/// Volume principal et vumètre de la sortie audio par défaut (Core Audio). La sortie est rouverte au
/// plus toutes les 3 s : on suit un changement de casque/enceintes sans coût quand on lit souvent.
/// </summary>
public sealed class VolumeService
{
    private const int ClsCtxAll = 0x17;
    private static readonly TimeSpan ReopenAfter = TimeSpan.FromSeconds(3);

    private readonly EDataFlow _flow;
    private readonly ERole _role;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private IAudioMeterInformation? _meter;
    private DateTime _openedAt = DateTime.MinValue;

    /// <summary>Dernier changement de volume fait par l'app (pour ne pas afficher notre propre changement).</summary>
    public DateTime LastOwnChange { get; private set; } = DateTime.MinValue;

    public VolumeService() : this(EDataFlow.Render, ERole.Multimedia)
    {
    }

    private VolumeService(EDataFlow flow, ERole role)
    {
        _flow = flow;
        _role = role;
    }

    /// <summary>Micro par défaut de Windows (6D, télécommande) : même interface, sourdine = micro coupé.</summary>
    public static VolumeService Microphone() => new(EDataFlow.Capture, ERole.Console);

    /// <summary>
    /// Tous les micros branchés sont-ils coupés ? (null s'il n'y en a aucun). Couper TOUS les micros (6D-1b) : une appli
    /// comme Discord peut en utiliser un autre que celui « par défaut ».
    /// </summary>
    public static bool? AllMicrophonesMuted()
    {
        bool? result = null;
        ForEachMicrophone(endpoint =>
        {
            if (endpoint.GetMute(out bool muted) == 0) result = (result ?? true) && muted;
        });
        return result;
    }

    public static void MuteAllMicrophones(bool mute)
    {
        var context = Guid.Empty;
        ForEachMicrophone(endpoint => endpoint.SetMute(mute, ref context));
    }

    private static void ForEachMicrophone(Action<IAudioEndpointVolume> action)
    {
        const int Active = 1;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.EnumAudioEndpoints(EDataFlow.Capture, Active, out var pointer) != 0 || pointer == IntPtr.Zero) return;
            var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            devices.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0 || device == null) continue;
                if (Activate<IAudioEndpointVolume>(device) is { } endpoint)
                {
                    action(endpoint);
                    Marshal.ReleaseComObject(endpoint);
                }
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Error("Micros : liste des périphériques", ex);
        }
    }

    /// <summary>Volume (0 à 1) et sourdine de la sortie par défaut.</summary>
    public bool TryRead(out double level, out bool muted)
    {
        level = 0;
        muted = false;
        EnsureOpen();
        if (_endpoint == null || _endpoint.GetMasterVolumeLevelScalar(out float scalar) != 0) return false;
        _endpoint.GetMute(out muted);
        level = scalar;
        return true;
    }

    /// <summary>Niveau crête du son en cours de lecture (0 à 1), 0 si indisponible.</summary>
    public float Peak()
    {
        EnsureOpen();
        return _meter != null && _meter.GetPeakValue(out float peak) == 0 ? peak : 0;
    }

    public void SetLevel(double level)
    {
        EnsureOpen();
        var endpoint = _endpoint;
        if (endpoint == null) return;

        LastOwnChange = DateTime.UtcNow;
        var context = Guid.Empty;
        endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref context);
        if (level > 0 && endpoint.GetMute(out bool muted) == 0 && muted)
            endpoint.SetMute(false, ref context);
    }

    /// <summary>Son coupé ↔ remis (clic sur l'icône du volume de la barre de menus).</summary>
    public void ToggleMute()
    {
        EnsureOpen();
        var endpoint = _endpoint;
        if (endpoint == null || endpoint.GetMute(out bool muted) != 0) return;
        LastOwnChange = DateTime.UtcNow;
        var context = Guid.Empty;
        endpoint.SetMute(!muted, ref context);
    }

    private void EnsureOpen()
    {
        if (_endpoint != null && DateTime.UtcNow - _openedAt < ReopenAfter) return;
        _openedAt = DateTime.UtcNow;
        Release();
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (_enumerator.GetDefaultAudioEndpoint(_flow, _role, out var device) != 0 || device == null)
                return;
            _endpoint = Activate<IAudioEndpointVolume>(device);
            _meter = Activate<IAudioMeterInformation>(device);
            Marshal.ReleaseComObject(device);
        }
        catch (Exception ex)
        {
            Log.Error("Volume : sortie audio", ex);
        }
    }

    private static T? Activate<T>(IMMDevice device) where T : class
    {
        var iid = typeof(T).GUID;
        return device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) == 0 ? instance as T : null;
    }

    private void Release()
    {
        if (_endpoint != null) Marshal.ReleaseComObject(_endpoint);
        if (_meter != null) Marshal.ReleaseComObject(_meter);
        _endpoint = null;
        _meter = null;
    }
}
