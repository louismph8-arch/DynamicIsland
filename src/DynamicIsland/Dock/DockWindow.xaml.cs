using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Dock style macOS, en bas (ou en haut) de l'écran choisi — le principal par défaut. Ce fichier : cycle de vie,
/// position, réglages et synchronisation des icônes. Voir aussi DockWindow.Animation / .Visibility / .Interaction.
/// </summary>
public partial class DockWindow : Window
{
    /// <summary>Échelle du dock (1 = icônes de 48 px).</summary>
    private static double SizeScale => DockIconView.BaseIcon / DockShape.ReferenceIcon;
    private bool IsBar => _settings.Layout == DockLayout.Bar;
    // Place du dock (5I) : celle des réglages, ou celle visée pendant qu'on le déplace (enregistrée au lâcher).
    private string? PlaceScreen => _live is { } live ? live.Screen : _settings.Screen;
    private DockEdge PlaceEdge => _live?.Edge ?? _settings.Edge;
    private double PlaceOffset => _live?.Offset ?? _settings.Offset;
    private bool AtTop => PlaceEdge == DockEdge.Top;
    /// <summary>Hauteur du fond : marge haute + icône + zone du point (68 en dock flottant de taille normale).</summary>
    private double BackgroundHeight => (IsBar ? DockShape.BarTopInset : DockShape.TopInset) * SizeScale + DockIconView.BaseIcon + DockIconView.DotZone;
    /// <summary>Le dock flottant garde une marge de 8 sous lui ; la barre est collée au bord.</summary>
    private double DockVisualHeight => BackgroundHeight + (IsBar ? 0 : DockShape.FloatingGap);
    private double ReservedHeight => DockVisualHeight + (IsBar ? 0 : DockShape.ReservedExtra); // espace réservé en mode Fixe

    private readonly DockSettings _settings;
    private readonly WindowTracker _tracker = new();
    private readonly List<DockItem> _pinned = [];
    private readonly List<DockItem> _running = [];
    private readonly Dictionary<DockItem, DockIconView> _views = [];
    private readonly Border _separator = CreateSeparator();
    private readonly Button _searchButton;
    private readonly Button _profileButton;
    private readonly TextBlock _profileGlyph = new();
    /// <summary>Liste d'applis épinglées affichée : si les réglages en ont une autre (changement de profil), on recharge.</summary>
    private List<PinnedApp> _loadedPins;
    private readonly TrayArea _tray = new();
    private readonly Border _traySeparator = CreateSeparator();
    /// <summary>Zone d'état (6B) : langue du clavier, Windows Update, corbeille, Téléchargements.</summary>
    private readonly StatusArea _status = new();
    private readonly Border _statusSeparator = CreateSeparator();
    private DownloadsWindow? _downloadsWindow;
    private IntPtr _explorerTray;
    private (DockMode Mode, bool HideTaskbar, double IconSize, DockLayout Layout, string? Screen, DockEdge Edge)? _applied;
    private (DockMode Mode, bool HideTaskbar, double IconSize, DockLayout Layout, string? Screen, DockEdge Edge) AppliedKey =>
        (_settings.Mode, _settings.HideTaskbar, DockIconView.BaseIcon, _settings.Layout, PlaceScreen, PlaceEdge);
    /// <summary>Place visée pendant un déplacement du dock (null hors déplacement).</summary>
    private (string? Screen, DockEdge Edge, double Offset)? _live;
    private readonly LiquidGlass _glass;
    private string? _iconStyle; // style des icônes affiché (voir IconStyler.Signature)
    private readonly SolidColorBrush _rimBrush = new(Swatch.WithAlpha(Swatch.White, 0));

    private IntPtr _hwnd;
    private AppBar? _appBar;
    private DockBackdrop? _backdrop;
    /// <summary>Écran du dock (pixels, échelle).</summary>
    private ScreenInfo.Monitor _screen;
    private int _screenLeftPx;
    private int _screenWidthPx;
    private uint _taskbarCreatedMessage;
    private RECT _appBarRect;
    /// <summary>Bord du dock côté écran, en pixels : le bas de la barre (ou son haut si le dock est en haut de l'écran).</summary>
    private int _edgePx;
    private double _scale = 1;

    public DockWindow()
    {
        InitializeComponent();
        _searchButton = CreateSearchButton();
        _profileButton = CreateProfileButton();
        _settings = SettingsStore.Current.Dock;
        // En mode barre, la forme déborde à gauche, à droite et côté bord de l'écran : seul le bord intérieur brille.
        _glass = new LiquidGlass(DockBackground, () => DockBackground.CornerRadius.TopLeft,
            () => !IsBar ? default
                : AtTop ? new Thickness(2 * Glass.Band, 2 * Glass.Band, 2 * Glass.Band, 0)
                : new Thickness(2 * Glass.Band, 0, 2 * Glass.Band, 2 * Glass.Band));
        DockBackground.BorderBrush = _rimBrush;
        ApplyAppearance();
        _loadedPins = _settings.Pinned;
        foreach (var pin in _settings.Pinned) _pinned.Add(DockItem.FromPin(pin));
        UpdateProfileButton();

        _tray.HoverStarted += (cell, title) =>
        {
            if (_dragView == null) ShowLabel(cell, title);
        };
        _tray.HoverEnded += cell =>
        {
            if (_labelAnchor == cell) HideLabel();
        };
        _tray.CountChanged += SyncViews;
        _status.HoverStarted += (cell, text) =>
        {
            if (_dragView == null) ShowLabel(cell, text);
        };
        _status.HoverEnded += cell =>
        {
            if (_labelAnchor == cell) HideLabel();
        };
        _status.CountChanged += SyncViews;
        _status.MenuRequested += OpenMenu;
        _status.DownloadsRequested += ShowDownloads;
        DockRoot.SizeChanged += (_, _) => ApplyDockMargin(); // dock flottant décalé : il doit rester dans l'écran
        SettingsStore.Changed += OnSettingsChanged;
        IsVisibleChanged += (_, _) => UpdateBackdrop(); // dock désactivé / réactivé

        _tracker.Changed += OnWindowsChanged;
        _tracker.ForegroundChanged += () => _preview?.RefreshActive();
        _tracker.MinimizeStarted += OnWindowMinimizing;
        GamingMode.Changed += OnGamingChanged;
        OnWindowsChanged();
        InitializeVisibility();
        InitializeInteraction();
    }

    /// <summary>Affiche les icônes d'arrière-plan (zone de notification) à droite du dock.</summary>
    public void AttachTray(System.Collections.ObjectModel.ObservableCollection<ManagedShell.WindowsTray.NotifyIcon> icons) =>
        _tray.Attach(icons);

    public void DetachTray() => _tray.Detach();

    /// <summary>Module « zone d'état » arrêté après une erreur : ses cases disparaissent, le reste du dock continue.</summary>
    public void StopStatus()
    {
        _downloadsWindow?.Hide();
        _status.StopAll();
    }

    /// <summary>Pile Téléchargements ouverte au-dessus de sa case (en dessous si le dock est en haut).</summary>
    private void ShowDownloads(FrameworkElement cell)
    {
        if (PresentationSource.FromVisual(cell) == null) return;
        _previewDelay.Stop();
        HidePreview();
        HideLabel();
        _downloadsWindow ??= new DownloadsWindow();
        var inner = cell.PointToScreen(new Point(cell.ActualWidth / 2, AtTop ? cell.ActualHeight : 0));
        _downloadsWindow.ShowFor(new Point(inner.X / _scale, inner.Y / _scale), _screen, below: AtTop);
    }

    /// <summary>Réglages modifiés (fenêtre de paramètres) : on n'applique que ce qui a changé.</summary>
    private void OnSettingsChanged()
    {
        ApplyAppearance();
        _status.Rebuild(); // ordre / affichage des cases de la zone d'état
        if (IconStyler.Signature != _iconStyle) RefreshIcons(); // icônes harmonisées / teinte / accent changés
        if (!ReferenceEquals(_loadedPins, _settings.Pinned)) ReloadPins(); // profil changé : ses applis
        UpdateProfileButton();
        ApplyMinimizeEffect();
        if (!IsVisible || SnapshotFolder != null || _live != null) return; // dock désactivé : l'app s'en occupe
        if (_applied != AppliedKey) ApplySettings();
        UpdateMagnification();
    }

    // ------------------------------------------------------------------ fenêtre

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);

        if (SnapshotFolder != null)
        {
            Left = -30000; // rendu hors écran, sans toucher à la barre des tâches
            return;
        }

        _appBar = new AppBar(_hwnd);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _explorerTray = TaskbarService.MainTray();
        ApplySettings();
        ShellGuard.Start(); // remettra la barre des tâches et l'espace de travail si l'app est tuée
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SettingsStore.Changed -= OnSettingsChanged;
        GamingMode.Changed -= OnGamingChanged;
        StopAnimation();
        _previewDelay.Stop();
        _mergeDelay.Stop();
        _preview?.Close();
        _folderWindow?.Close();
        _downloadsWindow?.Close();
        _backdrop?.Close();
        _glass.Detach();
        ReleaseShell();
        _tracker.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Libère l'espace réservé et remet la barre des tâches (sans risque si déjà fait).</summary>
    public void ReleaseShell()
    {
        _appBar?.Release();
        if (TaskbarService.IsHidden) TaskbarService.Restore();
        try
        {
            SystemMinimizeAnimation.Restore();
        }
        catch (Exception ex)
        {
            Log.Error("Animation de réduction de Windows", ex);
        }
        _applied = null;
    }

    /// <summary>
    /// Effet Génie actif ⇒ l'animation de réduction de Windows est coupée (sinon les deux se superposent). Le Génie aspire
    /// la fenêtre vers le bas : avec le dock en haut de l'écran, Windows garde sa propre animation.
    /// </summary>
    private void ApplyMinimizeEffect()
    {
        try
        {
            if (_settings.Enabled && _settings.MinimizeEffect && !AtTop && IsVisible && SnapshotFolder == null && _appBar != null)
                SystemMinimizeAnimation.Disable();
            else
                SystemMinimizeAnimation.Restore();
        }
        catch (Exception ex)
        {
            Log.Error("Animation de réduction de Windows", ex);
        }
    }

    /// <summary>Une fenêtre vient d'être réduite : effet Génie vers son icône.</summary>
    private void OnWindowMinimizing(IntPtr window)
    {
        if (!_settings.MinimizeEffect || AtTop || !IsVisible || GamingMode.IsActive || SnapshotFolder != null) return;
        GenieEffect.TryStart(window, IconTargetFor(window));
    }

    /// <summary>Icône de l'appli (là où elle est quand le dock est affiché), sinon le milieu du bord du dock.</summary>
    private RECT IconTargetFor(IntPtr window)
    {
        var tracked = _tracker.Windows.FirstOrDefault(w => w.Handle == window);
        var item = tracked == null ? null : _pinned.FirstOrDefault(i => i.Matches(tracked)) ?? _running.FirstOrDefault(i => i.Matches(tracked));
        if (item != null && _views.TryGetValue(item, out var view) && PresentationSource.FromVisual(view) != null)
        {
            var bounds = view.IconBounds(Root);
            var topLeft = Root.PointToScreen(bounds.TopLeft);
            var bottomRight = Root.PointToScreen(bounds.BottomRight);
            int shift = (int)Math.Round(DockShift.Y * _scale); // dock caché : on vise sa place une fois affiché
            return new RECT
            {
                Left = (int)topLeft.X,
                Top = (int)topLeft.Y - shift,
                Right = (int)bottomRight.X,
                Bottom = (int)bottomRight.Y - shift,
            };
        }

        int size = (int)(DockIconView.BaseIcon * _scale), center = _screen.Bounds.Left + _screen.WidthPx / 2;
        return AtTop
            ? new RECT { Left = center - size / 2, Top = _edgePx, Right = center + size / 2, Bottom = _edgePx + size }
            : new RECT { Left = center - size / 2, Top = _edgePx - size, Right = center + size / 2, Bottom = _edgePx };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage)
        {
            // Ce message part aussi quand on démarre la zone de notification : on ne réagit que si
            // Explorer a vraiment redémarré (appbars perdues, barre des tâches revenue).
            Dispatcher.BeginInvoke(() =>
            {
                var tray = TaskbarService.MainTray();
                if (tray == _explorerTray) return;
                _explorerTray = tray;
                _appBar?.Release();
                ApplySettings();
            });
        }
        else if (_appBar != null && msg == _appBar.CallbackMessage && wParam.ToInt64() == ABN_POSCHANGED)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_settings.Mode == DockMode.Fixed) ReserveSpace();
                PositionWindow();
            });
        }
        return IntPtr.Zero;
    }

    /// <summary>Écrans changés : le dock retrouve son écran (ou passe sur le principal si le sien est débranché).</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplySettings);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_appBar != null) Dispatcher.BeginInvoke(PositionWindow); // passé sur un écran d'une autre échelle
    }

    /// <summary>Masque (ou non) la barre des tâches, réserve (ou non) l'espace, puis place le dock.</summary>
    public void ApplySettings()
    {
        if (_appBar == null) return; // fenêtre pas encore créée
        if (!_settings.Enabled)
        {
            ReleaseShell(); // pas de dock = la barre des tâches revient toujours
            return;
        }
        var key = AppliedKey;
        // Autre écran ou autre bord : on repart d'une réservation neuve.
        if (_applied is { } previous && (previous.Screen != key.Screen || previous.Edge != key.Edge)) _appBar.Release();
        _applied = key;
        RefreshScreen();

        // D'abord la barre des tâches (elle libère son espace), ensuite l'espace réservé au dock.
        if (_settings.HideTaskbar) TaskbarService.Hide();
        else if (TaskbarService.IsHidden) TaskbarService.Restore();

        if (_settings.Mode == DockMode.Fixed) ReserveSpace();
        else _appBar?.Release();

        PositionWindow();
        UpdateVisibility();
        ApplyMinimizeEffect();
    }

    private void RefreshScreen()
    {
        _screen = ScreenInfo.Find(PlaceScreen);
        _scale = _screen.Scale;
    }

    private void ReserveSpace()
    {
        if (_appBar == null) return;
        _appBar.Edge = AtTop ? AppBar.EdgeTop : AppBar.EdgeBottom;
        _appBarRect = _appBar.ReserveRect((int)Math.Ceiling(ReservedHeight * _screen.Scale), _screen.Bounds);
    }

    /// <summary>
    /// Fenêtre sur toute la largeur de l'écran du dock, collée en bas (ou en haut) : contre l'espace réservé en mode Fixe,
    /// sinon contre la zone de travail (au-dessus de la barre des tâches si elle est visible, sous la barre de menus).
    /// </summary>
    private void PositionWindow()
    {
        RefreshScreen();
        var bounds = _screen.Bounds;
        _screenLeftPx = bounds.Left;
        _screenWidthPx = _screen.WidthPx;
        bool reserved = _settings.Mode == DockMode.Fixed && _appBar is { IsRegistered: true };
        _edgePx = AtTop
            ? reserved ? _appBarRect.Top : _screen.WorkArea.Top
            : reserved ? _appBarRect.Bottom : _screen.WorkArea.Bottom;

        Width = _screen.WidthPx / _scale;
        Left = bounds.Left / _scale;
        Top = AtTop ? _edgePx / _scale : _edgePx / _scale - Height;
        UpdateBackdrop();
    }

    // ------------------------------------------------------------------ icônes

    /// <summary>Style des icônes changé : chaque icône (et la mosaïque des dossiers) est redessinée.</summary>
    private void RefreshIcons()
    {
        _iconStyle = IconStyler.Signature;
        foreach (var item in _pinned.Concat(_running))
        {
            item.RefreshFolderIcon();
            if (_views.TryGetValue(item, out var view)) view.RefreshIcon();
        }
    }

    /// <summary>Profil changé : on remplace les applis épinglées (les fenêtres ouvertes restent dans le dock).</summary>
    private void ReloadPins()
    {
        _loadedPins = _settings.Pinned;
        _folderWindow?.Hide();
        _pinned.Clear();
        foreach (var pin in _settings.Pinned) _pinned.Add(DockItem.FromPin(pin));
        OnWindowsChanged();
    }

    /// <summary>Recopie les applis épinglées (ordre, dossiers) dans les réglages et programme la sauvegarde.</summary>
    private void SavePins()
    {
        foreach (var item in _pinned) item.SyncFolder();
        _settings.Pinned = _pinned.Select(i => i.Pin!).ToList();
        _loadedPins = _settings.Pinned;
        SettingsStore.ScheduleSave();
    }

    /// <summary>Répartit les fenêtres ouvertes entre les applis épinglées et les autres.</summary>
    private void OnWindowsChanged()
    {
        foreach (var item in _pinned.Concat(_running)) item.Windows.Clear();

        foreach (var window in _tracker.Windows)
        {
            if (window.ExePath == null && window.AppId == null) continue; // processus protégé : impossible à regrouper
            var item = _pinned.FirstOrDefault(i => i.Matches(window)) ?? _running.FirstOrDefault(i => i.Matches(window));
            if (item == null)
            {
                item = DockItem.FromWindow(window);
                _running.Add(item);
            }
            item.Windows.Add(window);
        }

        _running.RemoveAll(i => !i.IsRunning);
        SyncViews();
        _preview?.RefreshWindows();
    }

    /// <summary>Met les vues dans l'ordre : Rechercher | épinglées | séparateur | ouvertes. Les vues retirées s'effacent en douceur.</summary>
    private void SyncViews()
    {
        // Mode barre : recherche et icônes d'arrière-plan vont sur les bords ; sinon elles suivent les apps.
        PlaceEdgeElement(_searchButton, IsBar ? EdgeLeft : null);
        PlaceEdgeElement(_profileButton, IsBar ? EdgeLeft : null);
        PlaceEdgeElement(_statusSeparator, null); // en barre, la zone d'état est au bord droit, loin des applis
        PlaceEdgeElement(_status, IsBar && _status.VisibleCount > 0 ? EdgeRight : null);
        PlaceEdgeElement(_traySeparator, IsBar && _tray.VisibleCount > 0 ? EdgeRight : null);
        PlaceEdgeElement(_tray, IsBar ? EdgeRight : null);
        OrderChildren(EdgeRight, _status, _traySeparator, _tray);

        var desired = new List<UIElement>();
        if (!IsBar)
        {
            desired.Add(_searchButton);
            desired.Add(_profileButton);
        }
        foreach (var item in _pinned) desired.Add(ViewFor(item));
        if (_pinned.Count > 0 && _running.Count > 0) desired.Add(_separator);
        foreach (var item in _running) desired.Add(ViewFor(item));
        if (!IsBar && _status.VisibleCount > 0 && desired.Count > 0)
        {
            desired.Add(_statusSeparator);
            desired.Add(_status);
        }
        if (!IsBar && _tray.VisibleCount > 0)
        {
            desired.Add(_traySeparator);
            desired.Add(_tray);
        }

        var current = ItemsPanel.Children.Cast<UIElement>().ToList();
        foreach (var view in _views.Values.ToList())
        {
            if (desired.Contains(view)) continue;
            if (!view.IsRemoving)
            {
                view.IsRemoving = true;
                view.Appear.SetTarget(0, Springs.Vanish);
            }
            int index = current.IndexOf(view);
            if (index >= 0) desired.Insert(Math.Min(index, desired.Count), view);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < ItemsPanel.Children.Count && ItemsPanel.Children[i] == desired[i]) continue;
            ItemsPanel.Children.Remove(desired[i]);
            ItemsPanel.Children.Insert(i, desired[i]);
        }
        while (ItemsPanel.Children.Count > desired.Count) ItemsPanel.Children.RemoveAt(ItemsPanel.Children.Count - 1);

        foreach (var view in _views.Values) view.UpdateRunning();
        UpdateMagnification();
    }

    /// <summary>Met un élément dans un panneau de bord (mode barre), ou le retire des bords (null).</summary>
    private void PlaceEdgeElement(UIElement element, Panel? edge)
    {
        foreach (var panel in new Panel[] { EdgeLeft, EdgeRight })
            if (panel != edge && panel.Children.Contains(element)) panel.Children.Remove(element);
        if (edge == null || edge.Children.Contains(element)) return;
        ItemsPanel.Children.Remove(element);
        edge.Children.Add(element);
    }

    /// <summary>Remet les éléments présents d'un panneau dans cet ordre (sans retirer ceux qui y sont déjà bien placés).</summary>
    private static void OrderChildren(Panel panel, params UIElement[] order)
    {
        int index = 0;
        foreach (var element in order)
        {
            int current = panel.Children.IndexOf(element);
            if (current < 0) continue;
            if (current != index)
            {
                panel.Children.RemoveAt(current);
                panel.Children.Insert(index, element);
            }
            index++;
        }
    }

    private DockIconView ViewFor(DockItem item)
    {
        if (_views.TryGetValue(item, out var view))
        {
            if (view.IsRemoving)
            {
                view.IsRemoving = false;
                view.Appear.SetTarget(1, Springs.Appear);
            }
            return view;
        }

        view = new DockIconView(item);
        view.Appear.SetTarget(1, Springs.Appear); // petit rebond à l'apparition
        AttachItemHandlers(view);
        _views[item] = view;
        return view;
    }

    /// <summary>
    /// Apparence (fenêtre de paramètres) : taille des icônes et tout ce qui en découle, arrondi, halo néon
    /// et liseré. Le fond suit tout seul (DynamicResource).
    /// </summary>
    private void ApplyAppearance()
    {
        DockIconView.BaseIcon = SettingsStore.Current.Appearance.DockIconSize;
        DockIconView.DotRatio = IsBar ? DockShape.BarDotRatio : DockShape.DotRatio;
        double scale = SizeScale;

        Height = Math.Max(DockShape.MinWindowHeight, DockIconView.BaseIcon * DockShape.WindowIcons + DockShape.WindowExtra); // grossissement + nom
        DockBackground.Height = BackgroundHeight;
        // La barre a sa propre teinte (au-dessus du flou acrylique) ; le dock flottant garde celle de l'île.
        DockBackground.SetResourceReference(Border.BackgroundProperty, IsBar ? AppearanceService.DockBrushKey : AppearanceService.IslandBrushKey);

        // Dock en haut de l'écran : tout ce qui est collé en bas passe en haut (icônes, point, boutons, liseré).
        DockIconView.AtTop = AtTop;
        var edge = AtTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        DockRoot.VerticalAlignment = DockShell.VerticalAlignment = DockBackground.VerticalAlignment = edge;
        ItemsPanel.VerticalAlignment = EdgeLeft.VerticalAlignment = EdgeRight.VerticalAlignment = edge;
        if (IsBar)
        {
            // Barre façon barre des tâches : toute la largeur, collée au bord, coins carrés, liseré côté écran.
            DockRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
            DockBackground.CornerRadius = Corners.None;
            DockBackground.BorderThickness = AtTop ? Borders.BottomHairline : Borders.TopHairline;
            ItemsPanel.Margin = Borders.None;
        }
        else
        {
            DockRoot.HorizontalAlignment = HorizontalAlignment.Center;
            DockBackground.CornerRadius = new CornerRadius(AppearanceService.Radius(Radius.DockFloating) * scale);
            DockBackground.BorderThickness = Borders.Hairline;
            ItemsPanel.Margin = new Thickness(Space.S5, 0, Space.S5, 0);
        }
        ApplyDockMargin();
        _rimBrush.Color = AppearanceService.RimColor();
        AppearanceService.ApplyGlow(DockShadow, Shadows.Dock);

        double iconCenter = DockIconView.DotZone + DockIconView.BaseIcon / 2; // depuis le bord du dock côté écran
        foreach (var separator in new[] { _separator, _traySeparator, _statusSeparator })
        {
            separator.Height = DockShape.Separator * scale;
            separator.VerticalAlignment = edge;
            separator.Margin = FromEdge(Space.S3, Space.S3, iconCenter - separator.Height / 2);
        }
        _searchButton.Height = Math.Max(DockShape.SearchMinHeight, DockShape.SearchHeight * scale);
        _searchButton.VerticalAlignment = edge;
        _searchButton.Margin = FromEdge(Space.S2, Space.S4, iconCenter - _searchButton.Height / 2);
        _profileButton.Width = _profileButton.Height = Math.Round(Math.Max(DockShape.ProfileMin, DockShape.Profile * scale));
        _profileButton.VerticalAlignment = edge;
        _profileButton.Margin = FromEdge(0, Space.S5, iconCenter - _profileButton.Height / 2);
        _profileGlyph.FontSize = Math.Max(DockShape.ProfileGlyphMin, DockShape.ProfileGlyph * scale);
        _tray.VerticalAlignment = edge;
        _tray.Margin = FromEdge(Space.S1, Space.S5, Math.Max(0, iconCenter - DockShape.TrayCell));
        _status.ApplySize(scale);
        _status.VerticalAlignment = edge;
        _status.Margin = FromEdge(Space.S2, Space.S3, Math.Max(0, iconCenter - Math.Max(DockShape.StatusCellMin, DockShape.StatusCell * scale) / 2));

        foreach (var view in _views.Values) view.Apply();
        _glass.Refresh();
        if (IsLoaded)
        {
            SyncViews(); // la recherche et les icônes d'arrière-plan changent de place selon la disposition
            PositionWindow();
        }
        UpdateBackdrop();
    }

    /// <summary>
    /// Vrai flou acrylique sous la barre (mode barre seulement : DWM ne sait flouter qu'un rectangle).
    /// Suit le décalage du dock quand il se cache, disparaît quand il est caché.
    /// </summary>
    private void UpdateBackdrop()
    {
        bool wanted = IsBar && SettingsStore.Current.Appearance.DockBlur && DockBackdrop.IsSupported
            && SnapshotFolder == null && IsVisible && _hwnd != IntPtr.Zero && _shift.Value < HiddenOffset - 1;
        if (!wanted)
        {
            _backdrop?.Hide();
            return;
        }
        _backdrop ??= new DockBackdrop();
        int height = (int)Math.Ceiling(BackgroundHeight * _scale);
        int shift = (int)Math.Round(DockShift.Y * _scale);
        int y = (AtTop ? _edgePx : _edgePx - height) + shift;
        _backdrop.Place(_screenLeftPx, y, _screenWidthPx, height, _hwnd);
    }

    /// <summary>Marges d'un élément centré sur les icônes : à gauche, à droite et du côté du bord de l'écran.</summary>
    private Thickness FromEdge(double left, double right, double fromEdge) =>
        AtTop ? new Thickness(left, fromEdge, right, 0) : new Thickness(left, 0, right, fromEdge);

    /// <summary>
    /// Barre collée au bord ; dock flottant à 8 du bord et décalé le long de celui-ci (marges inégales : un élément centré
    /// l'est dans la place qui reste), sans sortir de l'écran.
    /// </summary>
    private void ApplyDockMargin()
    {
        double offset = FloatingOffset();
        var margin = IsBar ? Borders.None : new Thickness(Math.Max(0, 2 * offset), AtTop ? DockShape.FloatingGap : 0,
            Math.Max(0, -2 * offset), AtTop ? 0 : DockShape.FloatingGap);
        if (DockRoot.Margin != margin) DockRoot.Margin = margin;
    }

    /// <summary>Décalage du dock flottant depuis le centre du bord, ramené pour qu'il tienne dans l'écran (0 pour la barre).</summary>
    private double FloatingOffset()
    {
        if (IsBar || PlaceOffset == 0) return 0;
        double dock = DockRoot.DesiredSize.Width - DockRoot.Margin.Left - DockRoot.Margin.Right;
        double room = Math.Max(0, (Root.ActualWidth - dock) / 2 - Space.S4);
        return Math.Clamp(PlaceOffset, -room, room);
    }

    /// <summary>Bouton « Rechercher » façon barre des tâches Windows, au début du dock.</summary>
    private Button CreateSearchButton()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = Icons.Search,
            FontFamily = FontStack.Icons,
            FontSize = IconSize.M,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Space.Px, Space.S4, 0),
        });
        content.Children.Add(new TextBlock { Text = Loc.T("dock.search"), FontSize = TypeRamp.Body, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Style = (Style)FindResource("RoundButton"),
            Content = content,
            Width = DockShape.SearchWidth,
            Height = DockShape.SearchHeight,
            VerticalAlignment = VerticalAlignment.Bottom, // marges calculées dans ApplyAppearance (centré sur les icônes)
            Background = Paint.Surface,
            Foreground = Paint.TextSecondary,
        };
        button.Click += (_, _) =>
        {
            HidePreview();
            SearchWindow.Toggle();
        };
        return button;
    }

    /// <summary>Pastille du profil actif (dégradé de sa couleur d'accent + son icône) : clic = changer de profil.</summary>
    private Button CreateProfileButton()
    {
        _profileGlyph.FontFamily = FontStack.Icons;
        _profileGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _profileGlyph.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button
        {
            Style = (Style)FindResource("RoundButton"),
            Content = _profileGlyph,
            Width = DockShape.Profile,
            Height = DockShape.Profile,
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = Paint.White,
        };
        button.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        button.Click += (_, _) => ShowProfileMenu();
        button.MouseEnter += (_, _) =>
        {
            if (_dragView == null) ShowLabel(button, ProfileService.Active is { } profile ? Loc.F("dock.profile", profile.Name) : Loc.T("settings.profils"));
        };
        button.MouseLeave += (_, _) =>
        {
            if (_labelAnchor == button) HideLabel();
        };
        return button;
    }

    private void UpdateProfileButton() => _profileGlyph.Text = Icons.Glyph(ProfileService.Active?.Icon ?? "user");

    private static Border CreateSeparator() => new()
    {
        Width = Stroke.Hairline,
        Height = DockShape.Separator,
        VerticalAlignment = VerticalAlignment.Bottom, // taille et marges calculées dans ApplyAppearance
        Background = Paint.Track,
    };
}
