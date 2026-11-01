using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DynamicIsland.Services.NightLight;

/// <summary>Un écran dont on lit / écrit la rampe gamma (le vrai : <see cref="GdiGammaDevice"/> ; un faux pour --self-test).</summary>
public interface IGammaDevice
{
    /// <summary>Nom GDI de l'écran (« \\.\DISPLAY2 »).</summary>
    string Name { get; }
    bool TryRead(out ushort[] ramp);
    bool TryWrite(ushort[] ramp);
}

/// <summary>
/// Rampes d'origine des écrans (7F), dans <c>%APPDATA%\DynamicIsland\veilleuse.txt</c> : une ligne « écran=rampe en base64 »,
/// écrite AVANT le premier changement de cet écran et jamais écrasée (c'est le vrai état d'avant). Le fichier n'existe que tant
/// qu'une rampe est changée : <see cref="RestoreAll"/> remet chaque écran présent et l'oublie ; un écran débranché garde sa
/// ligne pour plus tard. Le chemin est un paramètre : --self-test travaille dans un fichier à lui avec de faux écrans.
/// </summary>
public sealed class GammaStore(string filePath)
{
    public string FilePath { get; } = filePath;

    public bool HasState => File.Exists(FilePath);

    public Dictionary<string, ushort[]> Load()
    {
        var all = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath)) return all;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                // Le nom de l'écran ne contient pas « = » ; la rampe (base64) peut en finir par « == » : on coupe au premier.
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                if (GammaRamp.Decode(line[(split + 1)..]) is { } ramp) all[line[..split]] = ramp;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Veilleuse : lecture des rampes d'origine", ex);
        }
        return all;
    }

    private void Save(Dictionary<string, ushort[]> all)
    {
        if (all.Count == 0)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllLines(temp, all.Select(pair => $"{pair.Key}={GammaRamp.Encode(pair.Value)}"), new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>
    /// Rampe d'origine de cet écran : celle déjà gardée, sinon lue maintenant et ÉCRITE dans le fichier avant de rendre la main.
    /// Null si elle ne peut pas être gardée (fichier impossible à écrire) : on ne touche alors pas à l'écran.
    /// </summary>
    public ushort[]? EnsureOriginal(IGammaDevice device)
    {
        var all = Load();
        if (all.TryGetValue(device.Name, out var saved)) return GammaRamp.OriginalOrIdentity(saved);
        var original = GammaRamp.OriginalOrIdentity(device.TryRead(out var read) ? read : null);
        try
        {
            all[device.Name] = original;
            Save(all);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Veilleuse : rampe d'origine impossible à garder, l'écran n'est pas touché", ex);
            return null;
        }
        return original;
    }

    /// <summary>
    /// Remet la rampe d'origine de chaque écran présent puis l'oublie (une rampe abîmée = rampe neutre). Écran absent ou qui
    /// refuse : ligne gardée pour plus tard. Vrai si plus rien n'est à remettre.
    /// </summary>
    public bool RestoreAll(IEnumerable<IGammaDevice> devices)
    {
        var all = Load();
        if (all.Count == 0) return true;
        foreach (var device in devices)
        {
            if (!all.TryGetValue(device.Name, out var original)) continue;
            if (device.TryWrite(GammaRamp.OriginalOrIdentity(original))) all.Remove(device.Name);
        }
        try
        {
            Save(all);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Veilleuse : fichier d'état", ex);
        }
        return all.Count == 0;
    }
}

/// <summary>Rampe gamma d'un écran par GDI (API documentée : CreateDC, Get/SetDeviceGammaRamp).</summary>
public sealed class GdiGammaDevice(string name) : IGammaDevice
{
    public string Name { get; } = name;

    public bool TryRead(out ushort[] ramp)
    {
        ramp = new ushort[GammaRamp.Length];
        var dc = CreateDC(Name, Name, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            return GetDeviceGammaRamp(dc, ramp);
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    public bool TryWrite(ushort[] ramp)
    {
        if (!GammaRamp.IsSane(ramp)) return false; // jamais une rampe qui noircirait l'écran
        var dc = CreateDC(Name, Name, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            return SetDeviceGammaRamp(dc, ramp);
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    /// <summary>Écrans actifs (noms GDI).</summary>
    public static List<IGammaDevice> All() => ScreenInfo.All().Select(m => (IGammaDevice)new GdiGammaDevice(m.Device)).ToList();

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] lpRamp);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] lpRamp);
}

/// <summary>Ce que Windows dit des écrans, en lecture seule : HDR actif (la rampe n'y marche pas), veilleuse de Windows.</summary>
public static class DisplayFacts
{
    /// <summary>
    /// Noms GDI des écrans en « couleur avancée » (HDR, ou gestion automatique des couleurs de Windows 11) : la rampe gamma
    /// peut y être ignorée, on le signale. Vide si Windows ne répond pas.
    /// </summary>
    public static HashSet<string> HdrScreens()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return result;
            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;
            for (int i = 0; i < pathCount; i++)
            {
                int at = i * PathInfoSize;
                uint sourceLow = BitConverter.ToUInt32(paths, at), sourceId = BitConverter.ToUInt32(paths, at + 8);
                int sourceHigh = BitConverter.ToInt32(paths, at + 4);
                uint targetLow = BitConverter.ToUInt32(paths, at + 20), targetId = BitConverter.ToUInt32(paths, at + 28);
                int targetHigh = BitConverter.ToInt32(paths, at + 24);

                var color = new AdvancedColorInfo
                {
                    Header = new InfoHeader { Type = GET_ADVANCED_COLOR_INFO, Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(), AdapterLow = targetLow, AdapterHigh = targetHigh, Id = targetId },
                };
                if (DisplayConfigGetDeviceInfo(ref color) != 0 || (color.Value & 0x2) == 0) continue; // bit 1 = couleur avancée active
                var source = new SourceName
                {
                    Header = new InfoHeader { Type = GET_SOURCE_NAME, Size = (uint)Marshal.SizeOf<SourceName>(), AdapterLow = sourceLow, AdapterHigh = sourceHigh, Id = sourceId },
                };
                if (DisplayConfigGetDeviceInfo(ref source) == 0) result.Add(source.GdiName);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            Log.Error("Veilleuse : HDR des écrans", ex);
        }
        return result;
    }

    /// <summary>
    /// Veilleuse de Windows active ? Lu (jamais écrit) dans l'état gardé par Windows ; null si on ne sait pas. Le format n'est pas
    /// documenté : seulement une indication pour prévenir que les deux filtres s'ajoutent.
    /// </summary>
    public static bool? WindowsNightLightOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
                + @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate");
            if (key?.GetValue("Data") is not byte[] data || data.Length < 19) return null;
            return data[18] == 0x15;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint GET_SOURCE_NAME = 1;
    private const uint GET_ADVANCED_COLOR_INFO = 9;
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;

    [StructLayout(LayoutKind.Sequential)]
    private struct InfoHeader
    {
        public uint Type, Size, AdapterLow;
        public int AdapterHigh;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public InfoHeader Header;
        public uint Value, ColorEncoding, BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public InfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] byte[] pathArray,
        ref uint numModeInfoArrayElements, [Out] byte[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);
}
