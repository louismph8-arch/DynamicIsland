using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>Mode développeur --snapshot : rend le contenu d'une fenêtre dans un PNG, sur un faux fond d'écran.</summary>
internal static class SnapshotRenderer
{
    /// <param name="crop">Zone à garder (en DIP), ou null pour toute la fenêtre.</param>
    public static async Task SaveAsync(Window window, string path, Rect? crop = null)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);

        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bounds = new Rect(0, 0, window.ActualWidth, window.ActualHeight);

        // Fond façon fond d'écran pour juger la translucidité et l'ombre.
        var backdrop = Themes.Paint.Gradient(Themes.Swatch.SnapshotSky, Themes.Swatch.SnapshotSand, 45);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(backdrop, null, bounds);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(bounds.Width * dpi.DpiScaleX), (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(root); // à l'échelle 1:1, par-dessus le fond

        BitmapSource result = bitmap;
        if (crop is { } area)
        {
            var pixels = new Int32Rect(
                (int)(area.X * dpi.DpiScaleX), (int)(area.Y * dpi.DpiScaleY),
                (int)(area.Width * dpi.DpiScaleX), (int)(area.Height * dpi.DpiScaleY));
            result = new CroppedBitmap(bitmap, pixels);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}
