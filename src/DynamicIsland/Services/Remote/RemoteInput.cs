using System.Runtime.InteropServices;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Pavé tactile, clavier et raccourcis de la télécommande (6D-1c) : de vrais mouvements de souris et touches envoyés à
/// Windows (SendInput), comme une souris ou un clavier sans fil. Seulement pour un téléphone en « Contrôle complet », et
/// jamais pendant le mode Gaming. Windows refuse ces entrées aux fenêtres lancées en administrateur et à l'écran de
/// verrouillage.
/// </summary>
public static class RemoteInput
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010,
        MiddleDown = 0x0020, MiddleUp = 0x0040, Wheel = 0x0800, HorizontalWheel = 0x1000;
    private const uint KeyExtended = 0x0001, KeyUp = 0x0002, KeyUnicode = 0x0004;
    private const ushort Alt = 0x12, Control = 0x11, Shift = 0x10, Windows = 0x5B;

    /// <summary>Touches spéciales de la page (nom → touche virtuelle, touche « étendue » ?).</summary>
    private static readonly Dictionary<string, (ushort Key, bool Extended)> Keys = new()
    {
        ["enter"] = (0x0D, false),
        ["backspace"] = (0x08, false),
        ["tab"] = (0x09, false),
        ["escape"] = (0x1B, false),
        ["space"] = (0x20, false),
        ["left"] = (0x25, true),
        ["up"] = (0x26, true),
        ["right"] = (0x27, true),
        ["down"] = (0x28, true),
        ["delete"] = (0x2E, true),
        ["home"] = (0x24, true),
        ["end"] = (0x23, true),
        ["pageup"] = (0x21, true),
        ["pagedown"] = (0x22, true),
    };

    /// <summary>Raccourcis proposés sur le téléphone (touches appuyées dans l'ordre, relâchées à l'envers).</summary>
    public static readonly IReadOnlyDictionary<string, ushort[]> Shortcuts = new Dictionary<string, ushort[]>
    {
        ["alttab"] = [Alt, 0x09],
        ["taskview"] = [Windows, 0x09],
        ["desktop"] = [Windows, 0x44],
        ["fullscreen"] = [0x7A],
        ["space"] = [0x20],
        ["snip"] = [Windows, Shift, 0x53],
        ["close"] = [Alt, 0x73],
        ["copy"] = [Control, 0x43],
        ["paste"] = [Control, 0x56],
        ["undo"] = [Control, 0x5A],
        ["selectall"] = [Control, 0x41],
        ["zoomin"] = [Control, 0xBB],
        ["zoomout"] = [Control, 0xBD],
    };

    public static void Move(int dx, int dy)
    {
        dx = Math.Clamp(dx, -800, 800);
        dy = Math.Clamp(dy, -800, 800);
        if (dx != 0 || dy != 0) Send(Mouse(MouseMove, dx, dy));
    }

    /// <summary>Bouton « left » / « right » / « middle », action « click » / « double » / « down » / « up ».</summary>
    public static bool Button(string button, string how)
    {
        (uint down, uint up) = button switch
        {
            "left" => (LeftDown, LeftUp),
            "right" => (RightDown, RightUp),
            "middle" => (MiddleDown, MiddleUp),
            _ => (0u, 0u),
        };
        if (down == 0) return false;
        switch (how)
        {
            case "click":
                Send(Mouse(down), Mouse(up));
                return true;
            case "double":
                Send(Mouse(down), Mouse(up), Mouse(down), Mouse(up));
                return true;
            case "down":
                Send(Mouse(down));
                return true;
            case "up":
                Send(Mouse(up));
                return true;
            default:
                return false;
        }
    }

    /// <summary>Molette : <paramref name="vertical"/> et <paramref name="horizontal"/> en unités Windows (120 = un cran).</summary>
    public static void Scroll(int vertical, int horizontal)
    {
        vertical = Math.Clamp(vertical, -1200, 1200);
        horizontal = Math.Clamp(horizontal, -1200, 1200);
        if (vertical != 0) Send(Mouse(Wheel, data: vertical));
        if (horizontal != 0) Send(Mouse(HorizontalWheel, data: horizontal));
    }

    /// <summary>Texte tapé sur le téléphone (lettres accentuées et émojis compris), 500 caractères au plus d'un coup.</summary>
    public static void Text(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text.Length > 500 ? text[..500] : text)
        {
            if (c == '\n')
            {
                inputs.Add(Key(0x0D, 0));
                inputs.Add(Key(0x0D, KeyUp));
            }
            else if (c != '\r')
            {
                inputs.Add(Unicode(c, 0));
                inputs.Add(Unicode(c, KeyUp));
            }
        }
        Send([.. inputs]);
    }

    public static bool PressKey(string name)
    {
        if (!Keys.TryGetValue(name, out var key)) return false;
        uint extended = key.Extended ? KeyExtended : 0;
        Send(Key(key.Key, extended), Key(key.Key, extended | KeyUp));
        return true;
    }

    public static bool Shortcut(string id)
    {
        if (!Shortcuts.TryGetValue(id, out var keys)) return false;
        var inputs = keys.Select(k => Key(k, IsExtended(k) ? KeyExtended : 0))
            .Concat(keys.Reverse().Select(k => Key(k, (IsExtended(k) ? KeyExtended : 0) | KeyUp)));
        Send([.. inputs]); // un seul envoi : aucune touche ne reste enfoncée
        return true;
    }

    /// <summary>Toucher l'aperçu de l'écran : la souris va à cet endroit (fractions 0–1 de l'écran) et clique.</summary>
    public static void ClickAt(System.Drawing.Rectangle screen, double x, double y)
    {
        SetCursorPos(screen.Left + (int)Math.Round(Math.Clamp(x, 0, 1) * (screen.Width - 1)),
                     screen.Top + (int)Math.Round(Math.Clamp(y, 0, 1) * (screen.Height - 1)));
        Button("left", "click");
    }

    private static bool IsExtended(ushort key) => key is Windows or 0x2E or >= 0x21 and <= 0x28;

    private static INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MOUSEINPUT { Dx = dx, Dy = dy, MouseData = data, Flags = flags } },
    };

    private static INPUT Key(ushort key, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KEYBDINPUT { Key = key, Flags = flags } },
    };

    private static INPUT Unicode(char c, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KEYBDINPUT { Scan = c, Flags = KeyUnicode | flags } },
    };

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Log.Info($"Télécommande : entrée refusée par Windows (erreur {Marshal.GetLastWin32Error()})");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy, MouseData;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
