using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Curseur de souris d'Opaline (5G, option) : formes originales dessinées par l'app — flèche au dégradé
/// d'accent liserée de blanc, la même avec une pastille pour les liens, barre de texte — écrites en vrais fichiers
/// .cur (plusieurs tailles : Windows prend celle de l'écran) puis déclarées à Windows comme le fait le panneau
/// « Souris ». Les autres curseurs (attente, redimensionnement…) restent ceux de Windows. Suit la couleur d'accent.
/// Réversible : les valeurs d'origine sont sauvées avant le premier changement (<see cref="WindowsBackup"/>, section
/// « Cursors ») et remises quand l'option est décochée ou que l'app est désinstallée.
/// </summary>
public static class CursorTheme
{
    public const string Section = "Cursors";
    private const string CursorsKey = @"Control Panel\Cursors";

    public enum Shape { Arrow, Hand, IBeam }

    /// <summary>Valeurs du registre touchées : nom du jeu de curseurs (valeur par défaut), les trois formes, l'origine du jeu.</summary>
    internal static readonly (string Key, string Name)[] Values =
        [(CursorsKey, ""), (CursorsKey, "Arrow"), (CursorsKey, "Hand"), (CursorsKey, "IBeam"), (CursorsKey, "Scheme Source")];

    private static string? _applied;

    /// <summary>Réglages changés : applique le curseur (si l'accent a changé) ou remet celui d'origine.</summary>
    public static void Apply()
    {
        if (SettingsStore.IsReadOnly || ModuleGuard.IsDisabled(ModuleGuard.Colors)) return; // --snapshot / module arrêté
        try
        {
            if (SettingsStore.Current.Appearance.ThemedCursor)
            {
                var signature = $"{AppearanceService.AccentStart}|{AppearanceService.AccentEnd}";
                if (_applied == signature) return;
                Install(Path.Combine(AppPaths.DataFolder, "Curseurs"), AppearanceService.AccentStart, AppearanceService.AccentEnd, reload: true);
                _applied = signature;
            }
            else if (WindowsBackup.Restore(Section))
            {
                _applied = null;
                Reload();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Curseur de souris", ex);
        }
    }

    /// <summary>Écrit les .cur (noms nouveaux à chaque couleur : Windows garde en mémoire un fichier déjà lu) et les déclare.</summary>
    internal static void Install(string folder, Color start, Color end, bool reload)
    {
        WindowsBackup.SaveIfMissing(Section, Values);
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.cur"))
        {
            try { File.Delete(old); }
            catch (IOException) { } // encore utilisé : on le laisse
            catch (UnauthorizedAccessException) { }
        }
        string tag = $"{start.R:X2}{start.G:X2}{start.B:X2}{end.R:X2}{end.G:X2}{end.B:X2}";
        using var key = WindowsBackup.Root.CreateSubKey(CursorsKey);
        foreach (var (shape, name, file) in new[] { (Shape.Arrow, "Arrow", "fleche"), (Shape.Hand, "Hand", "lien"), (Shape.IBeam, "IBeam", "texte") })
        {
            var path = Path.Combine(folder, $"{file}-{tag}.cur");
            WriteCursor(path, shape, start, end);
            key.SetValue(name, path, RegistryValueKind.ExpandString);
        }
        key.SetValue("", AppInfo.Name, RegistryValueKind.String);
        key.SetValue("Scheme Source", 1, RegistryValueKind.DWord); // jeu « personnalisé »
        if (reload) Reload();
    }

    /// <summary>Demande à Windows de relire les curseurs du registre.</summary>
    public static void Reload() => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    // ------------------------------------------------------------------ dessin

    /// <summary>Aperçu (paramètres, --snapshot).</summary>
    public static BitmapSource Render(Shape shape, Color start, Color end, int size)
    {
        var visual = new DrawingVisual();
        double k = size / 32.0;
        var (geometry, _) = ShapeOf(shape);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            if (shape == Shape.IBeam)
            {
                // Barre de texte : trait blanc sur un contour sombre (lisible sur fond clair comme sombre).
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x14)), 4.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
                dc.DrawGeometry(null, new Pen(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
                var accent = new SolidColorBrush(AppearanceService.Lerp(start, end, 0.5));
                dc.DrawEllipse(accent, null, new Point(16, 16), 1.1, 1.1);
            }
            else
            {
                var fill = new LinearGradientBrush(start, end, new Point(0, 0), new Point(0.6, 1));
                var outline = new Pen(Brushes.White, CursorArt.Outline) { LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(fill, outline, geometry);
                if (shape == Shape.Hand)
                {
                    // Pastille « lien » : rond d'accent liseré de blanc, avec un point blanc.
                    dc.DrawEllipse(new SolidColorBrush(end), new Pen(Brushes.White, CursorArt.Outline), new Point(22.5, 22.5), 5.2, 5.2);
                    dc.DrawEllipse(Brushes.White, null, new Point(22.5, 22.5), 1.6, 1.6);
                }
            }
            dc.Pop();
        }
        if (shape != Shape.IBeam)
            visual.Effect = new DropShadowEffect { BlurRadius = CursorArt.ShadowBlur * k, ShadowDepth = CursorArt.ShadowDepth * k, Opacity = CursorArt.ShadowOpacity, Direction = 300 };
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Formes sur une grille de 32 ; le point actif est le bout de la flèche (centre pour le texte).</summary>
    private static (System.Windows.Media.Geometry Path, Point Hotspot) ShapeOf(Shape shape)
    {
        if (shape == Shape.IBeam)
        {
            var beam = System.Windows.Media.Geometry.Parse("M 12.5,6 C 14.5,6 16,7 16,9 L 16,23 C 16,25 14.5,26 12.5,26 M 19.5,6 C 17.5,6 16,7 16,9 M 16,23 C 16,25 17.5,26 19.5,26");
            return (beam, new Point(16, 16));
        }
        // Flèche aux angles adoucis : pointe en (3,3), queue légèrement inclinée.
        var arrow = System.Windows.Media.Geometry.Parse("M 3,3 L 3,22.5 L 8,18 L 11.5,26 L 15,24.5 L 11.6,16.8 L 18.5,16.8 Z");
        return (arrow, new Point(3, 3));
    }

    // ------------------------------------------------------------------ fichier .cur

    /// <summary>Fichier .cur à plusieurs tailles (images 32 bits BGRA + masque, format classique de Windows).</summary>
    internal static void WriteCursor(string path, Shape shape, Color start, Color end)
    {
        var (_, hotspot) = ShapeOf(shape);
        var images = CursorArt.Sizes.Select(size => (Size: size, Data: Dib(Render(shape, start, end, size)))).ToList();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);           // réservé
        writer.Write((ushort)2);           // type : curseur
        writer.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);         // couleurs de palette
            writer.Write((byte)0);
            writer.Write((ushort)Math.Round(hotspot.X * size / 32.0));
            writer.Write((ushort)Math.Round(hotspot.Y * size / 32.0));
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) writer.Write(data);
    }

    /// <summary>
    /// Image d'un curseur ou d'une icône : BITMAPINFOHEADER, pixels BGRA de bas en haut (alpha non pré-multiplié), masque.
    /// Aussi utilisée pour l'icône de l'app (<see cref="Themes.OpalineLogo"/>).
    /// </summary>
    internal static byte[] Dib(BitmapSource image)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        int maskStride = (w + 31) / 32 * 4;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write(40);                  // taille de l'en-tête
        writer.Write(w);
        writer.Write(h * 2);               // image + masque
        writer.Write((ushort)1);           // plans
        writer.Write((ushort)32);          // bits par pixel
        writer.Write(0);                   // pas de compression
        writer.Write(w * h * 4 + maskStride * h);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte a = pixels[i + 3];
                for (int c = 0; c < 3; c++) writer.Write(a == 0 ? (byte)0 : (byte)Math.Min(255, pixels[i + c] * 255 / a));
                writer.Write(a);
            }
        }
        // Masque ET : 1 = transparent (pour les programmes qui ignorent l'alpha).
        for (int y = h - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (int x = 0; x < w; x++)
                if (pixels[(y * w + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(row);
        }
        return memory.ToArray();
    }

    private const uint SPI_SETCURSORS = 0x0057, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
}
