using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Un groupe affiché dans la colonne : nom et icône de l'appli, ses fenêtres (la plus récente d'abord).</summary>
internal sealed record StageColumnGroup(string Key, string Name, ImageSource? Icon, IReadOnlyList<AppWindow> Windows);

/// <summary>
/// Colonne de Stage Manager à gauche d'UN écran (7G) : un groupe par appli, la miniature DWM de sa fenêtre la plus récente (une
/// fenêtre réduite garde sa dernière image), le nom et l'icône, une petite pile derrière s'il y a plusieurs fenêtres et leurs
/// petites miniatures cliquables. Fenêtre outil toujours au premier plan, jamais active, NON transparente (DWM ne dessine pas de
/// miniature sur une fenêtre « layered », voir les aperçus du dock) ; coins arrondis et bordure sombre de Windows 11.
/// Pas de perspective : une miniature DWM n'est qu'un rectangle (ni rotation ni inclinaison possibles).
/// </summary>
internal sealed class StageColumnWindow : Window
{
    private readonly StackPanel _groups = new();
    private readonly List<(IntPtr Thumbnail, FrameworkElement Area, IntPtr Window)> _thumbnails = [];
    // Échange (7G) : miniature de la fenêtre qui s'envole cachée ; nouvelle miniature qui apparaît en fondu (fenêtre → début).
    private IntPtr _hiddenWindow;
    private readonly Dictionary<IntPtr, DateTime> _fades = [];
    private System.Windows.Threading.DispatcherTimer? _fadeTimer;
    private readonly IntPtr _hwnd;
    private RECT _work;
    private double _scale = 1;
    private IReadOnlyList<StageColumnGroup> _shown = [];
    private readonly bool _snapshot;
    // Option « Cacher la colonne automatiquement » : la colonne glisse hors de l'écran (vers la gauche) puis la fenêtre est cachée.
    private RECT _bounds;
    private bool _autoHide, _revealed = true, _animating, _clipped;
    private readonly Spring _slide = new(0); // décalage vers la gauche, en pixels (0 = affichée)
    private TimeSpan _lastFrame;

    /// <summary>Une fenêtre choisie (clic sur une miniature) : la fenêtre et la place de sa miniature à l'écran (pixels).</summary>
    public event Action<IntPtr, RECT>? WindowChosen;
    /// <summary>Clic droit sur un groupe : le menu est construit par le service.</summary>
    public event Action<StageColumnGroup, FrameworkElement>? GroupMenu;

    public StageColumnWindow(bool snapshot = false)
    {
        _snapshot = snapshot;
        Title = "Dynamic Island — Stage Manager";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = Paint.Preview;
        Topmost = !snapshot;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;
        Content = new Border { Child = _groups, Padding = new Thickness(Space.S4), Background = Paint.Preview };
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
        ApplyDarkRoundedFrame(_hwnd);
        SizeChanged += (_, _) =>
        {
            ApplyPosition();
            UpdateThumbnails();
        };
        Closed += (_, _) =>
        {
            StopAnimation();
            ClearThumbnails();
        };
    }

    public IntPtr Handle => _hwnd;

    /// <summary>La colonne a des groupes à montrer (même cachée par l'option).</summary>
    public bool HasGroups => _shown.Count > 0;

    /// <summary>Souris sur la colonne affichée (pixels écran, petite marge).</summary>
    public bool ContainsCursor(POINT cursor) =>
        IsVisible && GetWindowRect(_hwnd, out var r)
        && cursor.X >= r.Left - 4 && cursor.X < r.Right + 4 && cursor.Y >= r.Top - 4 && cursor.Y < r.Bottom + 4;

    /// <summary>Bande verticale (pixels) qui révèle la colonne : sa hauteur, là où elle apparaîtra.</summary>
    public (int Top, int Bottom) Band
    {
        get
        {
            int height = SizePx().Height;
            int top = StageLayout.ColumnTop(_work, height);
            return (top, top + height);
        }
    }

    /// <summary>Taille de la colonne en pixels : celle de la fenêtre affichée, sinon celle de son contenu mesuré.</summary>
    private (int Width, int Height) SizePx()
    {
        if (IsVisible && GetWindowRect(_hwnd, out var r)) return (r.Right - r.Left, r.Bottom - r.Top);
        if (Content is not FrameworkElement content) return (0, 0);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return ((int)Math.Ceiling(content.DesiredSize.Width * _scale), (int)Math.Ceiling(content.DesiredSize.Height * _scale));
    }

    private int BaseX => _work.Left + (int)Math.Round(StageShape.ScreenMargin * _scale);

    /// <summary>Décalage qui met la colonne entièrement à gauche du bord de son écran.</summary>
    private int HiddenDistance() => BaseX - _bounds.Left + SizePx().Width + 1;

    /// <summary>Option « Cacher la colonne automatiquement » ; coupée = la colonne revient tout de suite à sa place.</summary>
    public void SetAutoHide(bool autoHide)
    {
        if (autoHide == _autoHide) return;
        _autoHide = autoHide;
        if (autoHide)
        {
            _revealed = IsVisible; // le service la cache au prochain relevé de la souris
            return;
        }
        _revealed = true;
        StopAnimation();
        _slide.SetTarget(0, Springs.DockShow);
        _slide.Snap();
        if (HasGroups && !IsVisible) ShowAt();
        else ApplyPosition();
    }

    /// <summary>Fait venir (ressort) ou repartir (vers la gauche, puis fenêtre cachée) la colonne auto-masquée.</summary>
    public void Reveal(bool show)
    {
        if (_snapshot || !_autoHide || show == _revealed || show && !HasGroups) return;
        _revealed = show;
        if (show)
        {
            if (!IsVisible)
            {
                _slide.SetTarget(HiddenDistance(), Springs.DockShow);
                _slide.Snap();
                ShowAt();
            }
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); // par-dessus la fenêtre du centre
            _slide.SetTarget(0, Springs.DockShow);
        }
        else
        {
            if (!IsVisible) return;
            _slide.SetTarget(HiddenDistance(), Springs.DockHide);
        }
        StartAnimation();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == _lastFrame) return;
            double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
            _lastFrame = now;
            bool settled = _slide.Step(dt * AppearanceService.Speed);
            ApplyPosition();
            if (!settled) return;
            StopAnimation();
            if (!_revealed)
            {
                if (IsVisible) Hide(); // rien ne reste dessiné ni cliquable
                ClearClip();
            }
            else UpdateThumbnails();
        }
        catch (Exception ex)
        {
            StopAnimation(); // jamais d'animation figée qui inonde le journal
            Log.Error("Stage Manager : animation de la colonne", ex);
        }
    }

    /// <summary>Montre la fenêtre déjà à sa place (masquée par DWM le temps de la placer : pas d'éclair à l'ancienne place).</summary>
    private void ShowAt()
    {
        Left = _work.Left / _scale + StageShape.ScreenMargin; // sur le bon écran dès l'apparition (puis placé au pixel près)
        Top = _work.Top / _scale + StageShape.ScreenMargin;
        if (!_snapshot) SetCloaked(_hwnd, true);
        try
        {
            Show();
            UpdateLayout();
            ApplyPosition();
            UpdateThumbnails();
        }
        finally
        {
            if (!_snapshot) SetCloaked(_hwnd, false);
        }
    }

    private void ClearClip()
    {
        if (!_clipped) return;
        _clipped = false;
        SetWindowRgn(_hwnd, IntPtr.Zero, true);
    }

    /// <summary>Rangée du haut de la colonne (pixels écran) : là où file une fenêtre qui quitte le centre.</summary>
    public RECT TopSlot
    {
        get
        {
            if (GetWindowRect(_hwnd, out var r) && IsVisible)
            {
                int pad = (int)Math.Round(Space.S4 * 2 * _scale);
                return new RECT { Left = r.Left + pad, Top = r.Top + pad, Right = r.Left + pad + (int)(StageShape.ThumbWidth * _scale), Bottom = r.Top + pad + (int)(StageShape.ThumbHeight * _scale) };
            }
            int left = _work.Left + (int)(StageShape.ScreenMargin * _scale), top = (_work.Top + _work.Bottom) / 2;
            return new RECT { Left = left, Top = top, Right = left + (int)(StageShape.ThumbWidth * _scale), Bottom = top + (int)(StageShape.ThumbHeight * _scale) };
        }
    }

    /// <summary>La colonne reste à l'écran (pas auto-masquée, ou montrée par la souris).</summary>
    public bool StaysVisible => IsVisible && (!_autoHide || _revealed);

    /// <summary>Montre ces groupes sur l'écran dont voici la zone de travail ; aucun groupe = colonne cachée.</summary>
    public void ShowGroups(IReadOnlyList<StageColumnGroup> groups, RECT work, RECT bounds, double scale)
    {
        _work = work;
        _bounds = bounds;
        _scale = scale;
        if (groups.Count == 0)
        {
            HideColumn();
            return;
        }
        if (!Same(groups))
        {
            _shown = groups;
            Rebuild();
        }
        if (_autoHide && !_revealed && !IsVisible) return; // auto-masquée : contenu prêt pour sa prochaine apparition
        if (!IsVisible)
        {
            ShowAt();
            return;
        }
        UpdateLayout();
        ApplyPosition();
        UpdateThumbnails();
    }

    public void HideColumn()
    {
        StopAnimation();
        _revealed = !_autoHide;
        _slide.SetTarget(0, Springs.DockShow);
        _slide.Snap();
        ClearClip();
        if (IsVisible) Hide();
        ClearThumbnails();
        _shown = [];
        _groups.Children.Clear();
    }

    private bool Same(IReadOnlyList<StageColumnGroup> groups) =>
        groups.Count == _shown.Count && groups.Zip(_shown).All(p => p.First.Key == p.Second.Key && p.First.Name == p.Second.Name
            && p.First.Windows.Select(w => w.Handle).SequenceEqual(p.Second.Windows.Select(w => w.Handle)));

    private void Rebuild()
    {
        ClearThumbnails();
        _groups.Children.Clear();
        foreach (var group in _shown) _groups.Children.Add(BuildGroup(group));
    }

    private FrameworkElement BuildGroup(StageColumnGroup group)
    {
        var main = group.Windows[0];
        int behind = Math.Min(StageShape.PileCards, group.Windows.Count - 1);
        double offset = StageShape.PileOffset;

        // Pile : des cartes derrière la miniature, décalées vers le haut à droite (comme les piles de macOS).
        var pile = new Grid
        {
            Width = StageShape.ThumbWidth + StageShape.PileCards * offset,
            Height = StageShape.ThumbHeight + StageShape.PileCards * offset,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        for (int k = behind; k >= 1; k--)
        {
            pile.Children.Add(new SquircleBorder
            {
                Width = StageShape.ThumbWidth,
                Height = StageShape.ThumbHeight,
                CornerRadius = Corners.S,
                Background = Paint.Card,
                BorderBrush = Paint.Control,
                BorderThickness = Borders.Hairline,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(k * offset, (StageShape.PileCards - k) * offset, 0, 0),
                Opacity = 1 - 0.25 * k,
            });
        }
        var area = Area(main, group.Icon, StageShape.ThumbWidth, StageShape.ThumbHeight, Sizes.AppIcon);
        area.Margin = new Thickness(0, StageShape.PileCards * offset, 0, 0);
        area.HorizontalAlignment = HorizontalAlignment.Left;
        area.VerticalAlignment = VerticalAlignment.Top;
        pile.Children.Add(area);

        var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S3, 0, 0) };
        if (group.Icon != null)
            label.Children.Add(new Image { Source = group.Icon, Width = IconSize.M, Height = IconSize.M, Margin = new Thickness(0, 0, Space.S3, 0), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock
        {
            Text = group.Name,
            FontSize = TypeRamp.Caption,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = StageShape.ThumbWidth - IconSize.M - Space.S8,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (group.Windows.Count > 1)
            label.Children.Add(new TextBlock
            {
                Text = Loc.F("stage.count", group.Windows.Count),
                FontSize = TypeRamp.Caption,
                Foreground = Paint.TextSecondary,
                Margin = new Thickness(Space.S2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

        var content = new StackPanel();
        content.Children.Add(pile);
        content.Children.Add(label);

        // Les autres fenêtres de la pile : petites miniatures cliquables (clic = celle-là).
        if (group.Windows.Count > 1)
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S2, 0, 0) };
            foreach (var other in group.Windows.Skip(1).Take(StageShape.MaxChips))
            {
                var chip = Area(other, group.Icon, StageShape.ChipWidth, StageShape.ChipHeight, IconSize.S);
                chip.Margin = new Thickness(0, 0, Space.S2, 0);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = other.Title;
                var handle = other.Handle;
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    Choose(handle, chip);
                };
                chips.Children.Add(chip);
            }
            content.Children.Add(chips);
        }

        var card = new SquircleBorder
        {
            Child = content,
            Padding = new Thickness(Space.S4),
            Margin = new Thickness(0, 0, 0, StageShape.GroupGap),
            CornerRadius = Corners.M,
            BorderThickness = Borders.Hairline,
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = main.Title,
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = Paint.Control;
            card.BorderBrush = Paint.ActiveBorder;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = Brushes.Transparent;
            card.BorderBrush = Brushes.Transparent;
        };
        card.MouseLeftButtonUp += (_, _) => Choose(main.Handle, area);
        card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            GroupMenu?.Invoke(group, card);
        };
        return card;
    }

    /// <summary>Zone où DWM dessine la miniature (l'icône de l'appli, estompée, reste visible si Windows n'a pas d'image).</summary>
    private FrameworkElement Area(AppWindow window, ImageSource? icon, double width, double height, double iconSize)
    {
        var area = new SquircleBorder
        {
            Width = width,
            Height = height,
            CornerRadius = width < StageShape.ThumbWidth ? Corners.XS : Corners.S,
            Background = Paint.Card,
            Child = icon == null ? null : new Image { Source = icon, Width = iconSize, Height = iconSize, Opacity = Interaction.DimmedOpacity },
        };
        if (!_snapshot && window.Handle != IntPtr.Zero && DwmRegisterThumbnail(_hwnd, window.Handle, out var thumbnail) == 0)
            _thumbnails.Add((thumbnail, area, window.Handle));
        return area;
    }

    private void Choose(IntPtr window, FrameworkElement area)
    {
        if (PresentationSource.FromVisual(area) == null || !GetWindowRect(_hwnd, out var r)) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var box = area.TransformToAncestor(this).TransformBounds(new Rect(area.RenderSize));
        var slot = new RECT
        {
            Left = r.Left + (int)Math.Round(box.Left * scale),
            Top = r.Top + (int)Math.Round(box.Top * scale),
            Right = r.Left + (int)Math.Round(box.Right * scale),
            Bottom = r.Top + (int)Math.Round(box.Bottom * scale),
        };
        WindowChosen?.Invoke(window, slot);
    }

    /// <summary>Chaque miniature sur sa zone, au format de la fenêtre (pixels physiques de la colonne).</summary>
    private void UpdateThumbnails()
    {
        if (!IsVisible) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var now = DateTime.UtcNow;
        foreach (var (thumbnail, area, sourceWindow) in _thumbnails)
        {
            if (PresentationSource.FromVisual(area) == null) continue;
            var box = area.TransformToAncestor(this).TransformBounds(new Rect(area.RenderSize));
            double aspect = DwmQueryThumbnailSourceSize(thumbnail, out var source) == 0 && source.cx > 0 && source.cy > 0
                ? (double)source.cx / source.cy : box.Width / Math.Max(1, box.Height);
            double w = box.Width, h = w / aspect;
            if (h > box.Height)
            {
                h = box.Height;
                w = h * aspect;
            }
            var dest = new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
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
                opacity = ThumbOpacity(sourceWindow, now),
                fVisible = sourceWindow == _hiddenWindow ? 0 : 1,
                fSourceClientAreaOnly = 0,
            };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private byte ThumbOpacity(IntPtr window, DateTime now)
    {
        if (!_fades.TryGetValue(window, out var since)) return 255;
        double f = (now - since).TotalSeconds * Math.Max(0.1, AppearanceService.Speed) / Motion.StageColumnFade.TotalSeconds;
        return (byte)Math.Round(255 * Math.Clamp(f, 0, 1));
    }

    /// <summary>Échange : la miniature de cette fenêtre (celle qui s'envole vers le centre) n'est plus dessinée ici (pas de double).</summary>
    public void HideThumbnailOf(IntPtr window)
    {
        _hiddenWindow = window;
        UpdateThumbnails();
    }

    /// <summary>
    /// Fin d'un échange, AVANT la mise à jour de la colonne : la miniature de cette fenêtre (celle qui vient du centre) apparaîtra
    /// en fondu au lieu d'un coup ; la miniature cachée redevient normale.
    /// </summary>
    public void FadeInThumbnailOf(IntPtr window)
    {
        _hiddenWindow = IntPtr.Zero;
        if (window == IntPtr.Zero) return;
        _fades[window] = DateTime.UtcNow;
        if (_fadeTimer == null)
        {
            _fadeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _fadeTimer.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                foreach (var done in _fades.Where(p => (now - p.Value) > Motion.StageColumnFade + Motion.StageColumnFade).Select(p => p.Key).ToList())
                    _fades.Remove(done);
                UpdateThumbnails();
                if (_fades.Count == 0) _fadeTimer?.Stop();
            };
        }
        _fadeTimer.Start();
    }

    /// <summary>
    /// Contre le bord gauche de la zone de travail, centrée en hauteur (pixels physiques : écrans d'échelles différentes), moins le
    /// décalage de l'auto-masquage. La partie passée à gauche du bord de l'écran est découpée (région de la fenêtre) : rien ne se
    /// dessine sur l'écran voisin collé à gauche.
    /// </summary>
    private void ApplyPosition()
    {
        if (_snapshot || !IsVisible || !GetWindowRect(_hwnd, out var r)) return;
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        int x = BaseX - (int)Math.Round(_slide.Value);
        int y = StageLayout.ColumnTop(_work, height);
        if (r.Left != x || r.Top != y) SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        int clip = _bounds.Right > _bounds.Left ? _bounds.Left - x : 0;
        if (clip > 0)
        {
            var region = CreateRectRgn(Math.Min(clip, width), 0, width, height);
            if (SetWindowRgn(_hwnd, region, true) != 0) _clipped = true; // la région appartient alors à Windows
            else DeleteObject(region);
        }
        else ClearClip();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private void ClearThumbnails()
    {
        foreach (var (thumbnail, _, _) in _thumbnails) DwmUnregisterThumbnail(thumbnail);
        _thumbnails.Clear();
    }

    /// <summary>Rendu --snapshot : groupes d'exemple (miniatures dessinées, pas DWM), hors écran.</summary>
    internal static async Task RenderSnapshotAsync(string path)
    {
        static ImageSource Sample(Color top, Color bottom)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Fausse fenêtre : barre de titre sombre et quelques lignes de « texte ».
                dc.DrawRectangle(new LinearGradientBrush(top, bottom, 90), null, new Rect(0, 0, 160, 100));
                dc.DrawRectangle(new SolidColorBrush(Swatch.InkA(0x80)), null, new Rect(0, 0, 160, 12));
                for (int i = 0; i < 4; i++)
                    dc.DrawRectangle(new SolidColorBrush(Swatch.WhiteA(0x40)), null, new Rect(12, 22 + i * 16, 90 - i * 14, 7));
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        var window = new StageColumnWindow(snapshot: true) { Left = -30000, Top = 0 };
        var groups = new[]
        {
            new StageColumnGroup("a", Loc.T("stage.sample.browser"), null, [new AppWindow(IntPtr.Zero, "Exemple 1", null, null, true), new AppWindow(IntPtr.Zero, "Exemple 2", null, null, true), new AppWindow(IntPtr.Zero, "Exemple 3", null, null, true)]),
            new StageColumnGroup("b", Loc.T("stage.sample.music"), null, [new AppWindow(IntPtr.Zero, "Exemple", null, null, true)]),
            new StageColumnGroup("c", Loc.T("stage.sample.notes"), null, [new AppWindow(IntPtr.Zero, "Exemple", null, null, true), new AppWindow(IntPtr.Zero, "Exemple 2", null, null, true)]),
        };
        window._shown = groups;
        window.Rebuild();
        window.Show();
        window.UpdateLayout();
        // Miniatures d'exemple dessinées dans les zones (à la place des miniatures DWM).
        var colors = new[] { (Swatch.Indigo, Swatch.Preview), (Swatch.Green, Swatch.Preview), (Swatch.Orange, Swatch.Preview) };
        int index = 0;
        foreach (var card in window._groups.Children.OfType<SquircleBorder>())
        {
            var (top, bottom) = colors[index++ % colors.Length];
            foreach (var zone in Descendants(card).OfType<SquircleBorder>().Where(b => ReferenceEquals(b.Background, Paint.Card) && b.Opacity >= 1))
                zone.Child = new Image { Source = Sample(top, bottom), Stretch = Stretch.UniformToFill };
        }
        window.UpdateLayout();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
