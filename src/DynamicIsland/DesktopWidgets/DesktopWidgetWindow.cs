using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.DesktopWidgets;

/// <summary>
/// Fenêtre d'un widget du bureau : transparente, sans barre des tâches, jamais active (un clic ne vole pas le focus).
/// Déplacement (glisser n'importe où hors des boutons) et taille (poignée en bas à droite) passent par la boucle de
/// Windows, qui gère les écrans et leurs échelles ; on aimante à la grille dans WM_MOVING / WM_SIZING et on garde
/// les proportions (le contenu est dans un Viewbox). Rangée juste au-dessus du bureau (voir le service).
/// </summary>
public sealed class DesktopWidgetWindow : Window
{
    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const int WM_NCLBUTTONDOWN = 0x00A1, HTCAPTION = 2, HTBOTTOMRIGHT = 17;
    private const int WM_SIZING = 0x0214, WM_MOVING = 0x0216, WM_EXITSIZEMOVE = 0x0232;

    private readonly Border _grip;
    private IntPtr _hwnd;

    public DesktopWidgetWindow(DesktopWidget widget)
    {
        Widget = widget;
        Title = "Dynamic Island — Widget";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;

        var root = new Grid { Background = Paint.HitTarget }; // capte la souris partout (glisser)
        root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = widget });
        _grip = new Border
        {
            Width = Sizes.SmallIconButton,
            Height = Sizes.SmallIconButton,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Paint.HitTarget,
            Cursor = Cursors.SizeNWSE,
            Opacity = 0,
            ToolTip = Loc.T("widget.resize"),
            Child = new TextBlock
            {
                Text = Icons.Resize,
                FontFamily = FontStack.Icons,
                FontSize = IconSize.S,
                Foreground = Paint.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _grip.MouseLeftButtonDown += (_, e) =>
        {
            if (!Locked) StartSystemLoop(HTBOTTOMRIGHT);
            e.Handled = true;
        };
        root.Children.Add(_grip);
        root.MouseEnter += (_, _) => _grip.Opacity = Locked ? 0 : 1;
        root.MouseLeave += (_, _) => _grip.Opacity = 0;
        Content = root;

        // Les boutons du widget marquent l'événement comme traité : seul un clic ailleurs fait glisser.
        MouseLeftButtonDown += (_, e) =>
        {
            if (!Locked) StartSystemLoop(HTCAPTION);
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(_hwnd, GWL_EXSTYLE, (GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
    }

    public DesktopWidget Widget { get; }
    public DesktopWidgetKind Kind => Widget.Kind;
    public IntPtr Handle => _hwnd;

    /// <summary>Fin d'un déplacement ou d'un redimensionnement (pour enregistrer la place).</summary>
    public event Action<DesktopWidgetWindow>? Placed;

    private static bool Locked => SettingsStore.Current.DesktopWidgets.Locked;

    /// <summary>Échelle actuelle (1 = taille de base du widget).</summary>
    public double Scale => Width / Widget.BaseSize.Width;

    public void Place(double left, double top, double scale)
    {
        Left = left;
        Top = top;
        Width = Widget.BaseSize.Width * scale;
        Height = Widget.BaseSize.Height * scale;
    }

    /// <summary>Net (bureau au premier plan) ou estompé (une appli est devant).</summary>
    public void SetFaded(bool faded) =>
        BeginAnimation(OpacityProperty, new DoubleAnimation(faded ? Desk.FadedOpacity : 1, Desk.Fade) { EasingFunction = new CubicEase() });

    private void StartSystemLoop(int hitTest)
    {
        if (_hwnd == IntPtr.Zero) return;
        Mouse.Capture(null);
        SendMessage(_hwnd, WM_NCLBUTTONDOWN, new IntPtr(hitTest), IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            case WM_MOVING:
                SnapMove(lParam);
                handled = true;
                return new IntPtr(1);
            case WM_SIZING:
                SnapSize(lParam);
                handled = true;
                return new IntPtr(1);
            case WM_EXITSIZEMOVE:
                Placed?.Invoke(this);
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Coin haut-gauche aimanté à la grille (en pixels de l'écran, taille inchangée).</summary>
    private void SnapMove(IntPtr lParam)
    {
        var rect = Marshal.PtrToStructure<RECT>(lParam);
        double grid = Desk.Grid * VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        rect.Left = (int)(Math.Round(rect.Left / grid) * grid);
        rect.Top = (int)(Math.Round(rect.Top / grid) * grid);
        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        Marshal.StructureToPtr(rect, lParam, false);
    }

    /// <summary>Largeur aimantée à la grille, hauteur dans les proportions du widget, échelle bornée.</summary>
    private void SnapSize(IntPtr lParam)
    {
        var rect = Marshal.PtrToStructure<RECT>(lParam);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double grid = Desk.Grid * dpi, baseWidth = Widget.BaseSize.Width * dpi;
        double width = Math.Clamp(Math.Round((rect.Right - rect.Left) / grid) * grid, baseWidth * Desk.MinScale, baseWidth * Desk.MaxScale);
        rect.Right = rect.Left + (int)width;
        rect.Bottom = rect.Top + (int)Math.Round(width * Widget.BaseSize.Height / Widget.BaseSize.Width);
        Marshal.StructureToPtr(rect, lParam, false);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
