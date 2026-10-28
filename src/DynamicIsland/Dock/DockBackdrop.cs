using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Vrai flou acrylique de Windows 11 derrière la barre du dock : une fenêtre rectangulaire (non
/// transparente) placée juste sous la fenêtre du dock, avec le fond système DWM « transient » (acrylique).
///
/// Astuce : DWM rend ce fond opaque quand la fenêtre n'est pas active ; on répond donc toujours
/// « active » au message WM_NCACTIVATE. Vérifié sur la machine de l'utilisateur (prototype oct. 2026).
/// Ne marche que pour un rectangle : pas pour la pilule de l'île ni le dock flottant arrondi.
/// </summary>
public sealed class DockBackdrop
{
    private const int WM_NCACTIVATE = 0x0086;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // acrylique
    private const int DWMWCP_DONOTROUND = 1;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly Window _window;
    private IntPtr _hwnd;

    public DockBackdrop()
    {
        _window = new Window
        {
            Title = "Dynamic Island — Flou du dock",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Background = Brushes.Transparent,
            Left = -30000,
            Width = 10,
            Height = 10,
        };
        _window.SourceInitialized += (_, _) => Setup();
    }

    /// <summary>Windows 11 22H2+ avec les effets de transparence activés.</summary>
    public static bool IsSupported =>
        Environment.OSVersion.Version.Build >= 22621 &&
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is not 0;

    /// <summary>Place le flou (pixels écran) juste sous <paramref name="above"/> (la fenêtre du dock).</summary>
    public void Place(int x, int y, int width, int height, IntPtr above)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _window.Show();
            _hwnd = new WindowInteropHelper(_window).Handle;
        }
        SetWindowPos(_hwnd, above, x, y, Math.Max(width, 1), Math.Max(height, 1), SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero) ShowWindow(_hwnd, SW_HIDE);
    }

    public void Close() => _window.Close();

    private void Setup()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source?.AddHook(WndProc);

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int dark = 1, backdrop = DWMSBT_TRANSIENTWINDOW, corners = DWMWCP_DONOTROUND, noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int)); // pas de liseré DWM
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
    }

    /// <summary>Toujours « active » pour DWM : le flou ne retombe pas en couleur unie.</summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCACTIVATE) return IntPtr.Zero;
        handled = true;
        return DefWindowProc(hwnd, msg, new IntPtr(1), lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
