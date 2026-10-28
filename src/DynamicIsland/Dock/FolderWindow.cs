using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Dossier du dock ouvert : grille d'applis au-dessus de son icône (clic = ouvrir l'appli, clic droit =
/// sortir du dossier). Le nom se modifie directement dans le titre : la fenêtre est donc activable, et
/// elle se ferme dès qu'on clique ailleurs.
/// </summary>
internal sealed class FolderWindow : Window
{
    private const int MaxColumns = DockShape.FolderColumns;
    private const double ShadowMargin = Shadows.WindowMargin;
    private static readonly Brush HitBrush = Paint.HitTarget;
    private static readonly Brush HoverBrush = Paint.Control;

    private readonly TextBox _title;
    private readonly SquircleBorder _card;
    private readonly UniformGrid _grid = new() { Margin = new Thickness(Space.S5, Space.S1, Space.S5, Space.S6) };
    private DockItem? _folder;
    private Point _anchor;
    private ScreenInfo.Monitor _screen;
    private bool _below;
    private bool _menuOpen;
    private SquircleBorder? _pressed;

    public FolderWindow()
    {
        Title = Loc.T("folder.window");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;

        _title = new TextBox
        {
            Margin = new Thickness(Space.S9, Space.S7, Space.S9, Space.S3),
            FontFamily = FontStack.Display,
            FontSize = TypeRamp.Title3,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = Borders.None,
            Foreground = Paint.White,
            CaretBrush = Paint.White,
            MaxLength = 40,
            ToolTip = Loc.T("folder.rename.tooltip"),
        };
        _title.SetResourceReference(TextBoxBase.SelectionBrushProperty, AppearanceService.AccentBrushKey);
        _title.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitTitle();
            _grid.Focusable = true;
            _grid.Focus(); // quitte le champ
            e.Handled = true;
        };
        _title.LostKeyboardFocus += (_, _) => CommitTitle();

        var panel = new StackPanel();
        panel.Children.Add(_title);
        panel.Children.Add(_grid);
        _card = new SquircleBorder
        {
            Margin = new Thickness(ShadowMargin),
            MinWidth = DockShape.FolderMinWidth,
            BorderBrush = Paint.PopupBorder,
            BorderThickness = Borders.Hairline,
            Child = panel,
            Effect = Effects.Window,
        };
        _card.SetResourceReference(Border.BackgroundProperty, AppearanceService.IslandBrushKey);
        Content = _card;

        SourceInitialized += (_, _) =>
        {
            // Ni barre des tâches ni Alt+Tab, mais activable (pour taper le nom du dossier).
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, (GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) =>
        {
            if (!_menuOpen) Hide(); // clic ailleurs = fermeture
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
        };
    }

    /// <summary>Dossier affiché, ou null si la fenêtre est fermée.</summary>
    public DockItem? Folder => IsVisible ? _folder : null;

    /// <summary>Clic sur une appli du dossier.</summary>
    public event Action<DockItem>? AppChosen;
    /// <summary>Le dossier a été renommé.</summary>
    public event Action? Renamed;
    /// <summary>Appli à sortir du dossier : true = la remettre dans le dock, false = la retirer du dock.</summary>
    public event Action<DockItem, bool>? RemoveRequested;

    /// <param name="anchor">Milieu du bord intérieur de l'icône du dossier, en unités WPF de l'écran.</param>
    /// <param name="screen">Écran du dock : la grille n'en sort pas.</param>
    /// <param name="below">Dock en haut de l'écran : la grille s'ouvre sous l'icône.</param>
    public void ShowFor(DockItem folder, Point anchor, ScreenInfo.Monitor screen, bool below, bool rename = false)
    {
        _folder = folder;
        _anchor = anchor;
        _screen = screen;
        _below = below;
        Rebuild();
        if (!IsVisible) Show();
        UpdateLayout();
        Place();
        Activate();
        if (!rename) return;
        _title.Focus();
        _title.SelectAll();
    }

    /// <summary>Contenu du dossier modifié : on redessine la grille.</summary>
    public void Refresh()
    {
        if (!IsVisible || _folder == null) return;
        Rebuild();
        UpdateLayout();
        Place();
    }

    private void Rebuild()
    {
        var children = _folder?.Children ?? [];
        _title.Text = _folder?.Name ?? "";
        _card.CornerRadius = new CornerRadius(AppearanceService.Radius(Radius.Folder)); // suit « Arrondi des coins »
        _grid.Children.Clear();
        _grid.Columns = Math.Clamp(children.Count, 2, MaxColumns);
        foreach (var child in children) _grid.Children.Add(CreateTile(child));
    }

    /// <summary>Au-dessus de l'icône (en dessous si le dock est en haut), centrée sur elle, sans sortir de l'écran du dock.</summary>
    private void Place()
    {
        var bounds = _screen.Bounds;
        double scale = _screen.Scale;
        double minLeft = bounds.Left / scale - ShadowMargin;
        double maxLeft = bounds.Right / scale - ActualWidth + ShadowMargin;
        Left = Math.Clamp(_anchor.X - ActualWidth / 2, minLeft, Math.Max(minLeft, maxLeft));
        Top = _below
            ? _anchor.Y + DockShape.FolderAnchorGap - ShadowMargin // haut de la carte juste sous l'icône
            : _anchor.Y - DockShape.FolderAnchorGap - ActualHeight + ShadowMargin; // bas de la carte juste au-dessus de l'icône
    }

    private SquircleBorder CreateTile(DockItem child)
    {
        var image = new Image { Source = child.Icon, Width = DockShape.FolderTileIcon, Height = DockShape.FolderTileIcon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = child.Name,
            FontSize = TypeRamp.Caption,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32, // deux lignes
            Margin = new Thickness(0, Space.S3, 0, 0),
        };
        var content = new StackPanel();
        content.Children.Add(image);
        content.Children.Add(label);

        var tile = new SquircleBorder
        {
            Width = DockShape.FolderTileWidth,
            Padding = new Thickness(Space.S3, Space.S4, Space.S3, Space.S3),
            CornerRadius = Corners.XL,
            Background = HitBrush,
            Child = content,
            Cursor = Cursors.Hand,
        };
        tile.MouseEnter += (_, _) => tile.Background = HoverBrush;
        tile.MouseLeave += (_, _) => tile.Background = HitBrush;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = tile;
            e.Handled = true;
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressed != tile) return;
            _pressed = null;
            e.Handled = true;
            Hide();
            AppChosen?.Invoke(child);
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            ShowTileMenu(tile, child);
            e.Handled = true;
        };
        return tile;
    }

    private void ShowTileMenu(SquircleBorder tile, DockItem child)
    {
        var menu = new ContextMenu { PlacementTarget = tile, Placement = PlacementMode.MousePoint };
        menu.Items.Add(Entry(Loc.T("menu.open"), () =>
        {
            Hide();
            AppChosen?.Invoke(child);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("folder.take_out"), () => RemoveRequested?.Invoke(child, true)));
        menu.Items.Add(Entry(Loc.T("dock.remove"), () => RemoveRequested?.Invoke(child, false)));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (IsVisible && !IsActive) Activate(); // garde la fermeture au clic ailleurs
        };
        _menuOpen = true;
        menu.IsOpen = true;
    }

    private void CommitTitle()
    {
        if (_folder == null) return;
        var name = _title.Text.Trim();
        if (name.Length == 0)
        {
            _title.Text = _folder.Name; // nom vide : on garde l'ancien
            return;
        }
        if (name == _folder.Name) return;
        _folder.Rename(name);
        Renamed?.Invoke();
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }

    /// <summary>Mode --snapshot : rendu du dossier hors écran.</summary>
    internal static async Task RenderSnapshotAsync(DockItem folder, string path)
    {
        var window = new FolderWindow { ShowActivated = false };
        window._folder = folder;
        window.Rebuild();
        window.Left = -30000;
        window.Top = 0;
        window.Show();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }
}
