using System.IO;
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
    // Caches bornés (fuite mesurée chez l'utilisateur : chaque appli vue dans la palette, la recherche, le mélangeur… et chaque
    // fichier de l'étagère restait en mémoire pour toujours, souvent en 256 px). Les icônes du dock sont tenues par le dock lui-même.
    private static readonly BoundedCache Cache = new(256);
    private static readonly BoundedCache Thumbnails = new(64);

    public static ImageSource? Get(string parsingName)
    {
        if (Cache.TryGet(parsingName, out var cached)) return cached;
        var icon = Load(parsingName, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK);
        Cache.Set(parsingName, icon);
        return icon;
    }

    /// <summary>Aperçu d'un fichier (miniature d'une image, d'un PDF…, sinon son icône), pour l'étagère.</summary>
    public static ImageSource? GetThumbnail(string path)
    {
        if (Thumbnails.TryGet(path, out var cached)) return cached;
        // Fichier OneDrive « en ligne seulement » : une miniature le ferait télécharger (île figée) → son icône seulement.
        var image = Load(path, IsOnlineOnly(path) ? SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK : SIIGBF_BIGGERSIZEOK);
        Thumbnails.Set(path, image);
        return image;
    }

    private static bool IsOnlineOnly(string path)
    {
        try
        {
            const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Offline | RecallOnDataAccess)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static ImageSource? Load(string parsingName, int flags)
    {
        IntPtr bitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);
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
            // Rendue tout de suite : sinon l'élément du Shell (et le fournisseur de miniatures) restent jusqu'au ramasse-miettes.
            if (factory != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(factory);
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
        if (Math.Max(width, height) > IconSize)
        {
            // Windows rend souvent du 256 px (ou plus pour une photo) : on ne garde que 128 px (4 fois moins de mémoire).
            double scale = (double)IconSize / Math.Max(width, height);
            var reduced = new WriteableBitmap(new TransformedBitmap(source, new ScaleTransform(scale, scale)));
            reduced.Freeze();
            return reduced;
        }
        source.Freeze();
        return source;
    }

    /// <summary>Cache borné : au-delà de sa taille, l'élément le moins récemment utilisé est oublié.</summary>
    private sealed class BoundedCache(int capacity)
    {
        private readonly Dictionary<string, LinkedListNode<(string Key, ImageSource? Value)>> _map = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<(string Key, ImageSource? Value)> _order = new();

        public bool TryGet(string key, out ImageSource? value)
        {
            lock (_order) return TryGetLocked(key, out value);
        }

        public void Set(string key, ImageSource? value)
        {
            lock (_order) SetLocked(key, value);
        }

        private bool TryGetLocked(string key, out ImageSource? value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = null;
            return false;
        }

        private void SetLocked(string key, ImageSource? value)
        {
            if (_map.TryGetValue(key, out var old)) _order.Remove(old);
            _map[key] = _order.AddFirst((key, value));
            while (_map.Count > capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
