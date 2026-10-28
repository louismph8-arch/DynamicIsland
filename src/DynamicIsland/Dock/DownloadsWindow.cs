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
/// Pile Téléchargements ouverte (6B) : grille des derniers éléments au-dessus de la case du dock. Clic = ouvrir, glisser =
/// déposer le fichier ailleurs (copier / déplacer, au choix de la destination, comme depuis l'Explorateur), clic droit =
/// ouvrir ou afficher dans l'Explorateur. Activable comme un dossier du dock : se ferme dès qu'on clique ailleurs.
/// </summary>
internal sealed class DownloadsWindow : Window
{
    private const double ShadowMargin = Shadows.WindowMargin;
    private static readonly Brush HitBrush = Paint.HitTarget;
    private static readonly Brush HoverBrush = Paint.Control;

    private readonly SquircleBorder _card;
    private readonly UniformGrid _grid = new() { Margin = new Thickness(Space.S5, Space.S1, Space.S5, Space.S3) };
    private readonly TextBlock _empty;
    private Point _anchor;
    private ScreenInfo.Monitor _screen;
    private bool _below;
    private bool _menuOpen;
    private bool _dragging;
    private (SquircleBorder Tile, Point Start)? _pressed;

    public DownloadsWindow()
    {
        Title = Loc.T("downloads.window");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;

        var title = new TextBlock
        {
            Text = Loc.T("folders.downloads"),
            Margin = new Thickness(Space.S9, Space.S7, Space.S9, Space.S3),
            FontFamily = FontStack.Display,
            FontSize = TypeRamp.Title3,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _empty = new TextBlock
        {
            Text = Loc.T("downloads.empty"),
            Foreground = Paint.TextSecondary,
            FontSize = TypeRamp.Footnote,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(Space.S9, Space.S4, Space.S9, Space.S6),
        };
        var open = new Button
        {
            Style = (Style)FindResource("SoftButton"),
            Content = Loc.T("status.downloads.open"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, Space.S2, 0, Space.S7),
            Padding = new Thickness(Space.S6, Space.S3, Space.S6, Space.S3),
        };
        open.Click += (_, _) =>
        {
            Hide();
            DownloadsService.Open(DownloadsService.Folder);
        };

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(_grid);
        panel.Children.Add(_empty);
        panel.Children.Add(open);
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
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, (GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) =>
        {
            if (!_menuOpen && !_dragging) Hide(); // clic ailleurs = fermeture
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
        };
        DownloadsService.Changed += () =>
        {
            if (IsVisible && !_dragging) Refresh();
        };
    }

    /// <param name="anchor">Milieu du bord intérieur de la case, en unités WPF de l'écran.</param>
    public void ShowFor(Point anchor, ScreenInfo.Monitor screen, bool below)
    {
        _anchor = anchor;
        _screen = screen;
        _below = below;
        Rebuild();
        if (!IsVisible) Show();
        UpdateLayout();
        Place();
        Activate();
    }

    private void Refresh()
    {
        Rebuild();
        UpdateLayout();
        Place();
    }

    private void Rebuild()
    {
        var entries = DownloadsService.Recent(DockShape.DownloadsShown);
        _card.CornerRadius = new CornerRadius(AppearanceService.Radius(Radius.Folder));
        _grid.Children.Clear();
        _grid.Columns = Math.Clamp(entries.Count, 2, DockShape.FolderColumns);
        foreach (var entry in entries) _grid.Children.Add(CreateTile(entry));
        _grid.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = entries.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Place()
    {
        var bounds = _screen.Bounds;
        double scale = _screen.Scale;
        double minLeft = bounds.Left / scale - ShadowMargin;
        double maxLeft = bounds.Right / scale - ActualWidth + ShadowMargin;
        Left = Math.Clamp(_anchor.X - ActualWidth / 2, minLeft, Math.Max(minLeft, maxLeft));
        Top = _below
            ? _anchor.Y + DockShape.FolderAnchorGap - ShadowMargin
            : _anchor.Y - DockShape.FolderAnchorGap - ActualHeight + ShadowMargin;
    }

    private SquircleBorder CreateTile(DownloadsService.Entry entry)
    {
        var image = new Image { Source = IconService.GetThumbnail(entry.Path), Width = DockShape.FolderTileIcon, Height = DockShape.FolderTileIcon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = entry.Name,
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
            ToolTip = $"{entry.Name}\n{entry.When.ToString("g", Loc.Culture)}",
        };
        tile.MouseEnter += (_, _) => tile.Background = HoverBrush;
        tile.MouseLeave += (_, _) => tile.Background = HitBrush;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = (tile, e.GetPosition(this));
            e.Handled = true;
        };
        tile.MouseMove += (_, e) =>
        {
            if (_pressed is not { } pressed || pressed.Tile != tile || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(this) - pressed.Start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _pressed = null;
            _dragging = true;
            try
            {
                // Comme depuis l'Explorateur : la destination choisit copier ou déplacer.
                DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { entry.Path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                _dragging = false;
            }
            if (IsVisible && !IsActive) Hide(); // déposé dans une autre appli
            else Refresh();
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressed?.Tile != tile) return;
            _pressed = null;
            e.Handled = true;
            Hide();
            DownloadsService.Open(entry.Path);
        };
        tile.MouseRightButtonUp += (_, e) =>
        {
            ShowTileMenu(tile, entry);
            e.Handled = true;
        };
        return tile;
    }

    private void ShowTileMenu(SquircleBorder tile, DownloadsService.Entry entry)
    {
        var menu = new ContextMenu { PlacementTarget = tile, Placement = PlacementMode.MousePoint };
        menu.Items.Add(Entry(Loc.T("menu.open"), () =>
        {
            Hide();
            DownloadsService.Open(entry.Path);
        }));
        menu.Items.Add(Entry(Loc.T("shelf.show_in_explorer"), () =>
        {
            Hide();
            DownloadsService.ShowInExplorer(entry.Path);
        }));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (IsVisible && !IsActive) Activate();
        };
        _menuOpen = true;
        menu.IsOpen = true;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }

    /// <summary>Mode --snapshot : rendu de la pile hors écran.</summary>
    internal static async Task RenderSnapshotAsync(string path)
    {
        var window = new DownloadsWindow { ShowActivated = false };
        window.Rebuild();
        window.Left = -30000;
        window.Top = 0;
        window.Show();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }
}
