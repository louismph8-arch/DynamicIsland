using System.IO;
using System.Windows;

namespace DynamicIsland.Dock;

/// <summary>Mode développeur --snapshot : rend le dock au repos puis survolé, hors écran, sans toucher au bureau.</summary>
public partial class DockWindow
{
    public string? SnapshotFolder { get; init; }

    /// <summary>--glass-bench : fenêtre hors écran comme pour --snapshot, mais sans les rendus.</summary>
    public bool GlassBenchOnly { get; init; }
    internal Controls.LiquidGlass BenchGlass => _glass;
    internal FrameworkElement BenchGlassSurface => DockBackground;

    private async Task RunSnapshotAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        AttachDemoTray();
        await Task.Delay(1200); // laisse le suivi des fenêtres et les icônes se charger
        FinishAnimations();
        UpdateLayout();
        // Dock flottant : au moins 900 de large autour du dock (centré, ou posé contre un côté de l'écran), jamais coupé.
        double dockLeft = DockRoot.TranslatePoint(new Point(0, 0), Root).X;
        double cropWidth = Math.Min(ActualWidth, Math.Max(900, DockRoot.ActualWidth + 120));
        var crop = IsBar
            ? new Rect(0, 0, ActualWidth, ActualHeight) // la barre prend toute la largeur
            : new Rect(Math.Clamp(dockLeft + DockRoot.ActualWidth / 2 - cropWidth / 2, 0, Math.Max(0, ActualWidth - cropWidth)), 0, cropWidth, ActualHeight);

        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "4-dock.png"), crop);

        // Survol simulé de la 2e icône : grossissement + étiquette du nom.
        if (ItemsPanel.Children.OfType<DockIconView>().Skip(1).FirstOrDefault() is { } target)
        {
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(target);
            _pointerX = ItemsPanel.TranslatePoint(new Point(slot.Left + slot.Width / 2, 0), Root).X;
            UpdateMagnification();
            FinishAnimations();
            // Souris d'exemple au milieu de l'icône telle qu'on la voit une fois grossie (aligné à gauche / à droite, les icônes
            // grossies avant elle la décalent) : quelques allers-retours suffisent.
            for (int i = 0; i < 4; i++)
            {
                UpdateLayout();
                var shown = target.IconBounds(Root);
                _pointerX = shown.Left + shown.Width / 2;
                UpdateMagnification();
                FinishAnimations();
            }
            SetHovered(target);
            NameLabel.BeginAnimation(OpacityProperty, null);
            NameLabel.Opacity = 1;
            UpdateLayout();
            PlaceLabel();
            await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "5-dock-survol.png"), crop);
            await SavePointerCheckAsync(target, Path.Combine(folder, "5d-dock-souris.png"), crop);

            // Verre liquide : lumière (curseur simulé) juste au-dessus du dock — en barre, au-dessus d'une zone sans
            // icône (le verre est sous les icônes) ; en dock flottant, au-dessus de l'icône survolée.
            var top = target.TranslatePoint(new Point(target.ActualWidth / 2, 0), DockBackground); // l'icône n'est pas dans le fond
            _glass.SimulateCursor(new Point(IsBar ? DockBackground.ActualWidth * 0.18 : top.X, -target.ActualHeight / 3));
            await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "5b-dock-reflet.png"), crop);
            _glass.SimulateCursor(null);
        }

        // Aperçu des fenêtres : trois fenêtres d'exemple (16:9, 4:3, portrait), la dernière survolée (un seul cadre, « × »
        // réglable), puis une seule fenêtre (la miniature remplit la carte, pas de cadre de carte). Fausses miniatures dessinées.
        if (_pinned.FirstOrDefault(i => !i.IsFolder) is { Pin: { } pin })
        {
            await PreviewWindow.RenderSnapshotAsync(DockItem.FromPin(pin), Path.Combine(folder, "5c-apercu.png"));
            await PreviewWindow.RenderSnapshotAsync(DockItem.FromPin(pin), Path.Combine(folder, "5c-apercu-une-fenetre.png"), (1280, 860));
        }

        await SearchWindow.RenderSnapshotAsync(Path.Combine(folder, "6-recherche.png"), "chr");
        if (_pinned.FirstOrDefault(i => i.IsFolder) is { } dockFolder)
            await FolderWindow.RenderSnapshotAsync(dockFolder, Path.Combine(folder, "6b-dossier.png"));
        await DownloadsWindow.RenderSnapshotAsync(Path.Combine(folder, "6c-telechargements.png"));
        await Settings.SettingsWindow.RenderSnapshotsAsync(folder);
        Close();
    }

    /// <summary>
    /// Icônes d'arrière-plan factices pour le rendu : on n'héberge PAS la vraie zone de notification ici
    /// (elle appartient à l'app en cours d'exécution). Aucune fenêtre n'est créée sans Initialize().
    /// </summary>
    private void AttachDemoTray()
    {
        var area = new ManagedShell.WindowsTray.NotificationArea(
            new ManagedShell.WindowsTray.TrayService(), new ManagedShell.WindowsTray.ExplorerTrayService());
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var icons = new System.Collections.ObjectModel.ObservableCollection<ManagedShell.WindowsTray.NotifyIcon>();
        foreach (var (exe, title) in new[]
                 {
                     ("taskmgr.exe", "Démo 1"), ("notepad.exe", "Démo 2"), ("cmd.exe", "Démo 3"),
                     ("mspaint.exe", "Démo 4"), ("regedit.exe", "Démo 5"),
                 })
        {
            icons.Add(new ManagedShell.WindowsTray.NotifyIcon(area)
            {
                Icon = Services.IconService.Get(Path.Combine(system, exe)) ?? Services.IconService.Get(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), exe)),
                Title = title,
            });
        }
        AttachTray(icons);
    }

    /// <summary>
    /// Vérification du grossissement (correctif 15a) : un trait à la place de la souris d'exemple, et dans le journal de
    /// développement l'écart entre la souris et le milieu de l'icône la plus grosse (aligné à gauche / à droite compris).
    /// </summary>
    private async Task SavePointerCheckAsync(DockIconView hovered, string path, Rect crop)
    {
        if (_pointerX is not double pointer || NameLabel.Parent is not System.Windows.Controls.Canvas canvas) return;
        var biggest = _views.Values.Where(v => !v.IsRemoving).MaxBy(v => v.Scale.Value) ?? hovered;
        var bounds = biggest.IconBounds(Root);
        Log.Info($"Snapshot : dock {_settings.Layout}, {PlaceAlignment}, souris à {pointer:0.0}, icône la plus grosse ({biggest.Item.Name}, ×{biggest.Scale.Value:0.00}) "
                 + $"centrée à {bounds.Left + bounds.Width / 2:0.0} (écart {bounds.Left + bounds.Width / 2 - pointer:0.0}), survolée : {hovered.Item.Name}");
        var marker = new System.Windows.Shapes.Rectangle { Width = Themes.Stroke.Focus, Height = ActualHeight, Fill = Themes.Paint.Danger };
        System.Windows.Controls.Canvas.SetLeft(marker, pointer - Themes.Stroke.Focus / 2);
        canvas.Children.Add(marker);
        UpdateLayout();
        await SnapshotRenderer.SaveAsync(this, path, crop);
        canvas.Children.Remove(marker);
    }

    private void FinishAnimations()
    {
        _shift.Snap();
        DockShift.Y = _shift.Value;
        foreach (var view in _views.Values)
        {
            view.Scale.Snap();
            view.Appear.Snap();
            view.Apply();
            view.UpdateRunning();
        }
        StopAnimation();
    }
}
