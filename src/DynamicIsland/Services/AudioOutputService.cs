using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

/// <summary>Une sortie audio active (haut-parleurs, casque…) : identifiant Windows et nom affiché.</summary>
public sealed record AudioOutput(string Id, string Name, bool IsDefault);

/// <summary>
/// Sorties audio (7D, automatisations) : liste des sorties actives et changement de la sortie par défaut, pour les trois rôles
/// (multimédia, console, communications), comme le fait le panneau Son de Windows. Rien n'est changé sans une règle activée.
/// </summary>
public static class AudioOutputService
{
    private const int Active = 1;

    public static List<AudioOutput> List()
    {
        var outputs = new List<AudioOutput>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var current) == 0 && current != null)
            {
                current.GetId(out defaultId);
                Marshal.ReleaseComObject(current);
            }
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, Active, out var pointer) != 0 || pointer == IntPtr.Zero) return outputs;
            var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            devices.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0 || device == null) continue;
                if (device.GetId(out var id) == 0 && id != null)
                    outputs.Add(new AudioOutput(id, FriendlyName(device) ?? id, id == defaultId));
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Error("Sorties audio : liste", ex);
        }
        return outputs.OrderByDescending(o => o.IsDefault).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Met cette sortie par défaut ; faux si elle n'existe plus (casque débranché) ou si Windows refuse.</summary>
    public static bool SetDefault(string id)
    {
        if (!List().Any(o => o.Id == id)) return false;
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            bool ok = true;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                ok &= policy.SetDefaultEndpoint(id, role) == 0;
            Marshal.ReleaseComObject(policy);
            return ok;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Error("Sorties audio : changement", ex);
            return false;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    private static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store == null) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 }; // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out var value) != 0) return null;
            var name = value.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.value) : null;
            PropVariantClear(ref value);
            return name;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }
}
