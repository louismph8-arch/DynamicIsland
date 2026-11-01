using System.IO;
using System.Windows;
using DynamicIsland.Themes;

namespace DynamicIsland.Plugins;

/// <summary>
/// Icônes Lucide par leur NOM (« cloud-sun », « bitcoin ») pour les widgets des potes : la table des noms est lue une seule
/// fois dans la police embarquée elle-même (tables « post » version 2 et « cmap »), donc toutes les icônes de la police
/// sont disponibles sans liste à tenir à jour. Quelques icônes ont changé de nom chez Lucide : leurs noms actuels sont
/// ajoutés à la main (<see cref="Aliases"/>).
/// </summary>
public static class LucideNames
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _byName;

    /// <summary>Noms actuels de Lucide dont la police garde l'ancien nom.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["clock"] = Icons.Clock,
        ["music"] = Icons.Music,
        ["calendar"] = Icons.Calendar,
        ["layout-dashboard"] = Icons.Widgets,
    };

    /// <summary>Caractère de l'icône, ou null si la police n'a pas ce nom.</summary>
    public static string? Glyph(string name)
    {
        if (Aliases.TryGetValue(name, out var alias)) return alias;
        var map = Map();
        return map.TryGetValue(name, out var glyph) ? glyph : null;
    }

    /// <summary>Nombre d'icônes connues (auto-test).</summary>
    public static int Count => Map().Count;

    private static Dictionary<string, string> Map()
    {
        lock (Gate)
        {
            if (_byName != null) return _byName;
            try
            {
                using var stream = Application.GetResourceStream(new Uri("Assets/Fonts/lucide.ttf", UriKind.Relative))!.Stream;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                _byName = Parse(copy.ToArray());
            }
            catch (Exception ex)
            {
                Log.Error("Widgets des potes : noms des icônes Lucide", ex);
                _byName = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return _byName;
        }
    }

    /// <summary>Nom de glyphe → caractère, d'après les tables de la police (TrueType).</summary>
    internal static Dictionary<string, string> Parse(byte[] font)
    {
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        int count = U16(font, 4);
        for (int i = 0; i < count; i++)
        {
            int at = 12 + 16 * i;
            tables[System.Text.Encoding.ASCII.GetString(font, at, 4)] = ((int)U32(font, at + 8), (int)U32(font, at + 12));
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!tables.TryGetValue("post", out var post) || !tables.TryGetValue("cmap", out var cmap) || U32(font, post.Offset) != 0x00020000) return result;

        // post v2 : index de nom par glyphe, puis les noms (chaînes Pascal) au-delà des 258 noms Macintosh standard.
        int glyphs = U16(font, post.Offset + 32);
        var nameIndex = new int[glyphs];
        for (int g = 0; g < glyphs; g++) nameIndex[g] = U16(font, post.Offset + 34 + 2 * g);
        var names = new List<string>();
        for (int p = post.Offset + 34 + 2 * glyphs, end = post.Offset + post.Length; p < end && p < font.Length;)
        {
            int length = font[p];
            names.Add(System.Text.Encoding.ASCII.GetString(font, p + 1, Math.Min(length, font.Length - p - 1)));
            p += 1 + length;
        }

        // cmap format 4 (Unicode BMP) : caractère de chaque glyphe (le premier trouvé).
        var charOf = new Dictionary<int, int>();
        int subtables = U16(font, cmap.Offset + 2);
        for (int k = 0; k < subtables; k++)
        {
            int start = cmap.Offset + (int)U32(font, cmap.Offset + 4 + 8 * k + 4);
            if (U16(font, start) != 4) continue;
            int segments = U16(font, start + 6) / 2;
            int ends = start + 14, starts = ends + 2 * segments + 2, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
            for (int s = 0; s < segments; s++)
            {
                int first = U16(font, starts + 2 * s), last = U16(font, ends + 2 * s);
                short delta = (short)U16(font, deltas + 2 * s);
                int range = U16(font, ranges + 2 * s);
                for (int c = first; c <= last && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (range == 0) glyph = (c + delta) & 0xFFFF;
                    else
                    {
                        int at = ranges + 2 * s + range + 2 * (c - first);
                        glyph = at + 1 < font.Length ? U16(font, at) : 0;
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0) charOf.TryAdd(glyph, c);
                }
            }
            break;
        }

        for (int g = 0; g < glyphs; g++)
        {
            int index = nameIndex[g];
            if (index < 258 || index - 258 >= names.Count || !charOf.TryGetValue(g, out int c)) continue;
            var name = names[index - 258];
            if (name.Length > 0 && c is >= 0xE000 and <= 0xF8FF) result.TryAdd(name, ((char)c).ToString());
        }
        return result;
    }

    private static int U16(byte[] data, int at) => data[at] << 8 | data[at + 1];
    private static uint U32(byte[] data, int at) => (uint)(data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3]);
}
