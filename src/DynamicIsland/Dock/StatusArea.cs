using System.Diagnostics;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>
/// Zone d'état à droite du dock (6B) : langue du clavier, Windows Update, corbeille, pile Téléchargements, flèche des icônes
/// du bureau (correctif 16), mélangeur de volume (point 22), dans l'ordre et avec l'affichage choisis dans les paramètres. Chaque case a son survol (étiquette
/// du dock), son clic et son menu. Aucune action importante sans l'utilisateur : rien n'est installé ni redémarré, la corbeille
/// n'est vidée qu'après confirmation, un fichier déposé va dans la corbeille (récupérable).
/// </summary>
public sealed class StatusArea : StackPanel
{
    private static readonly Brush IdleBrush = Paint.HitTarget; // alpha 1 : capte la souris
    private static readonly Brush HoverBrush = Paint.Selected;

    private sealed class Cell
    {
        public required string Id;
        public required SquircleBorder Border;
        public required TextBlock Glyph;
        public required SquircleBorder Badge;
        public required TextBlock BadgeText;
        public required Ellipse Dot;
        public required Canvas Effects;
        public required ScaleTransform Scale;
        /// <summary>Pastille d'accent derrière le glyphe quand la case est « active » (O4, icône active).</summary>
        public required SquircleBorder Halo;
        public bool Active;
    }

    private readonly Dictionary<string, Cell> _cells = [];
    private double _cellSize = DockShape.StatusCell;
    private bool _stopped;
    /// <summary>Dernier élément des Téléchargements (relu seulement quand le dossier change, pas à chaque réglage).</summary>
    private DownloadsService.Entry? _newestDownload;
    /// <summary>Flèche des icônes du bureau : elle pivote (et glisse un peu) quand on les cache ou les remet.</summary>
    private readonly RotateTransform _arrowTurn = new();
    private readonly TranslateTransform _arrowSlide = new();
    /// <summary>État affiché par la flèche (null avant le premier affichage : pas d'animation au démarrage).</summary>
    private bool? _desktopHidden;
    /// <summary>Volume général pour la case « Mélangeur » (lu seulement au survol et à la molette, jamais en boucle).</summary>
    private readonly VolumeService _volume = new();

    public StatusArea()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Bottom;
        foreach (var id in StatusItem.All) _cells[id] = CreateCell(id);

        KeyboardLayoutService.Changed += switched =>
        {
            UpdateCell(StatusItem.Keyboard);
            if (switched && _cells[StatusItem.Keyboard].Border.Parent != null) Pop(_cells[StatusItem.Keyboard]);
        };
        WindowsUpdateService.Changed += () => UpdateCell(StatusItem.Updates);
        RecycleBinService.Changed += () => UpdateCell(StatusItem.Trash);
        DownloadsService.Changed += () =>
        {
            _newestDownload = DownloadsService.Recent(1).FirstOrDefault();
            UpdateCell(StatusItem.Downloads);
        };
        _newestDownload = DownloadsService.Recent(1).FirstOrDefault();
        Rebuild();
        foreach (var id in StatusItem.All) UpdateCell(id);
    }

    /// <summary>Survol d'une case (élément à viser pour l'étiquette, texte sur une ou plusieurs lignes).</summary>
    public event Action<FrameworkElement, string>? HoverStarted;
    public event Action<FrameworkElement>? HoverEnded;
    /// <summary>Le nombre de cases a changé (le dock doit se remettre en page).</summary>
    public event Action? CountChanged;
    /// <summary>Menu d'une case à ouvrir par le dock (même comportement que ses autres menus).</summary>
    public event Action<ContextMenu>? MenuRequested;
    /// <summary>Clic sur la pile Téléchargements : le dock ouvre la grille au-dessus de cette case.</summary>
    public event Action<FrameworkElement>? DownloadsRequested;

    public int VisibleCount => Children.Count;

    /// <summary>Module « zone d'état » arrêté après une erreur : plus aucune case jusqu'au redémarrage.</summary>
    public void StopAll()
    {
        _stopped = true;
        Rebuild();
    }

    /// <summary>Cases dans l'ordre et selon l'affichage des réglages.</summary>
    public void Rebuild()
    {
        UpdateCell(StatusItem.Desktop); // la flèche suit le réglage (clic dessus, paramètres, module arrêté…)
        int before = Children.Count;
        var wanted = _stopped || ModuleGuard.IsDisabled(ModuleGuard.Status)
            ? []
            : SettingsStore.Current.Dock.Status.Where(i => i.Visible && _cells.ContainsKey(i.Id)).Select(i => _cells[i.Id].Border).ToList();
        bool same = wanted.Count == Children.Count && wanted.Select((b, i) => Children[i] == b).All(x => x);
        if (same) return; // réglage sans rapport (appelé à chaque changement de réglage)
        Children.Clear();
        foreach (var border in wanted) Children.Add(border);
        UpdateWidth();
        if (Children.Count != before) CountChanged?.Invoke();
    }

    /// <summary>Côté d'une case pour une échelle du dock (1 = icônes de 48) ; calcul pur, vérifié par --self-test.</summary>
    internal static double CellFor(double scale) => Math.Round(Math.Max(DockShape.StatusCellMin, DockShape.StatusCell * scale));

    /// <summary>Taille des cases d'après la taille des icônes du dock.</summary>
    public void ApplySize(double scale)
    {
        _cellSize = CellFor(scale);
        foreach (var cell in _cells.Values)
        {
            cell.Border.Width = cell.Border.Height = _cellSize;
            cell.Border.Margin = new Thickness(Space.Px, 0, Space.Px, 0);
            cell.Glyph.FontSize = cell.Id == StatusItem.Keyboard
                ? Math.Max(DockShape.StatusTextMin, DockShape.StatusText * Math.Max(scale, 0.8))
                : Math.Max(DockShape.StatusGlyphMin, DockShape.StatusGlyph * scale);
        }
        UpdateWidth();
    }

    private void UpdateWidth() => Width = Children.Count * (_cellSize + 2 * Space.Px);

    /// <summary>Vrai si <paramref name="source"/> est dans la zone (le dock ignore alors ses propres clics et menus).</summary>
    public bool Contains(DependencyObject? source)
    {
        while (source != null)
        {
            if (source == this) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    // ------------------------------------------------------------------ cases

    private Cell CreateCell(string id)
    {
        var glyph = new TextBlock
        {
            FontFamily = id == StatusItem.Keyboard ? FontStack.Ui : FontStack.Icons,
            FontWeight = id == StatusItem.Keyboard ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Paint.TextPrimary,
        };
        if (id == StatusItem.Desktop)
        {
            glyph.RenderTransformOrigin = new Point(0.5, 0.5);
            glyph.RenderTransform = new TransformGroup { Children = { _arrowTurn, _arrowSlide } };
        }
        var badgeText = new TextBlock
        {
            FontSize = TypeRamp.Caption2,
            FontWeight = FontWeights.Bold,
            Foreground = Paint.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new SquircleBorder
        {
            MinWidth = DockShape.StatusBadge,
            Height = DockShape.StatusBadge,
            CornerRadius = Corners.Pill,
            Padding = new Thickness(Space.S2, 0, Space.S2, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -Space.S2, -Space.S2, 0),
            Child = badgeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        badge.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        var dot = new Ellipse
        {
            Width = DockShape.StatusDot,
            Height = DockShape.StatusDot,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Space.S2, Space.S2, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        dot.SetResourceReference(Shape.FillProperty, AppearanceService.AccentBrushKey);
        var effects = new Canvas { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var halo = IconChip.CreateHalo(Corners.M);
        var content = new Grid();
        content.Children.Add(halo);
        content.Children.Add(glyph);
        content.Children.Add(effects);
        content.Children.Add(dot);
        content.Children.Add(badge);

        var scale = new ScaleTransform(1, 1);
        var border = new SquircleBorder
        {
            Width = _cellSize,
            Height = _cellSize,
            CornerRadius = Corners.M,
            Background = IdleBrush,
            ClipContent = false,
            Child = content,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var cell = new Cell { Id = id, Border = border, Glyph = glyph, Badge = badge, BadgeText = badgeText, Dot = dot, Effects = effects, Scale = scale, Halo = halo };

        border.MouseEnter += (_, _) =>
        {
            border.Background = HoverBrush;
            PaintGlyph(cell);
            HoverStarted?.Invoke(border, HoverText(id));
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = IdleBrush;
            PaintGlyph(cell);
            HoverEnded?.Invoke(border);
        };
        border.MouseLeftButtonDown += (_, e) => e.Handled = true; // pas de déplacement du dock depuis une case
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Click(cell);
        };
        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            HoverEnded?.Invoke(border);
            MenuRequested?.Invoke(Menu(id));
        };
        if (id == StatusItem.Trash)
        {
            border.AllowDrop = true;
            border.DragEnter += (_, e) => TrashDragOver(cell, e);
            border.DragOver += (_, e) => TrashDragOver(cell, e);
            border.DragLeave += (_, e) =>
            {
                border.Background = IdleBrush;
                Animate(cell.Scale, 1);
                e.Handled = true;
            };
            border.Drop += (_, e) => TrashDrop(cell, e);
        }
        if (id == StatusItem.Mixer)
        {
            border.MouseEnter += (_, _) => UpdateCell(StatusItem.Mixer); // icône et étiquette au volume du moment
            border.MouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (_volume.TryRead(out double level, out bool _)) _volume.SetLevel(level + Math.Sign(e.Delta) * DockShape.StatusVolumeStep);
                UpdateCell(StatusItem.Mixer);
            };
        }
        return cell;
    }

    /// <summary>
    /// Couleur des glyphes (O4, la même partout) : texte secondaire au repos, texte principal au survol, icône active
    /// (dégradé d'accent + pastille) quand la case est active. La langue du clavier (texte) reste en texte principal.
    /// </summary>
    private static void PaintGlyph(Cell cell)
    {
        if (cell.Id == StatusItem.Keyboard)
        {
            cell.Glyph.Foreground = Paint.TextPrimary;
            return;
        }
        IconChip.Paint(cell.Glyph, cell.Halo, cell.Active, cell.Border.IsMouseOver ? Paint.TextPrimary : Paint.TextSecondary);
    }

    private void UpdateCell(string id)
    {
        if (!_cells.TryGetValue(id, out var cell)) return;
        bool dot = false;
        bool active = false;
        string? badge = null;
        cell.Glyph.Opacity = 1;
        switch (id)
        {
            case StatusItem.Keyboard:
                cell.Glyph.Text = (KeyboardLayoutService.Current ?? (SettingsStore.IsReadOnly ? KeyboardLayoutService.Read() : null))?.Short ?? "··";
                break;
            case StatusItem.Updates:
                bool restart = WindowsUpdateService.RestartRequired;
                int pending = WindowsUpdateService.Pending.Count;
                cell.Glyph.Text = restart ? Icons.Restart : Icons.WindowsUpdate;
                cell.Glyph.Opacity = WindowsUpdateService.Checking ? 0.5 : 1;
                if (restart) badge = "!";
                else if (pending > 0) badge = pending > 9 ? "9+" : pending.ToString(Loc.Culture);
                break;
            case StatusItem.Trash:
                bool full = RecycleBinService.Count > 0;
                cell.Glyph.Text = Icons.Trash;
                dot = full;
                break;
            case StatusItem.Downloads:
                cell.Glyph.Text = Icons.FolderDown;
                dot = _newestDownload is { } newest && newest.When > DownloadsService.SeenUntil;
                break;
            case StatusItem.Power:
                cell.Glyph.Text = Icons.Power;
                break;
            case StatusItem.Desktop:
                bool hidden = SettingsStore.Current.Dock.HideDesktopIcons;
                cell.Glyph.Text = Icons.ChevronDown;
                active = hidden; // icônes du bureau cachées = case active (icône active d'accent)
                if (_desktopHidden != hidden)
                {
                    bool animate = _desktopHidden != null && cell.Border.Parent != null;
                    _desktopHidden = hidden;
                    TurnArrow(cell, hidden, animate);
                }
                break;
            case StatusItem.Mixer:
                cell.Glyph.Text = cell.Border.IsMouseOver && _volume.TryRead(out double level, out bool muted) ? VolumeGlyph(level, muted) : Icons.Mixer;
                break;
        }
        cell.Active = active;
        PaintGlyph(cell);
        cell.Dot.Visibility = dot && badge == null ? Visibility.Visible : Visibility.Collapsed;
        cell.Badge.Visibility = badge != null ? Visibility.Visible : Visibility.Collapsed;
        cell.BadgeText.Text = badge ?? "";
        if (cell.Border.IsMouseOver) HoverStarted?.Invoke(cell.Border, HoverText(id)); // étiquette à jour pendant le survol
    }

    private string HoverText(string id)
    {
        switch (id)
        {
            case StatusItem.Keyboard:
                var layout = KeyboardLayoutService.Current;
                return (layout?.Name ?? Loc.T("status.keyboard")) + "\n" + Loc.T("status.keyboard.help");
            case StatusItem.Updates:
                return UpdatesText() + "\n" + Loc.T("status.updates.help");
            case StatusItem.Trash:
                long count = RecycleBinService.Count;
                return (count == 0 ? Loc.T("status.trash.empty")
                           : Loc.F(count == 1 ? "status.trash.one" : "status.trash.many", count, WindowsUpdateService.FormatSize(RecycleBinService.Size)))
                       + "\n" + Loc.T("status.trash.help");
            case StatusItem.Desktop:
                bool hidden = SettingsStore.Current.Dock.HideDesktopIcons;
                return Loc.T(hidden ? "status.desktop.hidden" : "status.desktop.shown") + "\n" + Loc.T(hidden ? "status.desktop.help.show" : "status.desktop.help.hide");
            case StatusItem.Power:
                return Loc.T("status.power") + "\n" + Loc.T("status.power.help");
            case StatusItem.Mixer:
                string volume = !_volume.TryRead(out double level, out bool muted) ? "—"
                    : muted ? Loc.T("volume.muted") : Loc.F("format.percent", Math.Round(level * 100));
                return Loc.F("status.mixer", volume) + "\n" + Loc.T("status.mixer.help");
            default:
                return Loc.T("folders.downloads") + (_newestDownload is { } newest ? "\n" + Loc.F("status.downloads.latest", newest.Name) : "");
        }
    }

    private static string UpdatesText()
    {
        if (WindowsUpdateService.Checking) return Loc.T("status.updates.checking");
        if (WindowsUpdateService.Error is { } error) return Loc.F("status.updates.error", error);
        if (WindowsUpdateService.LastCheck is not { } last) return Loc.T("status.updates.unchecked");
        var pending = WindowsUpdateService.Pending;
        var lines = new List<string>();
        if (WindowsUpdateService.RestartRequired) lines.Add(Loc.T("status.updates.restart"));
        if (pending.Count == 0 && !WindowsUpdateService.RestartRequired)
            lines.Add(Loc.F("status.updates.none", last.ToString("t", Loc.Culture)));
        if (pending.Count > 0)
        {
            lines.Add(Loc.F(pending.Count == 1 ? "status.updates.one" : "status.updates.many", pending.Count));
            foreach (var update in pending.Take(6))
            {
                var size = WindowsUpdateService.FormatSize(update.Size);
                lines.Add("• " + (update.Title.Length > 70 ? update.Title[..70] + "…" : update.Title) + (size.Length > 0 ? $" ({size})" : ""));
            }
            if (pending.Count > 6) lines.Add(Loc.F("status.updates.more", pending.Count - 6));
        }
        return string.Join("\n", lines);
    }

    private void Click(Cell cell)
    {
        switch (cell.Id)
        {
            case StatusItem.Keyboard:
                KeyboardLayoutService.Next();
                break;
            case StatusItem.Updates:
                WindowsUpdateService.OpenSettings();
                break;
            case StatusItem.Trash:
                RecycleBinService.Open();
                break;
            case StatusItem.Downloads:
                HoverEnded?.Invoke(cell.Border);
                DownloadsService.SeenUntil = DateTime.Now;
                UpdateCell(StatusItem.Downloads);
                DownloadsRequested?.Invoke(cell.Border);
                break;
            case StatusItem.Desktop:
                ToggleDesktopIcons();
                break;
            case StatusItem.Power:
                HoverEnded?.Invoke(cell.Border);
                MenuRequested?.Invoke(Menu(StatusItem.Power)); // clic gauche = même menu que le clic droit
                break;
            case StatusItem.Mixer:
                HoverEnded?.Invoke(cell.Border);
                (Application.Current as App)?.OpenMixer();
                break;
        }
    }

    private static string VolumeGlyph(double level, bool muted) =>
        muted || level < 0.005 ? Icons.VolumeMute : level < 0.34 ? Icons.VolumeLow : level < 0.67 ? Icons.VolumeMedium : Icons.VolumeHigh;

    /// <summary>Clic sur la flèche : même réglage que « Cacher les icônes du bureau » (le service cache ou remet aussitôt).</summary>
    /// <remarks>Demande de l'utilisateur : la même flèche range aussi les icônes d'arrière-plan du dock (et les ressort).</remarks>
    private static void ToggleDesktopIcons() => SettingsStore.Update(s =>
    {
        s.Dock.HideDesktopIcons = !s.Dock.HideDesktopIcons;
        s.Dock.TrayCollapsed = s.Dock.HideDesktopIcons;
    });

    private ContextMenu Menu(string id)
    {
        var menu = new ContextMenu();
        switch (id)
        {
            case StatusItem.Keyboard:
                var current = KeyboardLayoutService.Current?.Handle;
                foreach (var layout in KeyboardLayoutService.Installed())
                {
                    var item = Entry(layout.Name, () => KeyboardLayoutService.Switch(layout));
                    item.IsCheckable = true;
                    item.IsChecked = layout.Handle == current;
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                menu.Items.Add(Entry(Loc.T("status.keyboard.settings"), () => OpenUri("ms-settings:regionlanguage")));
                break;
            case StatusItem.Updates:
                var check = Entry(Loc.T("status.updates.check"), () => _ = WindowsUpdateService.CheckAsync(online: true));
                check.IsEnabled = !WindowsUpdateService.Checking;
                menu.Items.Add(check);
                menu.Items.Add(Entry(Loc.T("status.updates.open"), WindowsUpdateService.OpenSettings));
                if (WindowsUpdateService.Pending.Count > 0 || WindowsUpdateService.RestartRequired)
                {
                    // Correctif 21 : seulement au clic, avec confirmation (fenêtre administrateur s'il faut encore installer).
                    menu.Items.Add(new Separator());
                    var install = WindowsUpdateService.Pending.Count > 0;
                    var restartItem = Entry(Loc.T(install ? "updates.win.install.restart" : "updates.win.restart"), () => Later(() => WindowsUpdateInstall.Start(restart: true)), Icons.Restart);
                    var shutdownItem = Entry(Loc.T(install ? "updates.win.install.shutdown" : "updates.win.shutdown"), () => Later(() => WindowsUpdateInstall.Start(restart: false)), Icons.Power);
                    restartItem.IsEnabled = shutdownItem.IsEnabled = !WindowsUpdateInstall.Running;
                    menu.Items.Add(restartItem);
                    menu.Items.Add(shutdownItem);
                }
                break;
            case StatusItem.Trash:
                menu.Items.Add(Entry(Loc.T("status.trash.open"), RecycleBinService.Open));
                var empty = Entry(Loc.T("status.trash.empty_action"), EmptyTrash);
                empty.IsEnabled = RecycleBinService.Count > 0;
                menu.Items.Add(empty);
                break;
            case StatusItem.Downloads:
                menu.Items.Add(Entry(Loc.T("status.downloads.open"), () => DownloadsService.Open(DownloadsService.Folder)));
                break;
            case StatusItem.Desktop:
                menu.Items.Add(Entry(Loc.T(SettingsStore.Current.Dock.HideDesktopIcons ? "status.desktop.show" : "status.desktop.hide"), ToggleDesktopIcons));
                break;
            case StatusItem.Power:
                // Par le Dispatcher : le menu est refermé avant la question de confirmation.
                menu.Items.Add(Entry(Loc.T("menubar.lock"), () => Later(PowerActions.Lock), Icons.Lock));
                menu.Items.Add(Entry(Loc.T("menubar.sleep") + "…", () => Later(PowerActions.Sleep), Icons.WeatherMoon));
                menu.Items.Add(Entry(Loc.T("menubar.restart"), () => Later(PowerActions.Restart), Icons.Restart));
                menu.Items.Add(Entry(Loc.T("menubar.shutdown"), () => Later(PowerActions.ShutDown), Icons.Power));
                break;
            case StatusItem.Mixer:
                menu.Items.Add(Entry(Loc.T("status.mixer.open"), () => (Application.Current as App)?.OpenMixer()));
                menu.Items.Add(Entry(_volume.TryRead(out _, out bool silent) && silent ? Loc.T("status.mixer.unmute") : Loc.T("status.mixer.mute"), _volume.ToggleMute));
                menu.Items.Add(Entry(Loc.T("status.mixer.windows"), () => OpenUri("ms-settings:apps-volume")));
                break;
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("status.settings"), () => SettingsWindow.Open(SettingsPage.Dock)));
        return menu;
    }

    /// <summary>« Vider la corbeille… » : définitif, donc toujours après confirmation.</summary>
    private void EmptyTrash()
    {
        var answer = MessageBox.Show(Loc.F(RecycleBinService.Count == 1 ? "status.trash.confirm.one" : "status.trash.confirm.many", RecycleBinService.Count),
            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK) RecycleBinService.Empty(OwnerHandle());
    }

    // ------------------------------------------------------------------ corbeille : glisser-déposer

    private void TrashDragOver(Cell cell, DragEventArgs e)
    {
        // Seulement des fichiers, et seulement si la source accepte qu'on les déplace (l'étagère de l'île, elle, protège ses originaux).
        bool accepted = e.Data.GetDataPresent(DataFormats.FileDrop) && (e.AllowedEffects & DragDropEffects.Move) != 0;
        e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        if (accepted)
        {
            cell.Border.Background = HoverBrush;
            Animate(cell.Scale, 1.18);
        }
        e.Handled = true;
    }

    private void TrashDrop(Cell cell, DragEventArgs e)
    {
        cell.Border.Background = IdleBrush;
        Animate(cell.Scale, 1);
        e.Handled = true;
        if ((e.AllowedEffects & DragDropEffects.Move) == 0 || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        e.Effects = DragDropEffects.Move;
        // Après la fin du glisser : Windows peut afficher une question (élément trop gros pour la corbeille…).
        Dispatcher.BeginInvoke(() =>
        {
            if (!RecycleBinService.Recycle(paths, OwnerHandle())) return;
            Poof(cell);
            var text = paths.Length == 1 ? Loc.F("status.trash.recycled.one", System.IO.Path.GetFileName(paths[0].TrimEnd('\\'))) : Loc.F("status.trash.recycled.many", paths.Length);
            (Application.Current as App)?.Notify(Icons.Trash, text, RecycleBinService.Open);
        });
    }

    /// <summary>Petit nuage qui s'échappe de la corbeille (effet « poof »).</summary>
    private static void Poof(Cell cell)
    {
        Animate(cell.Scale, 0.85, 1);
        const int puffs = 7;
        for (int i = 0; i < puffs; i++)
        {
            double angle = -Math.PI / 2 + (i - (puffs - 1) / 2.0) * 0.45;
            double size = DockShape.StatusDot * (1.4 + i % 3 * 0.4);
            var move = new TranslateTransform(-size / 2, -size / 2);
            var grow = new ScaleTransform(0.4, 0.4);
            var puff = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Paint.TextSecondary,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { grow, move } },
            };
            cell.Effects.Children.Add(puff);
            var duration = new Duration(Motion.Scale);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-size / 2 + Math.Cos(angle) * DockShape.PoofDistance, duration) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-size / 2 + Math.Sin(angle) * DockShape.PoofDistance, duration) { EasingFunction = ease });
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.3, duration));
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.3, duration));
            var fade = new DoubleAnimation(0.8, 0, duration);
            fade.Completed += (_, _) => cell.Effects.Children.Remove(puff);
            puff.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Nouvelle langue du clavier : le texte rebondit.</summary>
    private static void Pop(Cell cell) => Animate(cell.Scale, 0.6, 1, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });

    /// <summary>
    /// Flèche des icônes du bureau : vers le bas = les ranger, vers le haut = les ressortir. Au changement elle pivote d'un
    /// demi-tour en glissant un peu, la case rebondit et de petites icônes rentrent dans la case (ou en ressortent).
    /// </summary>
    private void TurnArrow(Cell cell, bool hidden, bool animate)
    {
        double angle = hidden ? 180 : 0;
        if (!animate)
        {
            _arrowTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            _arrowTurn.Angle = angle;
            return;
        }
        var duration = new Duration(Motion.Scale);
        _arrowTurn.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, duration) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
        double slide = hidden ? DockShape.DesktopArrowSlide : -DockShape.DesktopArrowSlide; // dans le sens où partent les icônes
        var glide = new DoubleAnimationUsingKeyFrames { Duration = duration };
        glide.KeyFrames.Add(new EasingDoubleKeyFrame(slide, KeyTime.FromPercent(0.4), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        glide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        _arrowSlide.BeginAnimation(TranslateTransform.YProperty, glide);
        Animate(cell.Scale, 0.82, 1, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut });
        Tuck(cell, hidden);
    }

    /// <summary>Trois mini-icônes descendent du bureau dans la case (icônes cachées) ou en remontent (icônes remises).</summary>
    private static void Tuck(Cell cell, bool hiding)
    {
        const int count = 3;
        double size = DockShape.DesktopTuckDot;
        double desk = (DockIconView.AtTop ? 1 : -1) * DockShape.DesktopTuckDistance; // côté bureau (au-dessus de la case, ou dessous si le dock est en haut)
        var duration = new Duration(Motion.Scale);
        for (int i = 0; i < count; i++)
        {
            double x = (i - (count - 1) / 2.0) * size * 1.6 - size / 2;
            var move = new TranslateTransform(x, (hiding ? desk : 0) - size / 2);
            var icon = new SquircleBorder
            {
                Width = size,
                Height = size,
                CornerRadius = Corners.Marker,
                Background = Paint.TextSecondary,
                RenderTransform = move,
                Opacity = 0,
            };
            cell.Effects.Children.Add(icon);
            var delay = TimeSpan.FromTicks(Motion.DelayShort.Ticks * i); // l'une après l'autre
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation((hiding ? 0 : desk) - size / 2, duration)
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = hiding ? EasingMode.EaseIn : EasingMode.EaseOut },
            });
            var fade = new DoubleAnimationUsingKeyFrames { Duration = duration, BeginTime = delay };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(hiding ? 0.9 : 0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(hiding ? 0.3 : 0.35)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            fade.Completed += (_, _) => cell.Effects.Children.Remove(icon);
            icon.BeginAnimation(OpacityProperty, fade);
        }
    }

    private static void Animate(ScaleTransform scale, double to) => Animate(scale, null, to);

    private static void Animate(ScaleTransform scale, double? from, double to, IEasingFunction? ease = null)
    {
        var animation = new DoubleAnimation(to, new Duration(Motion.Long)) { EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (from is { } start) animation.From = start;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private IntPtr OwnerHandle() => Window.GetWindow(this) is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;

    private static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();

    private void Later(Action action) => Dispatcher.BeginInvoke(action);

    private static MenuItem Entry(string header, Action action, string? glyph = null)
    {
        var entry = new MenuItem { Header = header };
        if (glyph != null) entry.Icon = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.S, Foreground = Paint.TextSecondary };
        entry.Click += (_, _) => action();
        return entry;
    }
}
