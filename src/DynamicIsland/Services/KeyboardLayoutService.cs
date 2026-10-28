using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Settings;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Langue de saisie (6B) de la fenêtre au premier plan : le dock et l'île ne prennent jamais le focus, c'est donc celle de
/// l'appli où l'on tape. Lue toutes les 300 ms seulement quand elle est affichée (zone d'état ou annonce dans l'île).
/// Changer de langue = demander à la fenêtre au premier plan de passer à la disposition voulue (WM_INPUTLANGCHANGEREQUEST,
/// ce que fait Windows avec Win+Espace) : aucune touche simulée.
/// </summary>
public static class KeyboardLayoutService
{
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

    /// <summary>Disposition : handle Windows, abréviation (« FR »), nom (« Français (Canada) — Canadien français »).</summary>
    public sealed record Layout(IntPtr Handle, string Short, string Name);

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static readonly Dictionary<IntPtr, Layout> Names = [];
    private static IntPtr _window;
    private static bool _started;

    public static Layout? Current { get; private set; }

    /// <summary>Langue affichée changée (thread UI). Argument : vrai si elle a changé dans la même fenêtre (Win+Espace, clic).</summary>
    public static event Action<bool>? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Timer.Tick += (_, _) => Check();
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        Timer.Stop();
        SettingsStore.Changed -= Apply;
        _started = false;
    }

    private static void Apply()
    {
        var dock = SettingsStore.Current.Dock;
        bool wanted = !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Status)
                      && (dock.ShowsStatus(StatusItem.Keyboard) || dock.KeyboardInIsland);
        if (wanted == Timer.IsEnabled) return;
        if (wanted)
        {
            Check();
            Timer.Start();
        }
        else
        {
            Timer.Stop();
        }
    }

    /// <summary>Lecture immédiate (rendus, premier affichage).</summary>
    public static Layout? Read()
    {
        Check();
        return Current;
    }

    private static void Check()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        uint thread = Interop.DockNative.GetWindowThreadProcessId(window, out uint process);
        // Nos propres fenêtres (paramètres, palette, menus) : on garde la langue de l'appli où l'on tapait.
        if (process == Environment.ProcessId && Current != null) return;
        var handle = GetKeyboardLayout(thread);
        if (handle == IntPtr.Zero || handle == Current?.Handle)
        {
            _window = window;
            return;
        }
        bool sameWindow = window == _window && Current != null;
        _window = window;
        Current = Describe(handle);
        Changed?.Invoke(sameWindow);
    }

    /// <summary>Dispositions installées, dans l'ordre de Windows.</summary>
    public static List<Layout> Installed()
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0) return [];
        var handles = new IntPtr[count];
        count = GetKeyboardLayoutList(count, handles);
        return handles.Take(count).Select(Describe).ToList();
    }

    /// <summary>Passe la fenêtre au premier plan à la disposition suivante (clic sur la cellule).</summary>
    public static void Next()
    {
        var layouts = Installed();
        if (layouts.Count < 2) return;
        int index = layouts.FindIndex(l => l.Handle == Current?.Handle);
        Switch(layouts[(index + 1) % layouts.Count]);
    }

    public static void Switch(Layout layout)
    {
        // La dernière fenêtre d'une autre appli vue au premier plan (un menu du dock a pu passer devant entre-temps).
        var window = _window != IntPtr.Zero && Interop.DockNative.IsWindow(_window) ? _window : GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        Interop.DockNative.PostMessage(window, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout.Handle);
        // La lecture suivante (≤ 300 ms) voit le changement : même fenêtre ⇒ annoncé comme un changement voulu.
    }

    public static Layout Describe(IntPtr handle)
    {
        if (Names.TryGetValue(handle, out var known)) return known;
        long value = handle.ToInt64();
        int language = (int)(value & 0xFFFF);
        int device = (int)((value >> 16) & 0xFFFF);
        string shortName = "??", name = $"0x{value:X8}";
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            shortName = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            name = culture.NativeName.Length > 0 ? char.ToUpper(culture.NativeName[0], culture) + culture.NativeName[1..] : name;
        }
        catch (CultureNotFoundException)
        {
            // Langue inconnue de .NET : on garde le code.
        }
        if (LayoutName(language, device) is { } layout && !name.Contains(layout, StringComparison.OrdinalIgnoreCase))
            name += " — " + layout;
        var result = new Layout(handle, shortName, name);
        Names[handle] = result;
        return result;
    }

    /// <summary>Nom de la disposition dans la langue de Windows (« Canadien français », « États-Unis - International »).</summary>
    private static string? LayoutName(int language, int device)
    {
        try
        {
            using var layouts = Registry.LocalMachine.OpenSubKey(LayoutsKey);
            if (layouts == null) return null;
            string? klid = null;
            if ((device & 0xF000) == 0xF000)
            {
                // Variante (« Layout Id ») : on cherche la disposition qui porte cet identifiant.
                int id = device & 0x0FFF;
                foreach (var name in layouts.GetSubKeyNames())
                {
                    using var key = layouts.OpenSubKey(name);
                    if (key?.GetValue("Layout Id") is string text && int.TryParse(text, NumberStyles.HexNumber, null, out int found) && found == id)
                    {
                        klid = name;
                        break;
                    }
                }
            }
            else
            {
                klid = (device == 0 ? language : device).ToString("X8");
            }
            if (klid == null) return null;
            using var layoutKey = layouts.OpenSubKey(klid);
            if (layoutKey?.GetValue("Layout Display Name") is string display && display.StartsWith('@'))
            {
                var text = new StringBuilder(256);
                if (SHLoadIndirectString(display, text, text.Capacity, IntPtr.Zero) == 0) return text.ToString();
            }
            return layoutKey?.GetValue("Layout Text") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[]? layouts);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);
}
