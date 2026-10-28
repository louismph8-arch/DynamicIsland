using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;
using NotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace DynamicIsland.Dock;

/// <summary>
/// Icônes d'arrière-plan (zone de notification) à droite du dock, sur deux lignes.
/// Survol, clics gauche/droit/double-clic sont transmis à l'appli propriétaire de l'icône.
/// </summary>
public sealed class TrayArea : WrapPanel
{
    public const double CellSize = DockShape.TrayCell;

    private static readonly Brush IdleBrush = Paint.HitTarget; // alpha 1 : capte la souris
    private static readonly Brush HoverBrush = Paint.Selected;

    private ObservableCollection<NotifyIcon>? _icons;

    public TrayArea()
    {
        Orientation = Orientation.Vertical; // remplit colonne par colonne (2 icônes par colonne)
        Height = 2 * CellSize;
        Width = 0;
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    /// <summary>Survol d'une icône (élément à viser pour l'étiquette, nom de l'appli).</summary>
    public event Action<FrameworkElement, string>? HoverStarted;
    public event Action<FrameworkElement>? HoverEnded;

    /// <summary>Le nombre d'icônes visibles a changé (le dock doit se remettre en page).</summary>
    public event Action? CountChanged;

    public int VisibleCount => Children.Count;

    public void Attach(ObservableCollection<NotifyIcon> icons)
    {
        _icons = icons;
        icons.CollectionChanged += OnCollectionChanged;
        foreach (var icon in icons) icon.PropertyChanged += OnIconPropertyChanged;
        Rebuild();
    }

    public void Detach()
    {
        if (_icons == null) return;
        _icons.CollectionChanged -= OnCollectionChanged;
        foreach (var icon in _icons) icon.PropertyChanged -= OnIconPropertyChanged;
        _icons = null;
        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (NotifyIcon icon in e.OldItems) icon.PropertyChanged -= OnIconPropertyChanged;
        if (e.NewItems != null)
            foreach (NotifyIcon icon in e.NewItems) icon.PropertyChanged += OnIconPropertyChanged;
        Rebuild();
    }

    private void OnIconPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotifyIcon.IsHidden)) Rebuild();
    }

    private void Rebuild()
    {
        int before = Children.Count;
        Children.Clear();
        if (_icons != null)
            foreach (var icon in _icons.Where(i => !i.IsHidden)) Children.Add(CreateCell(icon));

        int columns = (Children.Count + 1) / 2;
        Width = columns * CellSize;
        if (Children.Count != before) CountChanged?.Invoke();
    }

    private SquircleBorder CreateCell(NotifyIcon icon)
    {
        var image = new Image { Width = DockShape.TrayIcon, Height = DockShape.TrayIcon };
        image.SetBinding(Image.SourceProperty, new Binding(nameof(NotifyIcon.Icon)) { Source = icon });
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var cell = new SquircleBorder
        {
            Width = CellSize,
            Height = CellSize,
            CornerRadius = Corners.XS,
            Background = IdleBrush,
            Child = image,
        };

        cell.MouseEnter += (_, _) =>
        {
            cell.Background = HoverBrush;
            UpdatePlacement(cell, icon);
            icon.IconMouseEnter(CursorParam());
            HoverStarted?.Invoke(cell, string.IsNullOrWhiteSpace(icon.Title) ? Loc.T("settings.icone") : icon.Title);
        };
        cell.MouseLeave += (_, _) =>
        {
            cell.Background = IdleBrush;
            icon.IconMouseLeave(CursorParam());
            HoverEnded?.Invoke(cell);
        };
        cell.MouseMove += (_, _) => icon.IconMouseMove(CursorParam());
        cell.MouseDown += (_, e) =>
        {
            UpdatePlacement(cell, icon); // l'appli place son menu / sa fenêtre près de l'icône
            icon.IconMouseDown(e.ChangedButton, CursorParam(), GetDoubleClickTime());
            e.Handled = true;
        };
        cell.MouseUp += (_, e) =>
        {
            icon.IconMouseUp(e.ChangedButton, CursorParam(), GetDoubleClickTime());
            e.Handled = true;
        };
        return cell;
    }

    /// <summary>Position de l'icône à l'écran (pixels), renvoyée aux applis qui la demandent.</summary>
    private static void UpdatePlacement(FrameworkElement cell, NotifyIcon icon)
    {
        if (PresentationSource.FromVisual(cell) == null) return;
        var topLeft = cell.PointToScreen(new Point(0, 0));
        var bottomRight = cell.PointToScreen(new Point(cell.ActualWidth, cell.ActualHeight));
        icon.Placement = new ManagedShell.Interop.NativeMethods.Rect(
            (int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }

    /// <summary>Position du curseur au format attendu par les icônes (x dans le mot bas, y dans le mot haut).</summary>
    private static uint CursorParam()
    {
        GetCursorPos(out var point);
        return ((uint)(ushort)point.Y << 16) | (ushort)point.X;
    }

    /// <summary>Vrai si <paramref name="source"/> est dans la zone (utile pour ignorer le menu du dock).</summary>
    public bool Contains(DependencyObject? source)
    {
        while (source != null)
        {
            if (source == this) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

}
