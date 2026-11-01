using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>
/// Poignée de la colonne auto-masquée de Stage Manager : petite pilule verticale (couleur d'accent voilée) collée au bord gauche
/// d'un écran, au milieu de la colonne. Sert surtout quand un autre écran est collé à gauche : la souris y passe sans jamais
/// s'arrêter au bord. Fenêtre « layered » outil, toujours au premier plan, jamais active (comme l'île), absente d'Alt+Tab ; seule sa
/// petite zone (alpha 1) capte la souris. Le survol est relevé par <see cref="StageManager"/> (même minuterie que le bord) ; le clic
/// arrive par <see cref="Clicked"/>.
/// </summary>
internal sealed class StageHandleWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly bool _snapshot;
    private readonly SquircleBorder _pill;

    /// <summary>Clic sur la poignée : la colonne sort tout de suite.</summary>
    public event Action? Clicked;

    public StageHandleWindow(bool snapshot = false)
    {
        _snapshot = snapshot;
        Title = "Dynamic Island — Stage Manager (poignée)";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = !snapshot;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Width = StageShape.HandleHitWidth;
        Height = StageShape.HandleHitHeight;
        _pill = new SquircleBorder
        {
            Width = StageShape.HandleWidth,
            Height = StageShape.HandleHeight,
            CornerRadius = Corners.Pill,
            BorderThickness = Borders.Hairline,
            BorderBrush = Paint.ActiveBorder,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(StageShape.HandleInset, 0, 0, 0),
            IsHitTestVisible = false,
        };
        RefreshColor();
        Content = new Grid { Background = Paint.HitTarget, Cursor = Cursors.Hand, Children = { _pill } };
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Clicked?.Invoke();
        };
    }

    /// <summary>Couleur d'accent actuelle (profil, fond d'écran), voilée.</summary>
    public void RefreshColor() =>
        _pill.Background = Paint.Frozen(Swatch.WithAlpha(AppearanceService.AccentMid, StageShape.HandleAlpha));

    /// <summary>Montre la poignée à cette place (pixels de l'écran) ou la cache.</summary>
    public void Place(bool visible, RECT zone)
    {
        if (_snapshot) return;
        if (!visible)
        {
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible)
        {
            Show();
            RefreshColor();
        }
        if (GetWindowRect(_hwnd, out var r) && r.Left == zone.Left && r.Top == zone.Top) return;
        // Position au pixel près ; la taille suit l'échelle de l'écran (WPF la refait si l'écran change d'échelle).
        SetWindowPos(_hwnd, HWND_TOPMOST, zone.Left, zone.Top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Rendu --snapshot : la poignée seule, hors écran.</summary>
    internal static async Task RenderSnapshotAsync(string path)
    {
        var window = new StageHandleWindow(snapshot: true) { Left = -30000, Top = 0 };
        window.Show();
        window.UpdateLayout();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }
}
