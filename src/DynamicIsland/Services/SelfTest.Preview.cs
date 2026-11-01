using System.Windows;
using DynamicIsland.Dock;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de l'aperçu des fenêtres du dock (après la 1.35.0) : la miniature DWM remplit la carte quel que soit le format
/// de la fenêtre et le DPI de l'écran. Calculs purs : aucune fenêtre ni miniature.
/// </summary>
internal static partial class SelfTest
{
    private static void PreviewThumbnails(List<string> lines)
    {
        var formats = new (string Name, int Width, int Height)[]
        {
            ("16:9", 1920, 1080), ("16:10", 1280, 800), ("4:3", 1024, 768), ("portrait", 1080, 1920),
            ("très large", 3840, 1080), ("carré", 800, 800), ("très haute", 400, 1600),
        };
        int checks = 0;
        var problems = new List<string>();
        foreach (var (name, w, h) in formats)
        {
            double aspect = PreviewLayout.Aspect(w, h);
            var area = PreviewLayout.Area(aspect);
            bool clamped = area.Width <= DockShape.PreviewThumbMinWidth || area.Height <= DockShape.PreviewThumbMinHeight;
            if (area.Width > DockShape.PreviewThumbMaxWidth + 0.5 || area.Height > DockShape.PreviewThumbMaxHeight + 0.5)
                problems.Add($"{name} : zone {area} au-delà des bornes");
            // Assez grande : une des deux bornes maxi est atteinte (sauf borne mini qui agrandit l'autre côté).
            if (Math.Max(area.Width / DockShape.PreviewThumbMaxWidth, area.Height / DockShape.PreviewThumbMaxHeight) < 0.99)
                problems.Add($"{name} : zone {area} trop petite");

            foreach (double dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            {
                checks++;
                // Zone posée à une place quelconque (décalage non entier en px) comme dans une vraie fenêtre.
                var areaPx = new Rect(7.3 * dpi, 31.6 * dpi, area.Width * dpi, area.Height * dpi);
                var dest = PreviewLayout.Destination(areaPx, aspect);
                bool inside = dest.X >= Math.Floor(areaPx.Left) && dest.Y >= Math.Floor(areaPx.Top)
                    && dest.X + dest.Width <= Math.Ceiling(areaPx.Right) && dest.Y + dest.Height <= Math.Ceiling(areaPx.Bottom);
                double fillW = dest.Width / areaPx.Width, fillH = dest.Height / areaPx.Height;
                double aspectError = Math.Abs((double)dest.Width / dest.Height - aspect) / aspect;
                bool fills = clamped ? Math.Max(fillW, fillH) >= 0.98 : Math.Min(fillW, fillH) >= 0.98;
                if (!inside) problems.Add($"{name} à {dpi:P0} : {dest} sort de la zone {areaPx}");
                if (!fills) problems.Add($"{name} à {dpi:P0} : remplit {fillW:P0} × {fillH:P0}");
                if (aspectError > 0.03) problems.Add($"{name} à {dpi:P0} : format déformé de {aspectError:P1}");
            }
        }
        Check(lines, problems.Count == 0, problems.Count == 0
            ? $"aperçu du dock : miniature qui remplit la carte ({checks} cas : 7 formats × DPI 100–200 %)"
            : "aperçu du dock : " + string.Join(" ; ", problems.Take(5)));

        // Fenêtre réduite : DWM n'annonce que sa petite barre (160 × 28) → format de sa place normale.
        double minimized = PreviewLayout.Aspect(160, 28, 1280, 800);
        Check(lines, Math.Abs(minimized - 1.6) < 0.001, $"aperçu du dock : fenêtre réduite au format de sa place normale ({minimized:0.##})");
        Check(lines, Math.Abs(PreviewLayout.Aspect(0, 0) - 1.6) < 0.001 && PreviewLayout.Area(double.NaN).Width > 0,
            "aperçu du dock : sans taille connue = format 16:10");
    }
}
