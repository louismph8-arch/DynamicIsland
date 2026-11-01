using System.Windows;
using DynamicIsland.Themes;

namespace DynamicIsland.Dock;

/// <summary>
/// Calculs purs de l'aperçu des fenêtres (testés par --self-test) : taille de la zone de la miniature (en DIP) d'après le
/// format de la fenêtre, et rectangle où DWM la dessine (en pixels physiques, ce qu'attend DwmUpdateThumbnailProperties).
/// La zone a le format de la fenêtre dans les bornes de <see cref="DockShape"/> : la miniature la remplit entièrement ; il
/// n'y a des bandes vides que si une borne mini joue (fenêtre très haute ou très large).
/// </summary>
internal static class PreviewLayout
{
    /// <summary>Format par défaut quand Windows ne donne aucune taille utilisable.</summary>
    private const double DefaultAspect = 16.0 / 10;

    /// <summary>
    /// Format de la fenêtre (largeur / hauteur) : taille donnée par DWM, sinon le rectangle de la fenêtre. Une hauteur de
    /// quelques dizaines de pixels = la petite barre d'une fenêtre réduite (pas son image) → on prend sa place normale.
    /// </summary>
    public static double Aspect(int thumbWidth, int thumbHeight, int normalWidth = 0, int normalHeight = 0)
    {
        if (thumbWidth > 0 && thumbHeight >= DockShape.PreviewThinSource) return (double)thumbWidth / thumbHeight;
        if (normalWidth > 0 && normalHeight > 0) return (double)normalWidth / normalHeight;
        if (thumbWidth > 0 && thumbHeight > 0) return (double)thumbWidth / thumbHeight;
        return DefaultAspect;
    }

    /// <summary>Taille de la zone de la miniature, en DIP : le plus grand rectangle au format de la fenêtre dans les bornes.</summary>
    public static Size Area(double aspect)
    {
        if (!(aspect > 0) || double.IsInfinity(aspect)) aspect = DefaultAspect;
        double width = DockShape.PreviewThumbMaxWidth, height = width / aspect;
        if (height > DockShape.PreviewThumbMaxHeight)
        {
            height = DockShape.PreviewThumbMaxHeight;
            width = height * aspect;
        }
        width = Math.Max(Math.Round(width), DockShape.PreviewThumbMinWidth);
        height = Math.Max(Math.Round(height), DockShape.PreviewThumbMinHeight);
        return new Size(width, height);
    }

    /// <summary>
    /// Rectangle de la miniature dans la zone <paramref name="areaPx"/> (pixels physiques, relatifs à la fenêtre de l'aperçu) :
    /// le plus grand au format de la fenêtre, centré, arrondi au pixel sans jamais dépasser la zone.
    /// </summary>
    public static Int32Rect Destination(Rect areaPx, double aspect)
    {
        if (!(aspect > 0) || double.IsInfinity(aspect)) aspect = DefaultAspect;
        int left = (int)Math.Ceiling(areaPx.Left - 0.01), top = (int)Math.Ceiling(areaPx.Top - 0.01);
        int right = (int)Math.Floor(areaPx.Right + 0.01), bottom = (int)Math.Floor(areaPx.Bottom + 0.01);
        int boxWidth = Math.Max(1, right - left), boxHeight = Math.Max(1, bottom - top);

        int width = boxWidth, height = (int)Math.Round(boxWidth / aspect);
        if (height > boxHeight)
        {
            height = boxHeight;
            width = Math.Min(boxWidth, (int)Math.Round(boxHeight * aspect));
        }
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return new Int32Rect(left + (boxWidth - width) / 2, top + (boxHeight - height) / 2, width, height);
    }
}
