using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>Icône d'un dossier du dock : squircle translucide avec les 4 premières applis en mosaïque.</summary>
internal static class FolderIcon
{
    private const int Size = 128;
    private const double Radius = 30; // pour une image de 128 px (= rayon d'une tuile de dossier à 48 px)
    private static readonly Pen Border = FrozenPen(Paint.SelectedStrong, Stroke.Focus);

    public static ImageSource Render(IEnumerable<ImageSource?> icons)
    {
        const double Margin = 6, Cell = 44, Gap = 10;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            var tile = new Rect(Margin, Margin, Size - 2 * Margin, Size - 2 * Margin);
            context.DrawGeometry(Paint.FolderTile, Border, SquircleGeometry.Create(tile, new CornerRadius(Radius), Themes.Radius.Smoothing));
            double start = (Size - (2 * Cell + Gap)) / 2;
            int index = 0;
            foreach (var icon in icons.Take(4))
            {
                if (icon != null)
                    context.DrawImage(icon, new Rect(start + index % 2 * (Cell + Gap), start + index / 2 * (Cell + Gap), Cell, Cell));
                index++;
            }
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
