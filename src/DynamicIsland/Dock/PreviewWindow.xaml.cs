using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Aperçu des fenêtres d'une appli au survol de son icône : miniatures en direct fournies par DWM.
/// Clic sur une miniature = aller à cette fenêtre ; « × » = la fermer (taille et couleur réglables, correctif 15c).
/// Un seul cadre par fenêtre (correctif 15c) : la carte entière (titre, « × » et miniature) s'encadre au survol ; la zone de
/// la miniature n'a plus de fond à elle (on voyait deux rectangles l'un dans l'autre).
/// </summary>
public partial class PreviewWindow : Window
{
    private const double MaxThumbWidth = 220;
    private const double MaxThumbHeight = 140;
    private const double MinSlotWidth = 130;

    private static readonly Brush HoverBrush = Paint.Control;
    private static readonly Brush FrameBrush = Paint.ActiveBorder;

    private readonly IntPtr _hwnd;
    private readonly List<Slot> _slots = [];
    private DockItem? _item;
    private Point _anchorPx;
    private bool _below;
    /// <summary>Fenêtre sous la souris (la seule encadrée tant que la souris est sur l'aperçu).</summary>
    private Slot? _hoveredSlot;
    /// <summary>Mode --snapshot : fenêtres d'exemple sans miniature DWM, fenêtre hors écran.</summary>
    private bool _snapshot;

    private sealed record Slot(AppWindow Window, IntPtr Thumbnail, Border Root, Border Area, Button Close, double FitWidth, double FitHeight);

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
        Closed += (_, _) => ClearSlots();
    }

    /// <summary>L'utilisateur a choisi une fenêtre (le dock ferme alors l'aperçu).</summary>
    public event Action? WindowChosen;

    /// <param name="anchorPx">Haut-centre de l'icône survolée, en pixels écran.</param>
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
    /// Un seul cadre net : la fenêtre survolée (fond + liseré, son « × » visible) ; sans survol, un simple liseré autour de la
    /// fenêtre au premier plan.
    /// </summary>
    private void UpdateFrames()
    {
        var foreground = _snapshot ? IntPtr.Zero : GetForegroundWindow();
        foreach (var slot in _slots)
        {
            bool hovered = slot == _hoveredSlot;
            bool framed = hovered || (_hoveredSlot == null && foreground != IntPtr.Zero && slot.Window.Handle == foreground);
            slot.Root.Background = hovered ? HoverBrush : Brushes.Transparent;
            slot.Root.BorderBrush = framed ? FrameBrush : Brushes.Transparent;
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
        AppName.Text = _item.Name;

        var dock = SettingsStore.Current.Dock;
        foreach (var window in _item.Windows)
        {
            var thumbnail = IntPtr.Zero;
            var source = new SIZE { cx = 16, cy = 10 }; // fenêtre d'exemple (--snapshot) : format 16/10
            if (!_snapshot)
            {
                if (DwmRegisterThumbnail(_hwnd, window.Handle, out thumbnail) != 0) continue;
                DwmQueryThumbnailSourceSize(thumbnail, out source);
            }
            var (fitWidth, fitHeight) = Fit(source.cx, source.cy);
            double areaWidth = Math.Max(fitWidth, MinSlotWidth);

            // Zone sur laquelle DWM dessine la miniature (sans fond : le seul cadre est celui de la carte) ; l'icône reste
            // visible si Windows n'a pas d'image de la fenêtre.
            var area = new SquircleBorder
            {
                Width = areaWidth,
                Height = fitHeight,
                Margin = new Thickness(0, Space.S3, 0, 0),
                CornerRadius = Corners.XS,
                Background = Brushes.Transparent,
                Child = new Image { Source = _item.Icon, Width = Sizes.AppIcon, Height = Sizes.AppIcon, Opacity = Interaction.DimmedOpacity },
            };

            var close = CloseButton(dock);
            close.Click += (_, _) => WindowActions.Close(window.Handle);
            var title = new TextBlock
            {
                Text = window.Title,
                FontSize = TypeRamp.Caption,
                MaxWidth = areaWidth - close.Width - Space.S3,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var header = new Grid { Height = Math.Max(Sizes.TinyButton, close.Height) };
            header.Children.Add(title);
            header.Children.Add(close);

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(area);

            var root = new SquircleBorder
            {
                Child = content,
                Padding = new Thickness(Space.S4),
                Margin = new Thickness(Space.S1),
                CornerRadius = Corners.M,
                BorderThickness = Borders.Hairline,
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var slot = new Slot(window, thumbnail, root, area, close, fitWidth, fitHeight);
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

    /// <summary>Taille de la miniature (en DIP) en gardant le format de la fenêtre.</summary>
    private static (double Width, double Height) Fit(int sourceWidth, int sourceHeight)
    {
        double aspect = sourceWidth > 0 && sourceHeight > 0 ? (double)sourceWidth / sourceHeight : 16.0 / 10;
        double width = MaxThumbWidth, height = width / aspect;
        if (height > MaxThumbHeight)
        {
            height = MaxThumbHeight;
            width = height * aspect;
        }
        return (Math.Round(width), Math.Round(height));
    }

    /// <summary>Place chaque miniature DWM sur sa zone (en pixels physiques de la fenêtre).</summary>
    private void UpdateThumbnails()
    {
        if (!IsVisible) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var slot in _slots)
        {
            if (slot.Thumbnail == IntPtr.Zero || PresentationSource.FromVisual(slot.Area) == null) continue;
            var area = slot.Area.TransformToAncestor(this).TransformBounds(new Rect(slot.Area.RenderSize));
            var dest = new Rect(area.X + (area.Width - slot.FitWidth) / 2, area.Y + (area.Height - slot.FitHeight) / 2,
                slot.FitWidth, slot.FitHeight);

            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT
                {
                    Left = (int)Math.Round(dest.Left * scale),
                    Top = (int)Math.Round(dest.Top * scale),
                    Right = (int)Math.Round(dest.Right * scale),
                    Bottom = (int)Math.Round(dest.Bottom * scale),
                },
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
    /// Mode --snapshot : aperçu de deux fenêtres d'exemple (sans miniature DWM, hors écran), la 2e survolée : un seul cadre et
    /// son « × » à la taille et à la couleur des réglages.
    /// </summary>
    internal static async Task RenderSnapshotAsync(DockItem item, string path)
    {
        item.Windows.Clear();
        item.Windows.Add(new AppWindow(IntPtr.Zero, "Exemple — fenêtre 1", null, null, false));
        item.Windows.Add(new AppWindow(IntPtr.Zero, "Exemple — fenêtre 2 (survolée)", null, null, false));
        var window = new PreviewWindow { _snapshot = true, Left = -30000, Top = 0 };
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
