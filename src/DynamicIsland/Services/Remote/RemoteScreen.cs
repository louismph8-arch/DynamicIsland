using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Aperçu de l'écran sur le téléphone (6D-1c) : une capture prise À LA DEMANDE (contrôle complet seulement), réduite à
/// 1280 px de large et envoyée en JPEG ; rien n'est enregistré sur le disque. Les écrans sont rangés de gauche à droite.
/// Un jeu en plein écran exclusif peut apparaître noir.
/// </summary>
public static class RemoteScreen
{
    private const int MaxWidth = 1280;
    private const long Quality = 70;

    /// <summary>Écrans de gauche à droite (en pixels réels).</summary>
    public static IReadOnlyList<Rectangle> Screens =>
        Forms.Screen.AllScreens.Select(s => s.Bounds).OrderBy(b => b.Left).ThenBy(b => b.Top).ToList();

    /// <summary>Numéro de l'écran principal dans <see cref="Screens"/>.</summary>
    public static int Primary
    {
        get
        {
            var primary = Forms.Screen.PrimaryScreen?.Bounds;
            var screens = Screens;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i] == primary) return i;
            return 0;
        }
    }

    public static byte[] Capture(int index)
    {
        var screens = Screens;
        var bounds = screens[index >= 0 && index < screens.Count ? index : Primary];
        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(full))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

        double scale = Math.Min(1, (double)MaxWidth / bounds.Width);
        using var small = new Bitmap(Math.Max(1, (int)(bounds.Width * scale)), Math.Max(1, (int)(bounds.Height * scale)), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(full, 0, 0, small.Width, small.Height);
        }
        return Jpeg(small);
    }

    internal static byte[] Jpeg(Bitmap image)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Quality);
        using var stream = new MemoryStream();
        image.Save(stream, codec, parameters);
        return stream.ToArray();
    }
}
