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
/// Icônes d'arrière-plan (zone de notification) à droite du dock, sur deux lignes (une seule si la barre est trop fine).
/// Survol, clics gauche/droit/double-clic sont transmis à l'appli propriétaire de l'icône.
/// </summary>
public sealed class TrayArea : WrapPanel
{
    private static readonly Brush IdleBrush = Paint.HitTarget; // alpha 1 : capte la souris
    private static readonly Brush HoverBrush = Paint.Selected;

    private ObservableCollection<NotifyIcon>? _icons;
    private double _cell = DockShape.TrayCell;
    private int _rows = 2;

    public TrayArea()
    {
        Orientation = Orientation.Vertical; // remplit colonne par colonne (2 icônes par colonne)
        Height = _rows * _cell;
        Width = 0;
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    /// <summary>
    /// Lignes et taille des cases pour une barre de cette hauteur (correctif 15b) : deux lignes tant que des cases d'au moins
    /// 18 tiennent, sinon une seule ; les icônes restent à 16 px (taille de Windows). Calcul pur, vérifié par --self-test.
    /// </summary>
    internal static (int Rows, double Cell) LayoutFor(double height)
    {
        double two = Math.Min(DockShape.TrayCell, Math.Floor(height / 2));
        if (two >= DockShape.TrayCellMin) return (2, two);
        return (1, Math.Clamp(Math.Floor(height), DockShape.TrayCellMin, DockShape.TrayCell));
    }

    /// <summary>Taille d'après la hauteur du fond du dock.</summary>
    public void ApplySize(double height)
    {
        var (rows, cell) = LayoutFor(height);
        if (rows == _rows && cell == _cell) return;
        _rows = rows;
        _cell = cell;
        Height = rows * cell;
        foreach (var child in Children.OfType<FrameworkElement>()) child.Width = child.Height = cell;
        UpdateWidth();
    }

    private void UpdateWidth() => Width = (Children.Count + _rows - 1) / _rows * _cell;

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

        UpdateWidth();
        if (Children.Count != before) CountChanged?.Invoke();
    }

    private SquircleBorder CreateCell(NotifyIcon icon)
    {
        var image = new Image { Width = DockShape.TrayIcon, Height = DockShape.TrayIcon };
        image.SetBinding(Image.SourceProperty, new Binding(nameof(NotifyIcon.Icon)) { Source = icon });
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var cell = new SquircleBorder
        {
            Width = _cell,
            Height = _cell,
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
