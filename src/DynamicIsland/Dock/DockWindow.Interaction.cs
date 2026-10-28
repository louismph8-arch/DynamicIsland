using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>Dock : clics, glisser-déposer, menus contextuels, aperçus des fenêtres.</summary>
public partial class DockWindow
{
    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private PreviewWindow? _preview;
    private DateTime _lastPreviewInside;
    private bool _menuOpen;
    private DockIconView? _pressView;
    private Point _pressPoint;
    private DockIconView? _dragView;
    private FolderWindow? _folderWindow;
    /// <summary>Glisser-déposer : icône sous l'icône glissée, qui devient « prête » (dossier) après un court arrêt.</summary>
    private readonly DispatcherTimer _mergeDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private DockIconView? _mergeCandidate;
    private bool _mergeArmed;

    private void InitializeInteraction()
    {
        _previewDelay.Tick += (_, _) =>
        {
            _previewDelay.Stop();
            ShowPreview();
        };
        _mergeDelay.Tick += (_, _) =>
        {
            _mergeDelay.Stop();
            if (_mergeCandidate == null || _dragView == null) return;
            _mergeArmed = true;
            _mergeCandidate.SetMergeHighlight(true);
        };
        _watch.Tick += (_, _) => CheckPreviewHover();
    }

    private void AttachItemHandlers(DockIconView view)
    {
        view.MouseLeftButtonDown += (_, e) =>
        {
            _pressView = view;
            _pressPoint = e.GetPosition(Root);
            view.CaptureMouse();
            e.Handled = true;
        };
        view.MouseLeftButtonUp += (_, e) =>
        {
            bool wasPressed = _pressView == view, wasDrag = _dragView != null;
            if (view.IsMouseCaptured) view.ReleaseMouseCapture();
            if (wasPressed && !wasDrag) OnItemClicked(view);
            e.Handled = true;
        };
        view.LostMouseCapture += (_, _) =>
        {
            if (_dragView == view) EndDrag();
            _pressView = null;
        };
        view.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle || view.Item.IsFolder) return;
            view.Item.Launch(); // clic molette = nouvelle fenêtre / nouvelle instance
            view.StartBounce();
        };
        view.MouseRightButtonUp += (_, e) =>
        {
            ShowItemMenu(view.Item);
            e.Handled = true;
        };
    }

    // ------------------------------------------------------------------ clic

    /// <summary>Dossier → l'ouvrir ; pas ouverte → lancer ; ouverte → premier plan ; déjà au premier plan → réduire.</summary>
    private void OnItemClicked(DockIconView view)
    {
        var item = view.Item;
        HidePreview();
        if (item.IsFolder)
        {
            if (_folderWindow?.Folder == item) _folderWindow.Hide();
            else ShowFolder(view);
            return;
        }
        if (!item.IsRunning)
        {
            item.Launch();
            view.StartBounce();
            return;
        }

        var foreground = GetForegroundWindow();
        var active = item.Windows.FirstOrDefault(w => w.Handle == foreground);
        if (active != null)
        {
            WindowActions.Minimize(active.Handle);
            return;
        }
        var target = item.Windows.FirstOrDefault(w => !w.IsMinimized) ?? item.Windows[0];
        WindowActions.Activate(target.Handle);
    }

    // ------------------------------------------------------------------ glisser-déposer (applis épinglées)

    private void OnDragMove(MouseEventArgs e)
    {
        if (_pressView == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(Root);

        if (_dragView == null)
        {
            if (!_pressView.Item.IsPinned || Math.Abs(position.X - _pressPoint.X) < 6) return;
            _dragView = _pressView;
            _dragView.Drag.BeginAnimation(TranslateTransform.XProperty, null);
            _previewDelay.Stop();
            HidePreview();
            HideLabel();
            UpdateMagnification(); // pas de grossissement pendant le glisser
        }

        var item = _dragView.Item;
        double pointer = e.GetPosition(ItemsPanel).X;
        var mergeTarget = FindMergeTarget(item, pointer);
        if (mergeTarget != _mergeCandidate) SetMergeCandidate(mergeTarget);
        if (mergeTarget == null)
        {
            // Pas au-dessus d'une autre icône : on réordonne.
            int from = _pinned.IndexOf(item);
            int to = _pinned.Count(other => other != item && SlotCenter(_views[other]) < pointer);
            if (to != from)
            {
                _pinned.RemoveAt(from);
                _pinned.Insert(to, item);
                SyncViews();
                ItemsPanel.UpdateLayout();
            }
        }
        _dragView.Drag.X = pointer - SlotCenter(_dragView);
    }

    private static double SlotCenter(UIElement element)
    {
        var slot = LayoutInformation.GetLayoutSlot((FrameworkElement)element);
        return slot.Left + slot.Width / 2;
    }

    /// <summary>Icône épinglée dont le centre est sous le curseur (on ne met pas un dossier dans un dossier).</summary>
    private DockIconView? FindMergeTarget(DockItem dragged, double pointer)
    {
        if (dragged.IsFolder) return null;
        foreach (var other in _pinned)
        {
            if (other == dragged || !_views.TryGetValue(other, out var view)) continue;
            if (Math.Abs(pointer - SlotCenter(view)) < DockIconView.BaseWidth * DockShape.MergeZone) return view;
        }
        return null;
    }

    private void SetMergeCandidate(DockIconView? view)
    {
        _mergeCandidate?.SetMergeHighlight(false);
        _mergeCandidate = view;
        _mergeArmed = false;
        _mergeDelay.Stop();
        if (view != null) _mergeDelay.Start();
    }

    private void EndDrag()
    {
        var view = _dragView;
        if (view == null) return;
        _dragView = null;
        var target = _mergeArmed ? _mergeCandidate : null;
        SetMergeCandidate(null);

        if (target != null)
        {
            MergeInto(view.Item, target.Item);
        }
        else
        {
            view.Drag.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Motion.Swift)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            SavePins();
        }
        UpdateMagnification();
    }

    // ------------------------------------------------------------------ dossiers

    /// <summary>Icône lâchée sur une autre : elles forment un dossier (ou l'icône rejoint le dossier).</summary>
    private void MergeInto(DockItem dragged, DockItem target)
    {
        _pinned.Remove(dragged);
        if (target.IsFolder)
        {
            var children = target.Children!;
            if (!children.Any(c => c.Key == dragged.Key)) children.Add(dragged);
            dragged.Windows.Clear();
            target.RefreshFolderIcon();
            if (_views.TryGetValue(target, out var folderView)) folderView.RefreshIcon();
        }
        else
        {
            _pinned[_pinned.IndexOf(target)] = DockItem.CreateFolder(Loc.T("dock.folder.default"), [target, dragged]);
        }
        SavePins();
        OnWindowsChanged();
    }

    /// <summary>Ouvre la grille d'un dossier au-dessus de son icône (avec le nom prêt à être modifié si <paramref name="rename"/>).</summary>
    private void ShowFolder(DockIconView view, bool rename = false)
    {
        if (PresentationSource.FromVisual(view) == null) return;
        _previewDelay.Stop();
        HidePreview();
        HideLabel();
        if (_folderWindow == null)
        {
            _folderWindow = new FolderWindow();
            _folderWindow.AppChosen += OpenFromFolder;
            _folderWindow.Renamed += SavePins;
            _folderWindow.RemoveRequested += RemoveFromFolder;
        }
        var inner = Root.PointToScreen(view.IconInnerCenter(Root));
        _folderWindow.ShowFor(view.Item, new Point(inner.X / _scale, inner.Y / _scale), _screen, below: AtTop, rename);
    }

    /// <summary>Appli choisie dans un dossier : premier plan si elle est déjà ouverte, sinon lancement.</summary>
    private void OpenFromFolder(DockItem app)
    {
        var window = _tracker.Windows.FirstOrDefault(app.Matches);
        if (window != null)
        {
            WindowActions.Activate(window.Handle);
            return;
        }
        app.Launch();
        _views.Values.FirstOrDefault(v => v.Item.Children?.Contains(app) == true)?.StartBounce();
    }

    /// <summary>« Sortir du dossier » (l'appli revient juste après le dossier) ou « Retirer du dock ».</summary>
    private void RemoveFromFolder(DockItem app, bool keepInDock)
    {
        var folder = _pinned.FirstOrDefault(i => i.Children?.Contains(app) == true);
        if (folder == null) return;
        folder.Children!.Remove(app);
        int index = _pinned.IndexOf(folder);
        if (keepInDock) _pinned.Insert(index + 1, app);

        if (folder.Children.Count <= 1)
        {
            // Un dossier d'une seule appli n'a plus de sens : on le défait.
            _folderWindow?.Hide();
            _pinned.RemoveAt(index);
            _pinned.InsertRange(index, folder.Children);
        }
        else
        {
            folder.RefreshFolderIcon();
            if (_views.TryGetValue(folder, out var view)) view.RefreshIcon();
            _folderWindow?.Refresh();
        }
        SavePins();
        OnWindowsChanged();
    }

    /// <summary>« Défaire le dossier » : ses applis reprennent sa place dans le dock.</summary>
    private void Ungroup(DockItem folder)
    {
        int index = _pinned.IndexOf(folder);
        if (index < 0) return;
        _folderWindow?.Hide();
        _pinned.RemoveAt(index);
        _pinned.InsertRange(index, folder.Children!);
        SavePins();
        OnWindowsChanged();
    }

    // ------------------------------------------------------------------ menus

    private void ShowItemMenu(DockItem item)
    {
        var menu = new ContextMenu();
        if (item.IsFolder && _views.TryGetValue(item, out var folderView))
        {
            menu.Items.Add(MenuEntry(Loc.T("menu.open"), () => ShowFolder(folderView)));
            menu.Items.Add(MenuEntry(Loc.T("menu.rename"), () => ShowFolder(folderView, rename: true)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(Loc.T("dock.folder.ungroup"), () => Ungroup(item)));
            menu.Items.Add(MenuEntry(Loc.T("dock.remove"), () => TogglePin(item)));
            OpenMenu(menu);
            return;
        }
        menu.Items.Add(MenuEntry(Loc.T(item.IsPinned ? "dock.unpin" : "dock.pin"), () => TogglePin(item)));
        if (item.IsPinned)
        {
            menu.Items.Add(MenuEntry(Loc.T("dock.icon.change"), () => ChooseIcon(item)));
            if (item.Pin!.Icon != null) menu.Items.Add(MenuEntry(Loc.T("dock.icon.original"), () => SetIcon(item, null)));
        }
        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            var label = item.Windows.Count > 1 ? Loc.F("dock.close_windows", item.Windows.Count) : Loc.T("dock.close");
            menu.Items.Add(MenuEntry(label, () =>
            {
                foreach (var window in item.Windows) WindowActions.Close(window.Handle);
            }));
        }
        OpenMenu(menu);
    }

    private void DockRoot_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tray.Contains(e.OriginalSource as DependencyObject)) return; // clic droit = menu de l'appli de l'icône
        if (_status.Contains(e.OriginalSource as DependencyObject)) return; // la case a son propre menu
        var menu = new ContextMenu();
        menu.Items.Add(MenuEntry(Loc.T("dock.add_app"), AddApp));
        menu.Items.Add(PlaceMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(TaskbarSwitch.Label, TaskbarSwitch.Toggle));
        menu.Items.Add(MenuEntry(Loc.T("tray.settings"), () => SettingsWindow.Open(SettingsPage.Dock)));
        OpenMenu(menu);
        e.Handled = true;
    }

    /// <summary>Sous-menu « Position » : écran, bord, recentrer (dock flottant), verrouiller.</summary>
    private MenuItem PlaceMenu()
    {
        var place = new MenuItem { Header = Loc.T("dock.position") };
        var monitors = ScreenInfo.All();
        if (monitors.Count > 1)
        {
            foreach (var monitor in monitors)
            {
                var name = ScreenInfo.SettingName(monitor);
                var entry = MenuEntry(monitor.Label, () => MoveDock(name, _settings.Edge, 0));
                entry.IsChecked = _screen.Handle == monitor.Handle;
                place.Items.Add(entry);
            }
            place.Items.Add(new Separator());
        }
        var bottom = MenuEntry(Loc.T("dock.position.bottom"), () => MoveDock(_settings.Screen, DockEdge.Bottom, _settings.Offset));
        bottom.IsChecked = !AtTop;
        var top = MenuEntry(Loc.T("dock.position.top"), () => MoveDock(_settings.Screen, DockEdge.Top, _settings.Offset));
        top.IsChecked = AtTop;
        place.Items.Add(bottom);
        place.Items.Add(top);
        if (!IsBar && _settings.Offset != 0) place.Items.Add(MenuEntry(Loc.T("dock.position.center"), () => MoveDock(_settings.Screen, _settings.Edge, 0)));
        place.Items.Add(new Separator());
        var locked = MenuEntry(Loc.T("island.verrouiller_position"), () => SettingsStore.Update(s => s.Dock.LockPosition = !s.Dock.LockPosition));
        locked.IsChecked = _settings.LockPosition;
        place.Items.Add(locked);
        return place;
    }

    /// <summary>Nouvelle place du dock (menu, paramètres, fin d'un glisser) : enregistrée puis appliquée.</summary>
    private static void MoveDock(string? screen, DockEdge edge, double offset) => SettingsStore.Update(s =>
    {
        s.Dock.Screen = screen;
        s.Dock.Edge = edge;
        s.Dock.Offset = offset;
    });

    // ------------------------------------------------------------------ déplacer le dock (5I)

    private POINT? _movePress;
    /// <summary>Pendant un déplacement : curseur − centre du dock, en pixels (le dock ne saute pas sous la souris).</summary>
    private double _moveGrab;
    /// <summary>L'espace réservé (mode Fixe) a été libéré parce que le dock change d'écran ou de bord.</summary>
    private bool _moveReleased;
    private const int MoveThreshold = 8;

    /// <summary>Appui sur le fond du dock (pas sur une icône, un bouton ou une icône d'arrière-plan) : glisser = le déplacer.</summary>
    private void DockRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.LockPosition || _hidden || SnapshotFolder != null || _tray.Contains(e.OriginalSource as DependencyObject)
            || _status.Contains(e.OriginalSource as DependencyObject)) return;
        if (!GetCursorPos(out var cursor) || !DockRoot.CaptureMouse()) return;
        _movePress = cursor;
        _moveGrab = cursor.X - DockRoot.PointToScreen(new Point(DockRoot.ActualWidth / 2, 0)).X;
        e.Handled = true;
    }

    private void DockRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movePress == null) return;
        DockRoot.ReleaseMouseCapture(); // → LostMouseCapture : fin du déplacement
        e.Handled = true;
    }

    private void DockRoot_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_movePress == null || e.OriginalSource != DockRoot) return; // une icône qui lâche la souris
        _movePress = null;
        if (_live != null) EndMoveDock();
    }

    /// <summary>
    /// Pendant un déplacement : le dock saute sur l'écran sous la souris, au bord (haut / bas) le plus proche ; le dock
    /// flottant glisse en plus le long du bord (aimanté au centre). Rien n'est enregistré avant le lâcher.
    /// </summary>
    private bool OnMoveDock()
    {
        if (_movePress is not { } press || !GetCursorPos(out var cursor)) return _movePress != null;
        if (_live == null)
        {
            if (Math.Abs(cursor.X - press.X) < MoveThreshold && Math.Abs(cursor.Y - press.Y) < MoveThreshold) return true;
            _live = (_settings.Screen, _settings.Edge, _settings.Offset);
            _moveReleased = false;
            _previewDelay.Stop();
            HidePreview();
            HideLabel();
            _folderWindow?.Hide();
            _pointerX = null;
            SetHovered(null);
            UpdateMagnification(); // icônes au repos pendant le déplacement
            DockRoot.Opacity = DockShape.MovingOpacity;
        }

        var screen = ScreenInfo.At(cursor);
        var edge = cursor.Y < screen.Bounds.Top + screen.HeightPx / 2 ? DockEdge.Top : DockEdge.Bottom;
        double offset = 0;
        if (!IsBar)
        {
            offset = (cursor.X - _moveGrab - (screen.Bounds.Left + screen.WidthPx / 2.0)) / screen.Scale;
            offset = Math.Abs(offset) < DockShape.SnapDistance ? 0 : Math.Round(offset);
        }
        (string? Screen, DockEdge Edge, double Offset) target = (ScreenInfo.SettingName(screen), edge, offset);
        var current = _live.Value;
        if (target == current) return true;
        _live = target;
        if (target.Screen != current.Screen || target.Edge != current.Edge)
        {
            if (!_moveReleased) _appBar?.Release(); // la réservation sera refaite à la nouvelle place, au lâcher
            _moveReleased = true;
            ApplyAppearance(); // retourne le dock si besoin et le pose sur son nouvel écran
        }
        else
        {
            ApplyDockMargin();
            UpdateMagnification();
        }
        return true;
    }

    private void EndMoveDock()
    {
        var place = _live!.Value;
        _live = null;
        DockRoot.Opacity = 1;
        bool samePlace = place.Screen == _settings.Screen && place.Edge == _settings.Edge;
        MoveDock(place.Screen, place.Edge, place.Offset); // → OnSettingsChanged : nouvelle réservation si écran / bord changé
        if (samePlace && _moveReleased) ApplySettings(); // revenu à sa place après un détour : la réservation libérée est à refaire
    }

    /// <summary>Liste des profils (le profil actif est coché) + accès aux paramètres des profils.</summary>
    private void ShowProfileMenu()
    {
        HideLabel();
        var menu = new ContextMenu();
        foreach (var profile in ProfileService.All.ToList())
        {
            var (start, end) = ProfileService.AccentOf(profile);
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = Icons.Glyph(profile.Icon),
                FontFamily = FontStack.Icons,
                FontSize = IconSize.M,
                Margin = new Thickness(0, Space.Px, Space.S4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Paint.Gradient(start, end, 0),
            });
            header.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center });
            var id = profile.Id;
            var entry = new MenuItem { Header = header, IsChecked = profile == ProfileService.Active };
            entry.Click += (_, _) => ProfileService.Switch(id);
            menu.Items.Add(entry);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(Loc.T("dock.profiles.manage"), () => SettingsWindow.Open(SettingsPage.Profiles)));
        OpenMenu(menu);
    }

    private void OpenMenu(ContextMenu menu)
    {
        _previewDelay.Stop();
        HidePreview();
        HideLabel();
        menu.PlacementTarget = DockRoot;
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        _menuOpen = true;
        menu.IsOpen = true;
    }

    private static MenuItem MenuEntry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => action();
        return entry;
    }

    private void TogglePin(DockItem item)
    {
        if (item.IsPinned)
        {
            _pinned.Remove(item);
            if (!item.IsFolder)
            {
                item.Pin = null;
                if (item.IsRunning) _running.Insert(0, item);
            }
        }
        else
        {
            item.Pin = item.CreatePin();
            _running.Remove(item);
            _pinned.Add(item);
        }
        SavePins();
        OnWindowsChanged();
    }

    /// <summary>Image perso pour une appli épinglée : réduite et copiée en PNG dans %APPDATA%\DynamicIsland\Icônes.</summary>
    private void ChooseIcon(DockItem item)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.F("dock.icon.dialog", item.Name),
            Filter = Loc.T("dialog.images") + " (*.png, *.jpg, *.ico, *.bmp, *.gif, *.webp)|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif;*.webp",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SetIcon(item, ImportIcon(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error("Dock : image d'icône", ex);
            MessageBox.Show(Loc.T("dock.icon.unreadable"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Copie l'image (au plus 256 px de large, proportions gardées) en PNG ; renvoie le chemin de la copie.</summary>
    private static string ImportIcon(string source)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(source);
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.EndInit();
        if (image.PixelWidth > Themes.DockIconArt.CustomIconSize)
        {
            image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(source);
            image.DecodePixelWidth = Themes.DockIconArt.CustomIconSize;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
        }
        var folder = Path.Combine(AppPaths.DataFolder, "Icônes");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
        using var stream = File.Create(path);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        encoder.Save(stream);
        return path;
    }

    private void SetIcon(DockItem item, string? path)
    {
        item.SetCustomIcon(path);
        if (_views.TryGetValue(item, out var view)) view.RefreshIcon();
        SavePins();
    }

    private void AddApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("dock.add_app.dialog"),
            Filter = Loc.T("dock.add_app.filter") + " (*.exe, *.lnk, *.url)|*.exe;*.lnk;*.url",
            DereferenceLinks = false, // garder le raccourci (ses arguments et son icône)
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        };
        if (dialog.ShowDialog() != true) return;

        var item = DockItem.FromFile(dialog.FileName);
        if (_pinned.Any(i => i.Key == item.Key)) return;
        _pinned.Add(item);
        SavePins();
        OnWindowsChanged();
    }

    // ------------------------------------------------------------------ aperçus des fenêtres

    private void OnHoveredChanged()
    {
        _previewDelay.Stop();
        if (_hovered == null || _dragView != null) return;

        if (_preview?.IsVisible == true)
        {
            // On glisse d'une icône à l'autre : l'aperçu suit immédiatement.
            if (_hovered.Item.IsRunning) ShowPreview();
            else HidePreview();
            return;
        }
        if (_hovered.Item.IsRunning) _previewDelay.Start();
    }

    private void ShowPreview()
    {
        var view = _hovered;
        if (view == null || !view.Item.IsRunning || _hidden || _menuOpen || _dragView != null) return;
        if (PresentationSource.FromVisual(view) == null) return; // dock en cours de fermeture

        if (_preview == null)
        {
            _preview = new PreviewWindow();
            _preview.WindowChosen += () => HidePreview();
        }
        var anchor = Root.PointToScreen(view.IconInnerCenter(Root));
        _preview.ShowFor(view.Item, anchor, below: AtTop);
        _lastPreviewInside = DateTime.UtcNow;
        HideLabel();
    }

    private void HidePreview() => _preview?.HidePreview();

    /// <summary>Ferme l'aperçu quand la souris n'est plus ni sur le dock ni sur l'aperçu.</summary>
    private void CheckPreviewHover()
    {
        if (_preview is not { IsVisible: true } || !GetCursorPos(out var cursor)) return;
        if (IsCursorOverDock(cursor) || _preview.ContainsCursor(cursor))
        {
            _lastPreviewInside = DateTime.UtcNow;
            return;
        }
        if (DateTime.UtcNow - _lastPreviewInside > TimeSpan.FromMilliseconds(300)) HidePreview();
    }
}
