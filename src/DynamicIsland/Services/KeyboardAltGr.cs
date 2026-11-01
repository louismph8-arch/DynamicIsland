using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Ce que tape une touche avec AltGr (= Ctrl+Alt sur les claviers qui ont AltGr) dans une disposition de clavier donnée, lu
/// directement dans la table de la disposition (KBDFR.DLL, KBDCA.DLL…, fonction KbdLayerDescriptor : ce que lit Windows) —
/// en lecture seule : la disposition n'est ni chargée pour l'utilisateur, ni ajoutée à sa liste, ni activée. Sert à vérifier
/// qu'un raccourci Ctrl+Alt+lettre ne vole pas un caractère (@, #, €, [, {…) aux claviers français (<c>--self-test</c>).
/// </summary>
internal static class KeyboardAltGr
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private const int KBDSHIFT = 1, KBDCTRL = 2, KBDALT = 4;
    private const byte SHFT_INVALID = 0x0F;
    private const ushort WCH_NONE = 0xF000;
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;

    /// <summary>Résultat pour une disposition : trouvée ou non, et les caractères de AltGr+touche / AltGr+Maj+touche (vides = rien).</summary>
    internal sealed record Result(string Klid, string? File, bool Read, string AltGr, string AltGrShift)
    {
        public bool Free => Read && AltGr.Length == 0 && AltGrShift.Length == 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LayerDescriptor();

    /// <summary>Caractères produits par AltGr + <paramref name="vk"/> (et avec Maj) dans la disposition <paramref name="klid"/> (« 0000040C »).</summary>
    internal static Result Probe(string klid, byte vk)
    {
        string? file;
        using (var key = Registry.LocalMachine.OpenSubKey($@"{LayoutsKey}\{klid}"))
            file = key?.GetValue("Layout File") as string;
        if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(['\\', '/', ':']) >= 0) return new Result(klid, file, false, "", "");

        var module = LoadLibraryEx(file, IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (module == IntPtr.Zero) return new Result(klid, file, false, "", "");
        try
        {
            var proc = GetProcAddress(module, "KbdLayerDescriptor");
            if (proc == IntPtr.Zero) return new Result(klid, file, false, "", "");
            var tables = Marshal.GetDelegateForFunctionPointer<LayerDescriptor>(proc)();
            if (tables == IntPtr.Zero) return new Result(klid, file, false, "", "");
            return new Result(klid, file, true, Char(tables, vk, KBDCTRL | KBDALT), Char(tables, vk, KBDCTRL | KBDALT | KBDSHIFT));
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>
    /// Caractère de la touche pour cette combinaison de modificateurs (KBDTABLES : pCharModifiers → colonne, puis les tables
    /// VK_TO_WCHARS). Vide si rien (colonne absente, WCH_NONE, caractère de contrôle). Touche morte ou ligature = « produit quelque chose ».
    /// </summary>
    private static string Char(IntPtr tables, byte vk, int bits)
    {
        var modifiers = Marshal.ReadIntPtr(tables, 0);
        var wcharTables = Marshal.ReadIntPtr(tables, IntPtr.Size);
        if (modifiers == IntPtr.Zero || wcharTables == IntPtr.Zero) return "";
        int maxBits = (ushort)Marshal.ReadInt16(modifiers, IntPtr.Size);
        if (bits > maxBits) return "";
        byte column = Marshal.ReadByte(modifiers, IntPtr.Size + 2 + bits);
        if (column == SHFT_INVALID) return "";

        int entrySize = IntPtr.Size * 2; // { PVK_TO_WCHARS1 pVkToWchars; BYTE nModifications; BYTE cbSize; } + alignement
        for (int t = 0; t < 32; t++)
        {
            var entry = wcharTables + t * entrySize;
            var rows = Marshal.ReadIntPtr(entry, 0);
            if (rows == IntPtr.Zero) break;
            int count = Marshal.ReadByte(entry, IntPtr.Size);
            int size = Marshal.ReadByte(entry, IntPtr.Size + 1);
            if (size < 4) continue;
            for (int r = 0; r < 512; r++)
            {
                var row = rows + r * size;
                byte key = Marshal.ReadByte(row, 0);
                if (key == 0) break;
                if (key != vk) continue;
                if (column >= count) return "";
                var wch = (ushort)Marshal.ReadInt16(row, 2 + 2 * column);
                if (wch == WCH_NONE || wch < 0x20) return "";
                return wch is 0xF001 or 0xF002 ? "(touche morte)" : ((char)wch).ToString();
            }
        }
        return "";
    }

    /// <summary>Dispositions installées pour l'utilisateur (HKCU\Keyboard Layout\Preload), en lecture.</summary>
    internal static List<string> Installed()
    {
        var list = new List<string>();
        try
        {
            using var preload = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload");
            using var substitutes = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Substitutes");
            foreach (var name in preload?.GetValueNames() ?? [])
            {
                if (preload!.GetValue(name) is not string id) continue;
                if (substitutes?.GetValue(id) is string real) id = real;
                if (!list.Contains(id, StringComparer.OrdinalIgnoreCase)) list.Add(id.ToUpperInvariant());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Illisible : on teste seulement les dispositions de référence.
        }
        return list;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
}
