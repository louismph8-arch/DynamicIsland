using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Petit générateur de QR code (6D) pour l'adresse de la télécommande : mode octets, correction « M », versions 1 à 10
/// (jusqu'à 213 octets), sans dépendance. D'après la norme ISO/IEC 18004 et l'algorithme de Project Nayuki (licence MIT).
/// </summary>
public sealed class QrCode
{
    public const int MaxVersion = 10;

    // Correction « M » (15 % environ), versions 0 à 10 (l'index 0 ne sert pas).
    private static readonly int[] EccPerBlock = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] BlockCount = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private const int FormatBitsM = 0; // L = 1, M = 0, Q = 3, H = 2

    private readonly bool[,] _modules;  // [ligne, colonne], vrai = foncé
    private readonly bool[,] _function; // motifs fixes (repères, synchronisation, format…) : jamais masqués

    public int Version { get; }
    public int Size { get; }
    public int Mask { get; private set; }

    public bool this[int row, int column] => _modules[row, column];

    private QrCode(int version)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _function = new bool[Size, Size];
    }

    /// <summary>QR code du texte (UTF-8) ; null s'il est trop long pour la version 10.</summary>
    public static QrCode? Encode(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        for (int version = 1; version <= MaxVersion; version++)
        {
            int countBits = version < 10 ? 8 : 16;
            if (4 + countBits + bytes.Length * 8 > DataCodewords(version) * 8) continue;
            var qr = new QrCode(version);
            qr.Build(DataBits(bytes, version, countBits));
            return qr;
        }
        return null;
    }

    /// <summary>Image en niveaux de gris (foncé sur blanc) avec la marge de 4 modules exigée par la norme.</summary>
    public BitmapSource ToBitmap(int moduleSize, Color? dark = null)
    {
        const int quiet = 4;
        int side = (Size + 2 * quiet) * moduleSize;
        var ink = dark ?? Colors.Black;
        var pixels = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int row = y / moduleSize - quiet, column = x / moduleSize - quiet;
                bool on = row >= 0 && row < Size && column >= 0 && column < Size && _modules[row, column];
                int i = (y * side + x) * 4;
                pixels[i] = on ? ink.B : (byte)255;
                pixels[i + 1] = on ? ink.G : (byte)255;
                pixels[i + 2] = on ? ink.R : (byte)255;
                pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4);
        bitmap.Freeze();
        return bitmap;
    }

    // ------------------------------------------------------------------ données

    private static int RawDataModules(int version)
    {
        int result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int align = version / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int version) => RawDataModules(version) / 8 - EccPerBlock[version] * BlockCount[version];

    private static byte[] DataBits(byte[] bytes, int version, int countBits)
    {
        var bits = new List<bool>();
        void Append(int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }
        Append(0b0100, 4); // mode octets
        Append(bytes.Length, countBits);
        foreach (var b in bytes) Append(b, 8);

        int capacity = DataCodewords(version) * 8;
        Append(0, Math.Min(4, capacity - bits.Count)); // fin
        Append(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var result = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        return result;
    }

    /// <summary>Découpe en blocs, ajoute la correction Reed-Solomon de chaque bloc, puis entrelace les blocs.</summary>
    internal static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        int blocks = BlockCount[version], eccLength = EccPerBlock[version];
        int raw = RawDataModules(version) / 8;
        int shortBlocks = blocks - raw % blocks, shortLength = raw / blocks;
        var divisor = ReedSolomonDivisor(eccLength);

        var all = new List<byte[]>();
        for (int i = 0, k = 0; i < blocks; i++)
        {
            int length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
            var block = new byte[shortLength + 1];
            Array.Copy(data, k, block, 0, length);
            k += length;
            var ecc = ReedSolomonRemainder(data.AsSpan(k - length, length), divisor);
            Array.Copy(ecc, 0, block, shortLength + 1 - eccLength, eccLength);
            all.Add(block);
        }

        var result = new List<byte>(raw);
        for (int i = 0; i < shortLength + 1; i++)
            for (int j = 0; j < blocks; j++)
                if (i != shortLength - eccLength || j >= shortBlocks) result.Add(all[j][i]);
        return [.. result];
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    internal static byte[] ReedSolomonRemainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    internal static byte[] ReedSolomonRemainder(ReadOnlySpan<byte> data, int degree) => ReedSolomonRemainder(data, ReedSolomonDivisor(degree));

    /// <summary>Produit dans le corps de Galois GF(2^8) modulo x^8 + x^4 + x^3 + x^2 + 1.</summary>
    private static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    // ------------------------------------------------------------------ dessin

    private void Build(byte[] data)
    {
        DrawFunctionPatterns();
        DrawCodewords(AddEccAndInterleave(data, Version));

        int best = 0, bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            int penalty = Penalty();
            if (penalty < bestPenalty)
            {
                best = mask;
                bestPenalty = penalty;
            }
            ApplyMask(mask); // le masque est un « ou exclusif » : le réappliquer l'enlève
        }
        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    private void Set(int column, int row, bool dark)
    {
        _modules[row, column] = dark;
        _function[row, column] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }
        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions();
        int last = positions.Length - 1;
        for (int i = 0; i <= last; i++)
            for (int j = 0; j <= last; j++)
                if (!(i == 0 && j == 0 || i == 0 && j == last || i == last && j == 0))
                    DrawAlignment(positions[i], positions[j]);

        DrawFormatBits(0); // réservé ; les vrais bits viennent avec le masque
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, distance != 2 && distance != 4);
            }
    }

    private void DrawAlignment(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1) return [];
        int count = Version / 7 + 2;
        int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step) result[i] = position;
        return result;
    }

    /// <summary>Niveau de correction + masque, protégés par un code BCH, écrits en deux exemplaires.</summary>
    internal static int FormatBits(int eccFormat, int mask)
    {
        int data = eccFormat << 3 | mask;
        int remainder = data;
        for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        return (data << 10 | remainder) ^ 0x5412;
    }

    private void DrawFormatBits(int mask)
    {
        int bits = FormatBits(FormatBitsM, mask);
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(8, i, Bit(i));
        Set(8, 7, Bit(6));
        Set(8, 8, Bit(7));
        Set(7, 8, Bit(8));
        for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));

        for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
        Set(8, Size - 8, true); // module toujours foncé
    }

    internal static int VersionBits(int version)
    {
        int remainder = version;
        for (int i = 0; i < 12; i++) remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        return version << 12 | remainder;
    }

    private void DrawVersion()
    {
        if (Version < 7) return;
        int bits = VersionBits(Version);
        for (int i = 0; i < 18; i++)
        {
            bool bit = ((bits >> i) & 1) != 0;
            int a = Size - 11 + i % 3, b = i / 3;
            Set(a, b, bit);
            Set(b, a, bit);
        }
    }

    /// <summary>Codewords posés en zigzag par colonnes de deux, de droite à gauche, en sautant la colonne de synchronisation.</summary>
    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vertical = 0; vertical < Size; vertical++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vertical : vertical;
                    if (_function[y, x] || i >= data.Length * 8) continue;
                    _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (_function[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) _modules[y, x] = !_modules[y, x];
            }
    }

    /// <summary>
    /// Pénalité de la norme (longues suites, carrés 2×2, faux repères, équilibre clair / foncé) : le masque qui la réduit
    /// donne le code le plus facile à lire. N'importe quel masque reste un code valide.
    /// </summary>
    private int Penalty()
    {
        int result = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < Size; a++)
            {
                int run = 1;
                for (int b = 1; b < Size; b++)
                {
                    bool previous = pass == 0 ? _modules[a, b - 1] : _modules[b - 1, a];
                    bool current = pass == 0 ? _modules[a, b] : _modules[b, a];
                    if (current == previous)
                    {
                        run++;
                        if (run == 5) result += 3;
                        else if (run > 5) result++;
                    }
                    else
                    {
                        run = 1;
                    }
                }
                for (int b = 0; b + 10 < Size; b++)
                    if (LooksLikeFinder(pass, a, b)) result += 40;
            }

        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
            {
                bool color = _modules[y, x];
                if (color == _modules[y, x + 1] && color == _modules[y + 1, x] && color == _modules[y + 1, x + 1]) result += 3;
            }

        int dark = 0;
        foreach (bool module in _modules)
            if (module) dark++;
        int total = Size * Size;
        result += ((Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10;
        return result;
    }

    private static readonly bool[] FinderLike = [true, false, true, true, true, false, true, false, false, false, false];

    private bool LooksLikeFinder(int pass, int line, int start)
    {
        bool forward = true, backward = true;
        for (int i = 0; i < FinderLike.Length; i++)
        {
            bool module = pass == 0 ? _modules[line, start + i] : _modules[start + i, line];
            forward &= module == FinderLike[i];
            backward &= module == FinderLike[^(i + 1)];
        }
        return forward || backward;
    }
}
