using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Étagère à fichiers : aperçu + nom de chaque élément. Glisser une tuile (ou « Tout glisser ») vers un
/// dossier ou une appli les y <b>copie</b> (on n'autorise pas le déplacement : les originaux restent en
/// place). Double-clic = ouvrir, clic droit = menu, « × » = retirer de l'étagère (rien n'est supprimé).
/// </summary>
public partial class ShelfView : UserControl
{
    private const double TileWidth = Sizes.ShelfTile;
    private static readonly Brush HitBrush = Paint.HitTarget;
    private static readonly Brush HoverBrush = Paint.Control;

    private Point _pressPoint;
    private string[]? _pressPaths;
    private readonly HashSet<string> _shown = new(StringComparer.OrdinalIgnoreCase); // tuiles déjà vues (les nouvelles arrivent en rebondissant)
    private bool _flash;

    public ShelfView()
    {
        InitializeComponent();
        ShelfService.Changed += Rebuild;
        Settings.SettingsStore.Changed += ShowPhone;
        Services.Remote.RemoteOutbox.Changed += () => Dispatcher.BeginInvoke(ShowPhone);
        DraggingOut += dragging =>
        {
            // Pendant qu'on glisse une tuile : la zone du téléphone s'allume pour inviter à y déposer.
            if (PhoneDrop.Visibility == Visibility.Visible) Highlight(dragging);
        };
        Rebuild();
        ShowPhone();
    }

    /// <summary>Vrai pendant qu'on glisse des fichiers de l'étagère vers l'extérieur (l'île reste ouverte).</summary>
    public event Action<bool>? DraggingOut;

    /// <summary>Cadre « Dépose ici » pendant qu'on glisse des fichiers au-dessus de l'île.</summary>
    public void ShowDropHint(bool show) =>
        DropHint.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, Motion.Quick));

    private void Rebuild()
    {
        Tiles.Children.Clear();
        bool first = _shown.Count == 0 && !IsLoaded;
        foreach (var path in ShelfService.Items)
        {
            var tile = CreateTile(path);
            Tiles.Children.Add(tile);
            if (_shown.Add(path) && !first) PopIn(tile); // vient d'arriver (fichier déposé, reçu du téléphone…)
        }
        _shown.IntersectWith(ShelfService.Items);

        int count = ShelfService.Items.Count;
        EmptyPanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = count == 0 ? Visibility.Hidden : Visibility.Visible;
        CountText.Text = count == 1 ? Loc.T("shelf.one_item") : Loc.F("shelf.items", count);
    }

    private FrameworkElement CreateTile(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        if (name.Length == 0) name = path; // racine d'un lecteur

        var image = new Image { Source = IconService.GetThumbnail(path), Width = Sizes.ShelfThumb, Height = Sizes.ShelfThumb, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = name,
            FontSize = TypeRamp.Caption,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxHeight = 30, // deux lignes
            Margin = new Thickness(0, Space.S2, 0, 0),
        };
        var remove = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = Icons.Close,
            Width = Sizes.TinyButton,
            Height = Sizes.TinyButton,
            FontSize = IconSize.XS,
            Background = Paint.Badge,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -Space.S1, -Space.S1, 0),
            Opacity = 0,
        };
        remove.Click += (_, _) => ShelfService.Remove(path);

        var content = new StackPanel();
        content.Children.Add(image);
        content.Children.Add(label);
        var layers = new Grid();
        layers.Children.Add(content);
        layers.Children.Add(remove);

        var tile = new SquircleBorder
        {
            Width = TileWidth,
            Padding = new Thickness(Space.S3),
            Margin = new Thickness(0, 0, Space.S2, 0),
            CornerRadius = Corners.L,
            ClipContent = false, // le « × » déborde un peu dans le coin
            Background = HitBrush,
            Child = layers,
            Cursor = Cursors.Hand,
        };
        tile.MouseEnter += (_, _) =>
        {
            tile.Background = HoverBrush;
            remove.Opacity = 1;
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.Background = HitBrush;
            remove.Opacity = 0;
        };
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Open(path);
                e.Handled = true;
                return;
            }
            BeginPress(tile, e, [path]);
        };
        tile.MouseMove += (_, e) => TryStartDrag(tile, e);
        tile.MouseLeftButtonUp += (_, _) => EndPress(tile);
        tile.MouseRightButtonUp += (_, e) =>
        {
            ShowMenu(path);
            e.Handled = true;
        };
        return tile;
    }

    // ------------------------------------------------------------------ glisser vers l'extérieur

    private void BeginPress(UIElement source, MouseButtonEventArgs e, string[] paths)
    {
        _pressPoint = e.GetPosition(this);
        _pressPaths = paths;
        source.CaptureMouse();
        e.Handled = true;
    }

    private void EndPress(UIElement source)
    {
        _pressPaths = null;
        if (source.IsMouseCaptured) source.ReleaseMouseCapture();
    }

    private void TryStartDrag(UIElement source, MouseEventArgs e)
    {
        if (_pressPaths is not { Length: > 0 } paths || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _pressPoint.X) < 6 && Math.Abs(position.Y - _pressPoint.Y) < 6) return;

        EndPress(source);
        DraggingOut?.Invoke(true);
        try
        {
            // Copie (ou raccourci) seulement : glisser hors de l'étagère ne déplace jamais l'original.
            DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            Log.Error("Étagère : glisser", ex);
        }
        finally
        {
            DraggingOut?.Invoke(false);
            ShelfService.Prune();
        }
    }

    private void DragAll_MouseDown(object sender, MouseButtonEventArgs e) =>
        BeginPress(DragAllHandle, e, ShelfService.Items.ToArray());

    private void DragAll_MouseMove(object sender, MouseEventArgs e) => TryStartDrag(DragAllHandle, e);

    // ------------------------------------------------------------------ actions

    private void ShowMenu(string path)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.T("menu.open"), () => Open(path)));
        if (Services.Remote.RemoteOutbox.Available && File.Exists(path))
            menu.Items.Add(Entry(Loc.T("shelf.phone.send"), () => SendToPhone([path])));
        menu.Items.Add(Entry(Loc.T("shelf.show_in_explorer"), () => Start("explorer.exe", $"/select,\"{path}\"")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("shelf.remove"), () => ShelfService.Remove(path)));
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ téléphone (7E)

    /// <summary>Tuile qui arrive : petite échelle + fondu, avec un léger rebond.</summary>
    private static void PopIn(FrameworkElement tile)
    {
        var scale = new ScaleTransform(0.6, 0.6);
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.RenderTransform = scale;
        var grow = new DoubleAnimation(0.6, 1, Motion.Slow) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Base));
    }

    private void ShowPhone()
    {
        PhoneDrop.Visibility = Services.Remote.RemoteOutbox.Available ? Visibility.Visible : Visibility.Collapsed;
        if (_flash) return;
        int count = Services.Remote.RemoteOutbox.Current.Count;
        PhoneDropText.Text = count == 0 ? Loc.T("shelf.phone") : Loc.F("shelf.phone.count", count);
    }

    private void Highlight(bool on)
    {
        if (on) PhoneDrop.SetResourceReference(Border.BorderBrushProperty, AppearanceService.AccentBrushKey);
        else PhoneDrop.BorderBrush = Brushes.Transparent;
    }

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(File.Exists) ? paths : null;

    private void PhoneDrop_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(true);
        e.Handled = true;
    }

    private void PhoneDrop_DragLeave(object sender, DragEventArgs e)
    {
        Highlight(false);
        e.Handled = true;
    }

    private void PhoneDrop_Drop(object sender, DragEventArgs e)
    {
        Highlight(false);
        if (DroppedFiles(e) is { } paths) SendToPhone(paths);
        e.Handled = true; // pas aussi « déposé sur l'île » (étagère)
    }

    private async void SendToPhone(string[] paths)
    {
        int added = Services.Remote.RemoteOutbox.Add(paths.Where(File.Exists));
        if (added == 0) return;
        _flash = true;
        PhoneDropText.Text = Loc.T("shelf.phone.sent");
        PhoneDrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, Motion.Slow));
        await Task.Delay(1600);
        _flash = false;
        ShowPhone();
    }

    /// <summary>Clic : ce qui attend sur le téléphone, avec « Retirer ».</summary>
    private void PhoneDrop_Click(object sender, MouseButtonEventArgs e)
    {
        var items = Services.Remote.RemoteOutbox.Current;
        var menu = new ContextMenu();
        if (items.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("shelf.phone.empty"), IsEnabled = false });
        foreach (var item in items)
            menu.Items.Add(Entry(Loc.F("shelf.phone.remove", item.Name), () => Services.Remote.RemoteOutbox.Remove(item.Id)));
        if (items.Count > 1)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Entry(Loc.T("shelf.phone.clear"), Services.Remote.RemoteOutbox.Clear));
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static void Open(string path) => Start(path, null);

    private static void Start(string file, string? arguments)
    {
        try
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = true };
            if (arguments != null) start.Arguments = arguments;
            Process.Start(start);
        }
        catch (Exception ex)
        {
            Log.Error($"Étagère : ouverture de « {file} »", ex);
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e) => ShelfService.Clear();

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
