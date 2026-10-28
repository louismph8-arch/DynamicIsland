using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>
/// Icônes harmonisées du dock (5G) : chaque icône posée au centre d'un même cadre squircle sombre et, en option,
/// redessinée d'une seule couleur — le dégradé d'accent, ombres et reflets gardés (luminosité de l'icône
/// d'origine). Le rendu est fait une fois par icône et par style, puis gardé tant que l'icône d'origine existe.
/// </summary>
public static class IconStyler
{
    private sealed record Styled(string Signature, ImageSource Image);
    private static readonly ConditionalWeakTable<ImageSource, Styled> Cache = [];

    /// <summary>Style actuel (change avec les réglages et la couleur d'accent) ; vide = icônes d'origine.</summary>
    public static string Signature
    {
        get
        {
            var dock = SettingsStore.Current.Dock;
            if (!dock.HarmonizedIcons && !dock.TintIcons) return "";
            return $"{dock.HarmonizedIcons}|{dock.TintIcons}|{AppearanceService.AccentStart}|{AppearanceService.AccentEnd}";
        }
    }

    public static ImageSource? Style(ImageSource? icon)
    {
        if (icon == null) return null;
        var signature = Signature;
        if (signature.Length == 0) return icon;
        if (Cache.TryGetValue(icon, out var styled) && styled.Signature == signature) return styled.Image;
        var dock = SettingsStore.Current.Dock;
        var image = Render(icon, dock.HarmonizedIcons, dock.TintIcons, AppearanceService.AccentStart, AppearanceService.AccentEnd);
        Cache.AddOrUpdate(icon, new Styled(signature, image));
        return image;
    }

    private static ImageSource Render(ImageSource icon, bool frame, bool tint, Color start, Color end)
    {
        const int Size = DockIconArt.Size;
        ImageSource glyph = tint ? Tinted(icon, start, end) : icon;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            if (frame)
            {
                var tile = new Rect(DockIconArt.TileInset, DockIconArt.TileInset, Size - 2 * DockIconArt.TileInset, Size - 2 * DockIconArt.TileInset);
                var shape = SquircleGeometry.Create(tile, new CornerRadius(tile.Width * DockIconArt.TileRadius), Radius.Smoothing);
                var fill = new LinearGradientBrush(DockIconArt.TileTop, DockIconArt.TileBottom, 90);
                fill.Freeze();
                dc.DrawGeometry(fill, new Pen(Paint.PopupBorder, Stroke.Selected), shape);
                double inset = Size * (1 - DockIconArt.GlyphScale) / 2;
                dc.DrawImage(glyph, new Rect(inset, inset, Size - 2 * inset, Size - 2 * inset));
            }
            else
            {
                dc.DrawImage(glyph, new Rect(0, 0, Size, Size));
            }
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>L'icône d'une seule couleur : dégradé d'accent (haut-gauche → bas-droite), plus clair là où l'icône l'est.</summary>
    private static BitmapSource Tinted(ImageSource icon, Color start, Color end)
    {
        const int Size = DockIconArt.Size;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(icon, new Rect(0, 0, Size, Size));
        var raster = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        raster.Render(visual);
        var pixels = new byte[Size * Size * 4];
        raster.CopyPixels(pixels, Size * 4, 0);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size + x) * 4;
                byte a = pixels[i + 3];
                if (a == 0) continue;
                // Luminosité de l'icône d'origine (couleurs pré-multipliées : on divise par l'alpha).
                double l = (0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]) / a;
                double t = (x + y) / (2.0 * Size);
                var accent = AppearanceService.Lerp(start, end, t);
                double light = Math.Clamp(DockIconArt.TintBase + (1 - DockIconArt.TintBase) * l, 0, 1);
                double toWhite = Math.Max(0, l - DockIconArt.TintHighlight) / (1 - DockIconArt.TintHighlight);
                double r = accent.R * light + (255 - accent.R * light) * toWhite * 0.6;
                double g = accent.G * light + (255 - accent.G * light) * toWhite * 0.6;
                double b = accent.B * light + (255 - accent.B * light) * toWhite * 0.6;
                pixels[i] = (byte)(b * a / 255);
                pixels[i + 1] = (byte)(g * a / 255);
                pixels[i + 2] = (byte)(r * a / 255);
            }
        }
        var tinted = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, pixels, Size * 4);
        tinted.Freeze();
        return tinted;
    }
}
