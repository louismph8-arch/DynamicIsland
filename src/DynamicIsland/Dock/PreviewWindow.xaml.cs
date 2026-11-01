using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Aperçu des fenêtres d'une appli au survol de son icône : miniatures en direct fournies par DWM.
/// Clic sur une miniature = aller à cette fenêtre ; « × » = la fermer (taille et couleur réglables, correctif 15c).
/// Refait après la 1.35.0 (« la fenêtre doit prendre toute la place ») : plus de bande avec le nom de l'appli (l'étiquette du
/// dock le montre déjà) ; chaque carte = une ligne fine (titre tronqué + « × ») puis la miniature. La zone de la miniature a
/// le format de la fenêtre (<see cref="PreviewLayout"/>) et DWM la remplit entièrement ; le rectangle est lu en pixels
/// physiques d'après la position réelle de la zone à l'écran (aucun facteur DPI deviné) ; une fenêtre réduite (DWM peut ne
/// donner que la taille de sa petite barre) prend le format de sa place normale. Une seule fenêtre = pas de cadre de carte (le
/// bord de l'aperçu est le seul encadré) ; plusieurs = cartes côte à côte, la survolée encadrée en verre opalin.
/// </summary>
public partial class PreviewWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly List<Slot> _slots = [];
    private DockItem? _item;
    private Point _anchorPx;
    private bool _below;
    /// <summary>Fenêtre sous la souris (la seule encadrée tant que la souris est sur l'aperçu).</summary>
    private Slot? _hoveredSlot;
    /// <summary>Mode --snapshot : fenêtres d'exemple sans miniature DWM, fenêtre hors écran.</summary>
    private bool _snapshot;
    /// <summary>Mode --snapshot : taille (px) de chaque fenêtre d'exemple, dans l'ordre.</summary>
    private IReadOnlyList<(int Width, int Height)> _snapshotSizes = [];

    private sealed record Slot(AppWindow Window, IntPtr Thumbnail, Border Root, Border Area, Button Close, double Aspect);

    public PreviewWindow()
    {
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
        ApplyDarkRoundedFrame(_hwnd);
        SizeChanged += (_, _) =>
        {
            Reposition();
            UpdateThumbnails();
        };
        // Autre écran, autre DPI : la mise en page change d'abord, puis on replace les miniatures.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(UpdateThumbnails);
        Closed += (_, _) => ClearSlots();
    }

    /// <summary>L'utilisateur a choisi une fenêtre (le dock ferme alors l'aperçu).</summary>
    public event Action? WindowChosen;

    /// <param name="anchorPx">Milieu du bord intérieur de l'icône, en pixels.</param>
    /// <param name="below">Dock en haut de l'écran : l'aperçu s'ouvre sous l'icône.</param>
    public void ShowFor(DockItem item, Point anchorPx, bool below = false)
    {
        _anchorPx = anchorPx;
        _below = below;
        bool unchanged = _item == item && HasSameWindows(item);
        _item = item;
        if (!unchanged) Rebuild();
        if (!IsVisible) Show();
        UpdateLayout();
        Reposition();
        UpdateThumbnails();
        RefreshActive();
    }

    public void HidePreview()
    {
        if (!IsVisible) return;
        Hide();
        ClearSlots();
        _item = null;
    }

    /// <summary>Une fenêtre s'est ouverte ou fermée.</summary>
    public void RefreshWindows()
    {
        if (!IsVisible || _item == null) return;
        if (!_item.IsRunning)
        {
            HidePreview();
            return;
        }
        if (HasSameWindows(_item)) return;
        Rebuild();
        UpdateLayout();
        Reposition();
        UpdateThumbnails();
    }

    /// <summary>Encadre la fenêtre au premier plan (quand la souris n'est sur aucune autre).</summary>
    public void RefreshActive() => UpdateFrames();

    /// <summary>
    /// Plusieurs fenêtres : un seul cadre net, la fenêtre survolée (fond + liseré opalins, son « × » visible) ; sans survol, un
    /// simple liseré autour de la fenêtre au premier plan. Une seule fenêtre : jamais de cadre de carte (le bord de l'aperçu
    /// suffit), seulement son « × » au survol.
    /// </summary>
    private void UpdateFrames()
    {
        var foreground = _snapshot ? IntPtr.Zero : GetForegroundWindow();
        bool several = _slots.Count > 1;
        foreach (var slot in _slots)
        {
            bool hovered = slot == _hoveredSlot;
            bool framed = several && (hovered || (_hoveredSlot == null && foreground != IntPtr.Zero && slot.Window.Handle == foreground));
            if (several && hovered) slot.Root.SetResourceReference(BackgroundProperty, OpalTheme.TileHoverKey);
            else slot.Root.Background = Brushes.Transparent;
            if (framed) slot.Root.SetResourceReference(BorderBrushProperty, OpalTheme.TileRimKey);
            else slot.Root.BorderBrush = Brushes.Transparent;
            slot.Close.Opacity = hovered ? 1 : 0;
        }
    }

    internal bool ContainsCursor(POINT cursor)
    {
        if (!IsVisible || !GetWindowRect(_hwnd, out var r)) return false;
        const int Margin = 6;
        return cursor.X >= r.Left - Margin && cursor.X <= r.Right + Margin
            && cursor.Y >= r.Top - Margin && cursor.Y <= r.Bottom + Margin;
    }

    private bool HasSameWindows(DockItem item) =>
        _slots.Select(s => s.Window.Handle).SequenceEqual(item.Windows.Select(w => w.Handle));

    private void Rebuild()
    {
        ClearSlots();
        if (_item == null) return;

        var dock = SettingsStore.Current.Dock;
        bool several = _item.Windows.Count > 1;
        int index = 0;
        foreach (var window in _item.Windows)
        {
            var thumbnail = IntPtr.Zero;
            double aspect;
            if (_snapshot)
            {
                var (w, h) = index < _snapshotSizes.Count ? _snapshotSizes[index] : (16, 10);
                aspect = PreviewLayout.Aspect(w, h);
            }
            else
            {
                if (DwmRegisterThumbnail(_hwnd, window.Handle, out thumbnail) != 0) continue;
                aspect = SourceAspect(thumbnail, window.Handle);
            }
            index++;
            var size = PreviewLayout.Area(aspect);

            // Zone sur laquelle DWM dessine la miniature (sans fond : le seul cadre est celui de la carte) ; l'icône reste
            // visible si Windows n'a pas d'image de la fenêtre.
            var area = new SquircleBorder
            {
                Width = size.Width,
                Height = size.Height,
                Margin = new Thickness(0, Space.S2, 0, 0),
                CornerRadius = Corners.XS,
                Background = Brushes.Transparent,
                Child = _snapshot
                    ? FakeThumbnail(size, aspect)
                    : new Image { Source = _item.Icon, Width = Sizes.AppIcon, Height = Sizes.AppIcon, Opacity = Interaction.DimmedOpacity },
            };

            var close = CloseButton(dock);
            close.Click += (_, _) => WindowActions.Close(window.Handle);
            var title = new TextBlock
            {
                Text = window.Title,
                FontSize = TypeRamp.Caption,
                Margin = new Thickness(Space.S1, 0, close.Width + Space.S3, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Paint.TextSecondary");

            // Ligne fine au-dessus de la miniature : titre tronqué à la largeur de la miniature, « × » à droite.
            var header = new Grid { Width = size.Width, Height = Math.Max(Sizes.TinyButton, close.Height) };
            header.Children.Add(title);
            header.Children.Add(close);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(area);

            var root = new SquircleBorder
            {
                Child = content,
                Padding = new Thickness(Space.S3),
                Margin = new Thickness(several ? Space.S1 : 0),
                CornerRadius = Corners.M,
                BorderThickness = Borders.Hairline,
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var slot = new Slot(window, thumbnail, root, area, close, aspect);
            root.MouseEnter += (_, _) =>
            {
                _hoveredSlot = slot;
                UpdateFrames();
            };
            root.MouseLeave += (_, _) =>
            {
                if (_hoveredSlot == slot) _hoveredSlot = null;
                UpdateFrames();
            };
            root.MouseLeftButtonUp += (_, _) =>
            {
                WindowActions.Activate(window.Handle);
                WindowChosen?.Invoke();
            };

            Slots.Children.Add(root);
            _slots.Add(slot);
        }
        UpdateFrames();
    }

    /// <summary>
    /// Format de la fenêtre : taille de l'image que DWM garde ; une fenêtre réduite peut n'annoncer que sa petite barre
    /// (160 × 28 px, d'où une miniature minuscule) → format de sa place normale.
    /// </summary>
    private static double SourceAspect(IntPtr thumbnail, IntPtr window)
    {
        int width = 0, height = 0, normalWidth = 0, normalHeight = 0;
        if (DwmQueryThumbnailSourceSize(thumbnail, out var source) == 0)
        {
            width = source.cx;
            height = source.cy;
        }
        bool iconic = IsIconic(window);
        if (iconic || height < DockShape.PreviewThinSource)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(window, ref placement))
            {
                normalWidth = placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;
                normalHeight = placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;
            }
        }
        if (iconic && normalWidth > 0 && normalHeight > 0) return (double)normalWidth / normalHeight;
        if ((width <= 0 || height <= 0) && GetWindowRect(window, out var rect))
        {
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }
        return PreviewLayout.Aspect(width, height, normalWidth, normalHeight);
    }

    /// <summary>« × » d'une fenêtre : petite / moyenne (comme avant) / grande ; gris (comme avant), rouge ou couleur d'accent.</summary>
    private Button CloseButton(DockSettings dock)
    {
        var (size, glyph) = dock.CloseSize switch
        {
            PreviewCloseSize.Small => (DockShape.PreviewCloseSmall, DockShape.PreviewCloseGlyphSmall),
            PreviewCloseSize.Large => (DockShape.PreviewCloseLarge, DockShape.PreviewCloseGlyphLarge),
            _ => (DockShape.PreviewCloseMedium, DockShape.PreviewCloseGlyphMedium),
        };
        var close = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = Icons.Close,
            Width = size,
            Height = size,
            FontSize = glyph,
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0,
        };
        switch (dock.CloseColor)
        {
            case PreviewCloseColor.Red:
                close.Background = Paint.Danger;
                close.Foreground = Paint.White;
                break;
            case PreviewCloseColor.Accent:
                close.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
                close.Foreground = Paint.White;
                break;
        }
        return close;
    }

    /// <summary>
    /// Place chaque miniature DWM sur sa zone. DWM attend des pixels physiques relatifs à la fenêtre de l'aperçu : on les lit
    /// directement à l'écran (PointToScreen de la zone moins celui de la fenêtre), ce qui reste juste quel que soit le DPI de
    /// l'écran du dock, même juste après un passage d'un écran à l'autre.
    /// </summary>
    private void UpdateThumbnails()
    {
        if (!IsVisible || PresentationSource.FromVisual(this) == null) return;
        var origin = PointToScreen(new Point(0, 0));
        foreach (var slot in _slots)
        {
            if (slot.Thumbnail == IntPtr.Zero || PresentationSource.FromVisual(slot.Area) == null) continue;
            var topLeft = slot.Area.PointToScreen(new Point(0, 0));
            var bottomRight = slot.Area.PointToScreen(new Point(slot.Area.ActualWidth, slot.Area.ActualHeight));
            var dest = PreviewLayout.Destination(
                new Rect(topLeft.X - origin.X, topLeft.Y - origin.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y), slot.Aspect);

            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT { Left = dest.X, Top = dest.Y, Right = dest.X + dest.Width, Bottom = dest.Y + dest.Height },
                opacity = 255,
                fVisible = 1,
                fSourceClientAreaOnly = 0,
            };
            DwmUpdateThumbnailProperties(slot.Thumbnail, ref properties);
        }
    }

    /// <summary>Centré au-dessus de l'icône (en dessous si le dock est en haut), sans sortir de l'écran du dock.</summary>
    private void Reposition()
    {
        if (_snapshot) return; // rendu hors écran : jamais posé sur le vrai bureau
        var screen = ScreenInfo.At(new POINT { X = (int)_anchorPx.X, Y = (int)_anchorPx.Y });
        var bounds = screen.Bounds;
        double scale = screen.Scale;
        double left = _anchorPx.X / scale - ActualWidth / 2;
        left = Math.Clamp(left, bounds.Left / scale + 8, Math.Max(bounds.Left / scale + 8, bounds.Right / scale - ActualWidth - 8));
        Left = left;
        Top = _below ? _anchorPx.Y / scale + 14 : _anchorPx.Y / scale - ActualHeight - 14;
    }

    private void ClearSlots()
    {
        foreach (var slot in _slots)
            if (slot.Thumbnail != IntPtr.Zero) DwmUnregisterThumbnail(slot.Thumbnail);
        _slots.Clear();
        _hoveredSlot = null;
        Slots.Children.Clear();
    }

    /// <summary>
    /// Mode --snapshot : fausse fenêtre dessinée (barre de titre, barre latérale, contenu) à la place de la miniature DWM,
    /// posée exactement sur le rectangle que DWM recevrait (même calcul, à 100 %).
    /// </summary>
    private static FrameworkElement FakeThumbnail(Size area, double aspect)
    {
        var dest = PreviewLayout.Destination(new Rect(area), aspect);
        var page = new Grid
        {
            Width = dest.Width,
            Height = dest.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(dest.X, dest.Y, 0, 0),
            ClipToBounds = true,
        };
        page.SetResourceReference(Panel.BackgroundProperty, "Paint.Window");
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(Space.S4, dest.Height * 0.08)) });
        page.RowDefinitions.Add(new RowDefinition());
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dest.Width * 0.24) });
        page.ColumnDefinitions.Add(new ColumnDefinition());

        var bar = new Border();
        bar.SetResourceReference(Border.BackgroundProperty, "Paint.Control");
        Grid.SetColumnSpan(bar, 2);
        page.Children.Add(bar);
        var side = new Border();
        side.SetResourceReference(Border.BackgroundProperty, "Paint.Card");
        Grid.SetRow(side, 1);
        page.Children.Add(side);
        var hero = new Border
        {
            Margin = new Thickness(Space.S3),
            Height = dest.Height * 0.32,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = Corners.XS,
        };
        hero.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        Grid.SetRow(hero, 1);
        Grid.SetColumn(hero, 1);
        page.Children.Add(hero);
        var lines = new StackPanel { Margin = new Thickness(Space.S3), VerticalAlignment = VerticalAlignment.Bottom };
        foreach (double part in new[] { 0.9, 0.6, 0.75 })
        {
            var line = new Border
            {
                Height = Space.S2,
                Margin = new Thickness(0, Space.S2, 0, 0),
                Width = dest.Width * 0.5 * part,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            line.SetResourceReference(Border.BackgroundProperty, "Paint.Control");
            lines.Children.Add(line);
        }
        Grid.SetRow(lines, 1);
        Grid.SetColumn(lines, 1);
        page.Children.Add(lines);
        return page;
    }

    /// <summary>
    /// Mode --snapshot : aperçu de fenêtres d'exemple (fausses miniatures dessinées, hors écran) aux formats donnés (px) ; la
    /// dernière est survolée : son « × » à la taille et à la couleur des réglages (et son cadre s'il y a plusieurs fenêtres).
    /// </summary>
    internal static async Task RenderSnapshotAsync(DockItem item, string path, params (int Width, int Height)[] sizes)
    {
        if (sizes.Length == 0) sizes = [(1920, 1080), (1024, 768), (1080, 1920)];
        item.Windows.Clear();
        for (int i = 0; i < sizes.Length; i++)
        {
            string title = sizes.Length == 1
                ? "Paramètres — Opaline"
                : $"Exemple — fenêtre {i + 1} ({sizes[i].Width} × {sizes[i].Height})";
            item.Windows.Add(new AppWindow(IntPtr.Zero, title, null, null, false));
        }
        var window = new PreviewWindow { _snapshot = true, _snapshotSizes = sizes, Left = -30000, Top = 0 };
        window._item = item;
        window.Rebuild();
        window._hoveredSlot = window._slots[^1];
        window.UpdateFrames();
        ((Panel)window.Content).Background = window.Background; // le fond de la fenêtre n'est pas dans le contenu
        window.Show();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }
}
