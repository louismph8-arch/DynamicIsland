using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;
using DynamicIsland.Tiles.Tools;

namespace DynamicIsland;

/// <summary>
/// --snapshot des tuiles-outils (O6, lot B) : la grille montre ces tuiles en S / W / L avec des données d'exemple (micro coupé, fond
/// du pack, couleurs, QR code du site d'Opaline, mémo en cours…), puis la loupe de la pipette sur une image du pack. Rien n'est lu
/// ni changé dans Windows ; la vraie disposition est remise ensuite.
/// </summary>
public partial class IslandWindow
{
    private async Task SnapshotToolTilesAsync(string folder)
    {
        if (!GridLayout) return;
        ToolTiles.UseDemo(recording: true);
        try
        {
            // Page 1 : Fond d'écran W, Couleur W, Micro, Capture, Calculatrice, Mémo (en cours) ; page 2 : QR code L, Téléphone W,
            // Paramètres, Micro W ; page 3 : Fond d'écran L, Couleur L ; page 4 : Mémo L, Capture W, Téléphone S, Couleur S.
            var pages = new List<IReadOnlyList<(string, TileSize)>>
            {
                new[] { (ToolTiles.Wallpaper, TileSize.W), (ToolTiles.Color, TileSize.W), (ToolTiles.Microphone, TileSize.S), (ToolTiles.Capture, TileSize.S), (ToolTiles.Calculator, TileSize.S), (ToolTiles.Memo, TileSize.S) },
                new[] { (ToolTiles.Qr, TileSize.L), (ToolTiles.Phone, TileSize.W), (ToolTiles.Settings, TileSize.S), (ToolTiles.Wallpaper, TileSize.S) },
                new[] { (ToolTiles.Wallpaper, TileSize.L), (ToolTiles.Color, TileSize.L) },
                new[] { (ToolTiles.Memo, TileSize.L), (ToolTiles.Microphone, TileSize.W), (ToolTiles.Phone, TileSize.S), (ToolTiles.Color, TileSize.S) },
                new[] { (ToolTiles.Qr, TileSize.W), (ToolTiles.Capture, TileSize.W), (ToolTiles.Calculator, TileSize.W) },
                new[] { (ToolTiles.Microphone, TileSize.L), (ToolTiles.Calculator, TileSize.L) },
            };
            Collapse();
            FinishAnimations();
            GoHome(animate: false);
            Expand();
            var context = TileContext;
            TileGrid.Show(TileLayout.Place(pages), (definition, size) => definition.Create(context, size));
            string[] names = ["4i-outils-page1", "4i-outils-page2", "4i-outils-L", "4i-outils-page4", "4i-outils-W", "4i-outils-L2"];
            for (int i = 0; i < Math.Min(names.Length, TileGrid.PageCount); i++)
            {
                TileGrid.ShowPage(i, animate: false);
                TileGrid.RefreshAll();
                FinishAnimations();
                _opal.HideSweep();
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, names[i] + ".png"));
            }
            TileGrid.ShowPage(0, animate: false);
            await SavePickerSampleAsync(Path.Combine(folder, "4i-pipette-loupe.png"));
        }
        finally
        {
            ToolTiles.ClearDemo();
            RefreshGrid(); // la vraie disposition
            GoHome(animate: false);
            FinishAnimations();
        }
    }

    /// <summary>La loupe de la pipette sur une image du pack (pas de fenêtre plein écran, pas de capture de l'écran).</summary>
    private static async Task SavePickerSampleAsync(string path)
    {
        const int width = 640, height = 360;
        var image = WallpaperArt.Render(WallpaperDesign.Nebuleuse, Wallpapers.Sunset, width, height);
        var surface = new ColorPickerSurface(ColorScreenShot.FromImage(image)) { Width = width, Height = height };
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        surface.MoveTo(new Point(width * 0.42, height * 0.38));
        surface.UpdateLayout();
        await Task.Yield();
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}
