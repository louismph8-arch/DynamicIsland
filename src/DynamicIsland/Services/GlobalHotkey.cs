using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Raccourcis clavier globaux : RegisterHotKey sur une fenêtre de messages invisible.
/// <list type="bullet">
/// <item>Palette (Ctrl+Espace par défaut) : libéré pendant le mode Gaming (le jeu garde toutes ses touches).</item>
/// <item>« Afficher le dock » (Ctrl+Alt+D) : TOUJOURS actif, surtout en jeu — la barre des tâches est masquée sur
/// tous les écrans, c'est le seul moyen d'atteindre ses applis pendant une partie.</item>
/// </list>
/// Les deux sont libérés pendant qu'on choisit un nouveau raccourci dans les paramètres.
/// </summary>
public static class GlobalHotkey
{
    private const int WM_HOTKEY = 0x0312;
    private const int PaletteId = 0x4449;
    private const int RevealId = 0x444A;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private static HwndSource? _window;
    private static string? _palette, _reveal; // raccourcis enregistrés
    private static bool _suspended;

    /// <summary>Le raccourci de la palette a été pressé (thread UI).</summary>
    public static event Action? Pressed;
    /// <summary>Le raccourci « Afficher le dock » a été pressé (thread UI).</summary>
    public static event Action? RevealPressed;
    /// <summary>Un raccourci a été (ou n'a pas pu être) enregistré.</summary>
    public static event Action? StateChanged;

    /// <summary>Message pour les paramètres quand Windows refuse le raccourci de la palette (null = tout va bien).</summary>
    public static string? Error { get; private set; }
    /// <summary>Idem pour le raccourci « Afficher le dock ».</summary>
    public static string? RevealError { get; private set; }

    public static void Start()
    {
        if (_window != null) return;
        _window = new HwndSource(new HwndSourceParameters("DynamicIsland.Hotkey") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _window.AddHook(WndProc);
        SettingsStore.Changed += Apply;
        GamingMode.Changed += Apply;
        Apply();
    }

    /// <summary>Réapplique les raccourcis (module de la palette arrêté après une erreur : son raccourci est libéré).</summary>
    public static void Refresh() => Apply();

    /// <summary>Paramètres : pendant qu'on tape un nouveau raccourci, les anciens ne doivent rien déclencher.</summary>
    public static void Suspend(bool suspended)
    {
        _suspended = suspended;
        Apply();
    }

    private static void Apply()
    {
        if (_window == null) return;
        var settings = SettingsStore.Current;
        _palette = Register(PaletteId, _palette,
            settings.Palette.Enabled && !_suspended && !GamingMode.IsActive && !ModuleGuard.IsDisabled(ModuleGuard.Palette) ? settings.Palette.Hotkey : null,
            "Palette", error => Error = error);
        _reveal = Register(RevealId, _reveal,
            settings.Dock.Enabled && !_suspended ? settings.Dock.RevealHotkey : null,
            "Afficher le dock", error => RevealError = error);
    }

    /// <returns>Le raccourci enregistré après l'appel (null si aucun).</returns>
    private static string? Register(int id, string? registered, string? wanted, string name, Action<string?> setError)
    {
        if (wanted == registered) return registered;
        if (registered != null) UnregisterHotKey(_window!.Handle, id);
        string? result = null;
        if (wanted != null)
        {
            if (!Hotkeys.TryParse(wanted, out var modifiers, out var key))
            {
                setError(Loc.T("hotkey.invalid"));
            }
            else if (RegisterHotKey(_window!.Handle, id, (uint)modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                result = wanted;
                setError(null);
            }
            else
            {
                setError(Loc.T("hotkey.taken"));
                Log.Info($"{name} : raccourci {wanted} refusé (erreur {Marshal.GetLastWin32Error()})");
            }
        }
        StateChanged?.Invoke();
        return result;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id != PaletteId && id != RevealId) return IntPtr.Zero;
        handled = true;
        try
        {
            if (id == PaletteId) Pressed?.Invoke();
            else RevealPressed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Raccourci clavier", ex);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>Raccourcis « Ctrl+Space » (format des réglages) ↔ touches WPF, et leur affichage en français.</summary>
public static class Hotkeys
{
    public const string Default = "Ctrl+Space";
    /// <summary>« Afficher le dock » : D comme Dock ; Ctrl+Alt ne se tape pas par hasard en jeu.</summary>
    public const string DefaultReveal = "Ctrl+Alt+D";

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        var parts = (text ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift" or "maj": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        return Enum.TryParse(parts[^1], true, out key) && key != Key.None && !IsModifier(key) && HasMainModifier(modifiers);
    }

    /// <summary>Il faut Ctrl, Alt ou Win (Maj seule ne suffit pas : on ne pourrait plus taper la lettre).</summary>
    public static bool HasMainModifier(ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static string Format(ModifierKeys modifiers, Key key) =>
        string.Join("+", Names(modifiers, "Ctrl", "Alt", "Shift", "Win").Append(key.ToString()));

    /// <summary>« Ctrl + Espace ».</summary>
    public static string Display(string? text) =>
        TryParse(text, out var modifiers, out var key)
            ? string.Join(" + ", Names(modifiers, "Ctrl", "Alt", Loc.T("key.shift"), "Win").Append(KeyName(key)))
            : Loc.T("hotkey.none");

    /// <summary>Pendant la saisie : « Ctrl + Maj + … ».</summary>
    public static string DisplayPartial(ModifierKeys modifiers) =>
        string.Join(" + ", Names(modifiers, "Ctrl", "Alt", Loc.T("key.shift"), "Win").Append("…"));

    private static IEnumerable<string> Names(ModifierKeys modifiers, string ctrl, string alt, string shift, string win)
    {
        if (modifiers.HasFlag(ModifierKeys.Control)) yield return ctrl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) yield return alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) yield return shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) yield return win;
    }

    private static string KeyName(Key key) => key switch
    {
        Key.Space => Loc.T("key.space"),
        Key.Return => Loc.T("key.enter"),
        Key.Back => Loc.T("key.backspace"),
        Key.Tab => "Tab",
        Key.Escape => Loc.T("key.escape"),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => Loc.F("key.numpad", (int)(key - Key.NumPad0)),
        _ => key.ToString(),
    };
}
