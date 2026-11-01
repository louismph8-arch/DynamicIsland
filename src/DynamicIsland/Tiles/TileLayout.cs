using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>Taille d'une tuile de la grille de l'île (refonte O6) : S = 1 case, W = 2 × 1 (large), L = 2 × 2 (grande).</summary>
public enum TileSize
{
    S,
    W,
    L,
}

/// <summary>Une tuile posée : page, colonne et ligne de son coin haut gauche (en cases).</summary>
public readonly record struct TileSlot(string Id, TileSize Size, int Page, int Column, int Row);

/// <summary>
/// Placement des tuiles (refonte O6), calcul pur (--self-test) : chaque page enregistrée est remplie dans l'ordre, chaque tuile
/// au PREMIER emplacement libre (de gauche à droite, puis de haut en bas) où elle tient ; les tuiles de trop passent sur une
/// nouvelle page juste après (placement dense : une petite tuile plus loin dans la liste bouche un trou). Si l'ordre choisi
/// laisse des tuiles de côté alors qu'elles tiendraient rangées de la plus grande à la plus petite (L, puis W, puis S), c'est
/// ce rangement qui est gardé pour cette page. Aucune superposition, aucune tuile hors de la grille.
/// </summary>
public static class TileLayout
{
    public static (int Columns, int Rows) Span(this TileSize size) => size switch
    {
        TileSize.W => (2, 1),
        TileSize.L => (2, 2),
        _ => (1, 1),
    };

    public static int Cells(this TileSize size) => size.Span().Columns * size.Span().Rows;

    public static List<TileSlot> Place(IEnumerable<IReadOnlyList<(string Id, TileSize Size)>> pages,
        int columns = TileGridShape.Columns, int rows = TileGridShape.Rows)
    {
        var result = new List<TileSlot>();
        if (columns < 1 || rows < 1) return result;
        int pageIndex = 0;
        foreach (var page in pages)
        {
            // Une tuile plus grande que la grille entière (grille réduite un jour) devient S plutôt que de disparaître.
            var pending = page.Select(t => Fits(t.Size, columns, rows) ? t : (t.Id, Size: TileSize.S)).ToList();
            while (pending.Count > 0)
            {
                var placed = Pack(pending, columns, rows, out var rest);
                if (rest.Count > 0)
                {
                    var sorted = pending.OrderByDescending(t => t.Size.Cells()).ToList(); // tri stable : L, W, S
                    var tidy = Pack(sorted, columns, rows, out var tidyRest);
                    if (tidyRest.Count < rest.Count)
                    {
                        placed = tidy;
                        rest = tidyRest;
                    }
                }
                if (placed.Count == 0) break; // garde-fou (ne peut pas arriver : une S tient toujours dans une page vide)
                result.AddRange(placed.Select(p => p with { Page = pageIndex }));
                pageIndex++;
                pending = rest;
            }
        }
        return result;
    }

    private static bool Fits(TileSize size, int columns, int rows) => size.Span().Columns <= columns && size.Span().Rows <= rows;

    private static List<TileSlot> Pack(List<(string Id, TileSize Size)> tiles, int columns, int rows, out List<(string Id, TileSize Size)> rest)
    {
        var used = new bool[columns, rows];
        var placed = new List<TileSlot>();
        rest = [];
        foreach (var tile in tiles)
        {
            if (FirstFree(used, tile.Size, columns, rows) is (int column, int row))
            {
                var (w, h) = tile.Size.Span();
                for (int x = column; x < column + w; x++)
                    for (int y = row; y < row + h; y++) used[x, y] = true;
                placed.Add(new TileSlot(tile.Id, tile.Size, 0, column, row));
            }
            else rest.Add(tile);
        }
        return placed;
    }

    private static (int, int)? FirstFree(bool[,] used, TileSize size, int columns, int rows)
    {
        var (w, h) = size.Span();
        for (int row = 0; row + h <= rows; row++)
            for (int column = 0; column + w <= columns; column++)
            {
                bool free = true;
                for (int x = column; x < column + w && free; x++)
                    for (int y = row; y < row + h && free; y++) free = !used[x, y];
                if (free) return (column, row);
            }
        return null;
    }

    /// <summary>
    /// Page suivante / précédente (molette, glisser, calcul pur) : <paramref name="step"/> &gt; 0 = suivante. Au bout, la page reste
    /// la même et <c>Edge</c> est vrai (rebond élastique). Toujours dans [0, count − 1].
    /// </summary>
    public static (int Page, bool Edge) Turn(int current, int count, int step)
    {
        if (count <= 0) return (0, step != 0);
        current = Math.Clamp(current, 0, count - 1);
        if (step == 0) return (current, false);
        int target = current + Math.Sign(step);
        return target < 0 || target >= count ? (current, true) : (target, false);
    }
}
