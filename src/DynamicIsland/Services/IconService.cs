using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Interop;
using static DynamicIsland.Interop.ShellNative;

namespace DynamicIsland.Services;

/// <summary>
/// Icônes haute définition (avec transparence) d'un exe, d'un raccourci ou d'une appli du Store
/// (« shell:AppsFolder\&lt;AppUserModelID&gt; »), via IShellItemImageFactory. Résultats mis en cache.
/// </summary>
public static class IconService
{
    private const int IconSize = 128;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ImageSource?> Thumbnails = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string parsingName)
    {
        if (Cache.TryGetValue(parsingName, out var cached)) return cached;
        var icon = Load(parsingName, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK);
        Cache[parsingName] = icon;
        return icon;
    }

    /// <summary>Aperçu d'un fichier (miniature d'une image, d'un PDF…, sinon son icône), pour l'étagère.</summary>
    public static ImageSource? GetThumbnail(string path)
    {
        if (Thumbnails.TryGetValue(path, out var cached)) return cached;
        var image = Load(path, SIIGBF_BIGGERSIZEOK);
        Thumbnails[path] = image;
        return image;
    }

    private static ImageSource? Load(string parsingName, int flags)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
            var size = new DockNative.SIZE { cx = IconSize, cy = IconSize };
            if (factory.GetImage(size, flags, out bitmap) != 0) return null;
            return ToBitmapSource(bitmap);
        }
        catch (Exception ex)
        {
            Log.Error($"Icône de « {parsingName} »", ex);
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    /// <summary>Copie les pixels du HBITMAP (32 bits, alpha prémultiplié) sans perdre la transparence.</summary>
    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, System.Runtime.InteropServices.Marshal.SizeOf<BITMAP>(), out var info) == 0) return null;
        int width = info.bmWidth, height = info.bmHeight, stride = width * 4;

        var header = new BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // lignes de haut en bas
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[stride * height];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(screen, hBitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        // Certaines vieilles icônes n'ont pas de canal alpha : on les rend opaques.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
        if (!hasAlpha)
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
