using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Icône de Dynamic Island dans la zone de notification (donc dans le dock quand on l'héberge) :
/// clic = paramètres, clic droit = menu. Sa fenêtre cachée « DynamicIsland.Messages » reçoit aussi la
/// demande d'une 2e instance de l'app (qui ouvre alors les paramètres au lieu de se lancer).
/// </summary>
public sealed class AppTrayIcon : IDisposable
{
    public const string MessageWindowTitle = "DynamicIsland.Messages";
    public static readonly uint OpenSettingsMessage = DockNative.RegisterWindowMessage("DynamicIsland.OpenSettings");

    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 0x42; // WM_APP + 0x42
    private const uint NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, WM_CONTEXTMENU = 0x7B;
    private const int WM_COPYDATA = 0x004A;
    /// <summary>Marque des messages WM_COPYDATA « importer ce thème » (« IMP »).</summary>
    private static readonly IntPtr ImportData = new(0x494D50);

    private readonly HwndSource _window;
    private readonly uint _taskbarCreated = DockNative.RegisterWindowMessage("TaskbarCreated");
    private IntPtr _icon;
    private bool _added;
    private readonly bool _safeMode;

    /// <param name="safeMode">Mode sans échec : info-bulle qui le dit et « Redémarrer normalement » dans le menu.</param>
    public AppTrayIcon(bool safeMode = false)
    {
        _safeMode = safeMode;
        _window = new HwndSource(new HwndSourceParameters(MessageWindowTitle)
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, jamais affichée (mais reçoit les diffusions)
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        });
        _window.AddHook(WndProc);
        _icon = LoadAppIcon();
        Add();
    }

    public event Action? OpenSettingsRequested;
    public event Action? QuitRequested;
    /// <summary>« Redémarrer normalement » (mode sans échec).</summary>
    public event Action? RestartRequested;
    /// <summary>Une 2e instance a été lancée avec un fichier de thème (double-clic dessus).</summary>
    public event Action<string>? ImportRequested;

    /// <summary>Dans la 2e instance : envoie le chemin du thème à l'instance qui tourne.</summary>
    public static void SendImport(IntPtr window, string path)
    {
        var text = Marshal.StringToHGlobalUni(path);
        try
        {
            var data = new COPYDATASTRUCT { dwData = ImportData, cbData = (path.Length + 1) * 2, lpData = text };
            SendMessageTimeout(window, WM_COPYDATA, IntPtr.Zero, ref data, 0x2 /* SMTO_ABORTIFHUNG */, 5000, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    private void Add()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _safeMode ? $"{AppInfo.Name} — {Loc.T("safe.short")}" : AppInfo.Name;
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added) return;
        data.uVersion = 4; // NOTIFYICON_VERSION_4 : NIN_SELECT / WM_CONTEXTMENU
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated)
        {
            // Explorer a redémarré ou on vient d'héberger la zone de notification : on se réenregistre.
            Add();
        }
        else if (msg == OpenSettingsMessage)
        {
            OpenSettingsRequested?.Invoke();
            handled = true;
        }
        else if (msg == WM_COPYDATA)
        {
            var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (data.dwData == ImportData && data.cbData is > 2 and < 65536)
            {
                var path = Marshal.PtrToStringUni(data.lpData, data.cbData / 2 - 1);
                // Pas pendant le SendMessage de l'autre instance : on ouvre l'aperçu juste après.
                _window.Dispatcher.BeginInvoke(() => ImportRequested?.Invoke(path));
                handled = true;
                return new IntPtr(1);
            }
        }
        else if (msg == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    OpenSettingsRequested?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var settings = new MenuItem { Header = Loc.T("tray.settings") };
        settings.Click += (_, _) => OpenSettingsRequested?.Invoke();
        var quit = new MenuItem { Header = Loc.F("tray.quit", AppInfo.Name) };
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(settings);
        if (!_safeMode)
        {
            var taskbar = new MenuItem { Header = Dock.TaskbarSwitch.Label };
            taskbar.Click += (_, _) => Dock.TaskbarSwitch.Toggle();
            menu.Items.Add(taskbar);
        }
        if (_safeMode)
        {
            var restart = new MenuItem { Header = Loc.T("safe.restart") };
            restart.Click += (_, _) => RestartRequested?.Invoke();
            menu.Items.Add(restart);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        SetForegroundWindow(_window.Handle); // sinon le menu ne se ferme pas en cliquant ailleurs
        menu.IsOpen = true;
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>Icône de l'exe à la taille des petites icônes du système (16 px à 100 %).</summary>
    private static IntPtr LoadAppIcon()
    {
        int size = GetSystemMetrics(49); // SM_CXSMICON
        var icons = new IntPtr[1];
        var ids = new uint[1];
        return Environment.ProcessPath is { } exe && PrivateExtractIcons(exe, 0, size, size, icons, ids, 1, 0) > 0
            ? icons[0]
            : IntPtr.Zero;
    }

    public void Dispose()
    {
        // Toujours tenter la suppression : l'icône peut exister chez Explorer même si un ajout a échoué.
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT data, uint flags, uint timeout, out IntPtr result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
