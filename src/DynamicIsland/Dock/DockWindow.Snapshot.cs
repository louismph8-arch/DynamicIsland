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
        var crop = IsBar
            ? new Rect(0, 0, ActualWidth, ActualHeight) // la barre prend toute la largeur
            : new Rect(Math.Max(0, ActualWidth / 2 - 450), 0, Math.Min(900, ActualWidth), ActualHeight);

        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "4-dock.png"), crop);

        // Survol simulé de la 2e icône : grossissement + étiquette du nom.
        if (ItemsPanel.Children.OfType<DockIconView>().Skip(1).FirstOrDefault() is { } target)
        {
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(target);
            _pointerX = ItemsPanel.TranslatePoint(new Point(slot.Left + slot.Width / 2, 0), Root).X;
            UpdateMagnification();
            FinishAnimations();
            SetHovered(target);
            NameLabel.BeginAnimation(OpacityProperty, null);
            NameLabel.Opacity = 1;
            UpdateLayout();
            PlaceLabel();
            await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "5-dock-survol.png"), crop);

            // Verre liquide : lumière (curseur simulé) juste au-dessus du dock — en barre, au-dessus d'une zone sans
            // icône (le verre est sous les icônes) ; en dock flottant, au-dessus de l'icône survolée.
            var top = target.TranslatePoint(new Point(target.ActualWidth / 2, 0), DockBackground); // l'icône n'est pas dans le fond
            _glass.SimulateCursor(new Point(IsBar ? DockBackground.ActualWidth * 0.18 : top.X, -target.ActualHeight / 3));
            await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "5b-dock-reflet.png"), crop);
            _glass.SimulateCursor(null);
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
