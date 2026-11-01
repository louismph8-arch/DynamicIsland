using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Animation;
using DynamicIsland.Palette;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;
using DynamicIsland.Tiles.Tools;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des tuiles-outils de la grille (O6, lot B) : calcul « = », en-tête WAV du mémo, QR code, couleurs, disposition par
/// défaut, modules ; puis (thread de l'interface) chaque tuile construite en S / W / L avec des données d'exemple, et aucune
/// relecture pour une tuile d'une page cachée. Rien n'est enregistré, rien n'est lu dans le vrai presse-papiers, aucun micro
/// n'est ouvert ni coupé.
/// </summary>
internal static partial class SelfTest
{
    private static void ToolTilesPure(List<string> lines)
    {
        // Calcul : « = 2+3 » (tuile Calculatrice) comme « 2+3 = » ; « = » seul n'est pas un calcul.
        bool calc = Calculator.TryEvaluate("= 2+3", out double five) && five == 5 && Calculator.TryEvaluate("=15 % de 80", out double twelve) && twelve == 12
                    && !Calculator.TryEvaluate("=", out _) && !Calculator.TryEvaluate("= 42", out _);
        Check(lines, calc, "calculatrice : « = 2+3 » = 5, « =15 % de 80 » = 12, « = » et « = 42 » ne sont pas des calculs");

        // Mémo vocal : en-tête WAV PCM 16 bits mono 44,1 kHz exact.
        var header = VoiceMemo.Header(88200);
        bool wav = header.Length == 44 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && BitConverter.ToInt32(header, 4) == 36 + 88200
                   && Encoding.ASCII.GetString(header, 8, 8) == "WAVEfmt " && BitConverter.ToInt32(header, 16) == 16 && BitConverter.ToInt16(header, 20) == 1
                   && BitConverter.ToInt16(header, 22) == 1 && BitConverter.ToInt32(header, 24) == 44100 && BitConverter.ToInt32(header, 28) == 88200
                   && BitConverter.ToInt16(header, 32) == 2 && BitConverter.ToInt16(header, 34) == 16 && Encoding.ASCII.GetString(header, 36, 4) == "data"
                   && BitConverter.ToInt32(header, 40) == 88200;
        Check(lines, wav, $"mémo vocal : en-tête WAV (PCM, 1 canal, 44 100 Hz, 16 bits, 1 s = 88 200 octets) ; dossier « {System.IO.Path.GetFileName(VoiceMemo.Folder)} » dans Documents\\{AppInfo.Name}");

        // QR code : site d'un lien, début d'un texte, 213 octets au plus.
        bool describe = QrClip.Describe("https://www.exemple.com/page?x=1") == "exemple.com" && QrClip.Describe("ligne 1\r\nligne 2") == "ligne 1 ligne 2"
                        && QrClip.Describe(new string('a', 60)).Length == 41;
        bool encode = QrClip.Encode(new string('a', 213)) && QrClip.Image != null && QrClip.Problem == null
                      && !QrClip.Encode(new string('a', 214)) && QrClip.Problem == "tiles.qr.too_long" && !QrClip.Encode("   ") && QrClip.Problem == "tiles.qr.empty";
        QrClip.Reset();
        Check(lines, describe && encode, "QR code : « exemple.com » pour un lien, texte sur une ligne coupé à 40 caractères, 213 octets acceptés, 214 refusés, vide refusé");

        // Couleurs : 10 gardées, la plus récente d'abord, sans doublon ; HEX / rgb().
        ColorHistory.UseDemo();
        for (byte i = 0; i < 12; i++) ColorHistory.Add(Color.FromRgb(i, 0, 0));
        ColorHistory.Add(Color.FromRgb(5, 0, 0));
        bool colors = ColorHistory.All.Count == ToolTileShape.HistoryKept && ColorHistory.All[0] == Color.FromRgb(5, 0, 0) && ColorHistory.All.Distinct().Count() == ColorHistory.All.Count
                      && ColorHistory.Hex(Color.FromRgb(0x3A, 0x7B, 0xFF)) == "#3A7BFF" && ColorHistory.Rgb(Color.FromRgb(58, 123, 255)) == "rgb(58, 123, 255)";
        ColorHistory.UseDemo();
        Check(lines, colors, "couleurs : 10 gardées en mémoire, la dernière en tête sans doublon, « #3A7BFF » / « rgb(58, 123, 255) »");

        // Disposition : les tuiles-outils sur leur page, dans l'ordre ; une grille enregistrée avant reçoit les nouvelles à la fin.
        var tools = ToolTiles.All.Select(d => d.Id).ToList();
        var fresh = IslandGrid.Default();
        var old = new IslandGrid { Pages = [new() { Tiles = [new() { Id = "music", Size = TileSize.L }] }], Known = [.. IslandTabItem.All] };
        old.Normalize();
        bool layout = tools.All(id => fresh.Contains(id) == TileRegistry.Find(id)!.InDefaultLayout)
                      && tools.All(id => old.Contains(id)) && old.Pages[^1].Tiles[^1].Id == tools[^1]
                      && ToolTiles.All.All(d => d.Click != null && d.CreateFace != null && d.Allows(d.DefaultSize) && d.CreateDetail(null!) == null);
        Check(lines, layout, $"disposition : {tools.Count} tuiles-outils dans la disposition par défaut ({string.Join(", ", tools)}), ajoutées à la fin d'une grille d'avant ; chacune a son action au clic");

        // Modules : une erreur dans le mémo / la pipette n'arrête que ce module ; les autres tuiles-outils ont le leur.
        bool modules = ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Tiles.Tools.VoiceMemo.Start()") == ModuleGuard.MemoTile
                       && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Tiles.Tools.ColorPickerSurface.MoveTo()") == ModuleGuard.ColorTile
                       && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Tiles.Tools.QrClip.Encode()") == ModuleGuard.ToolTiles
                       && Loc.T("module." + ModuleGuard.MemoTile) != "module." + ModuleGuard.MemoTile;
        Check(lines, modules, "modules : mémo vocal, pipette et autres tuiles-outils arrêtés séparément (l'île continue)");
    }

    /// <summary>Thread de l'interface : vues S / W / L avec des données d'exemple ; une page cachée n'est jamais relue.</summary>
    private static void ToolTileViews(List<string> lines)
    {
        ToolTiles.UseDemo();
        try
        {
            var context = new TileContext
            {
                Media = new MediaService(Application.Current.Dispatcher),
                Timer = new TimerService(),
                ToolAvailable = _ => true,
                ExistingView = _ => null,
            };
            var broken = new List<string>();
            int built = 0;
            foreach (var definition in ToolTiles.All)
            {
                foreach (var size in definition.Sizes)
                {
                    try
                    {
                        var face = definition.Create(context, size);
                        face.Refresh();
                        var (columns, rows) = size.Span();
                        var area = new Size(columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap, rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap);
                        face.Width = area.Width;
                        face.Height = area.Height;
                        face.Measure(area);
                        face.Arrange(new Rect(area));
                        if (face is not ToolFace { Updates: 1 }) broken.Add($"{definition.Id} {size} (relectures)");
                        built++;
                    }
                    catch (Exception ex)
                    {
                        broken.Add($"{definition.Id} {size} ({ex.GetType().Name} : {ex.Message})");
                    }
                }
            }
            Check(lines, broken.Count == 0, broken.Count == 0
                ? $"{built} vues de tuiles-outils construites (S / W / L permises) avec les données d'exemple"
                : "tuiles-outils en défaut : " + string.Join(", ", broken));

            // Grille : une tuile-onglet sur la page 1, les tuiles-outils sur la page 2 → île ouverte sur la page 1 : aucune relecture
            // de la page 2, aucune animation en cours ; la tuile Téléphone accepte les fichiers seulement quand sa page est affichée.
            var grid = new TileGridView();
            // Téléphone en premier : il est sur la page 2 (8 cases), la 9e tuile passe sur une page 3.
            var tools = ToolTiles.All.OrderBy(d => d.Id == ToolTiles.Phone ? 0 : 1).Select(d => (d.Id, TileSize.S)).ToList();
            var slots = TileLayout.Place([[(IslandTabItem.Music, TileSize.L)], tools]);
            grid.Show(slots, (d, s) => d.Create(context, s));
            grid.IsActive = true;
            var hidden = grid.Faces.OfType<ToolFace>().ToList();
            bool quiet = hidden.Count == ToolTiles.All.Count && hidden.All(f => f.Updates == 0) && MotionClock.Count == 0 && !grid.CurrentPageAcceptsFiles;
            grid.ShowPage(1, animate: false);
            bool shown = hidden.All(f => f.Updates == (grid.IsOnCurrentPage(f) ? 1 : 0)) && hidden.Count(grid.IsOnCurrentPage) == TileGridShape.Columns * TileGridShape.Rows
                         && grid.CurrentPageAcceptsFiles;
            grid.IsActive = false;
            grid.ShowPage(0, animate: false);
            int before = hidden.Sum(f => f.Updates);
            grid.RefreshAll();
            Check(lines, quiet && shown && hidden.Sum(f => f.Updates) == before + hidden.Count,
                "page cachée : aucune tuile-outil relue ni animée tant que sa page n'est pas affichée ; Téléphone accepte un fichier seulement sur sa page");

            // Palette : « = … » ne propose que le calcul (et le note pour la tuile), « = » seul une astuce.
            var actions = new PaletteActions(_ => { }, () => { });
            Calculator.Last = null;
            var result = PaletteEngine.Search("= 15 % de 80", actions);
            var hint = PaletteEngine.Search("=", actions);
            bool palette = result.Count == 1 && result[0].Title == Calculator.Format(12) && Calculator.Last is { Result: 12 }
                           && hint.Count == 1 && hint[0].Title == Loc.T("palette.calc.hint");
            Check(lines, palette, "palette : « = 15 % de 80 » → 12 seulement (gardé pour la tuile Calculatrice), « = » seul → astuce");

            // Pipette : la surface lit le bon pixel d'une image d'exemple (sans fenêtre ni capture de l'écran).
            var pixels = new byte[4 * 4 * 4];
            pixels[(2 * 4 + 3) * 4 + 0] = 0xFF; // pixel (3, 2) : bleu
            var shot = ColorScreenShot.FromPixels(4, 4, 16, pixels);
            var surface = new ColorPickerSurface(shot);
            surface.Measure(new Size(400, 400));
            surface.Arrange(new Rect(0, 0, 400, 400));
            surface.MoveTo(new Point(350, 250));
            Check(lines, surface.Current == Color.FromRgb(0, 0, 0xFF) && surface.PixelAt(new Point(0, 399)) == (0, 3),
                "pipette : point de la surface → pixel de l'écran figé (couleur lue juste, bords bornés)");
        }
        finally
        {
            ToolTiles.ClearDemo();
        }
    }
}
