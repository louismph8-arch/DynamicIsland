using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicIsland.Themes;

namespace DynamicIsland.Plugins;

/// <summary>Widget refusé : message lisible (traduit) + clé du message (pour l'auto-test).</summary>
public sealed class WidgetPackageException(string key, params object?[] values) : Exception(Loc.F(key, values))
{
    public string Key { get; } = key;
}

/// <summary>
/// Lecture d'un widget des potes (7H) = fichier d'un INCONNU : tout est vérifié avant d'être gardé, et le moindre écart
/// refuse le widget entier avec un message clair (rien n'est installé). Un fichier .opalinewidget est soit le JSON lui-même,
/// soit un .zip avec « widget.json » et jusqu'à 8 images PNG. Aucun code, aucune ressource extérieure (sauf la source web
/// déclarée, qui reste coupée tant que l'utilisateur n'a pas autorisé son site). Calcul pur : aucun accès réseau ni disque
/// à part le fichier lu.
/// </summary>
public static partial class WidgetPackageReader
{
    public const string Extension = ".opalinewidget";
    public const string ManifestName = "widget.json";
    public const int Format = 1;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxManifestBytes = 256 * 1024;
    public const int MaxImageBytes = 1024 * 1024;
    public const int MaxImages = 8;
    public const int MaxImagePixels = 2048;
    public const int MaxElements = 40;
    public const int MaxDepth = 4;
    public const int MaxText = 200;
    public const int MaxFields = 10;
    public const int MinEveryMinutes = 5;
    public const double MinWidth = 80, MaxWidth = 640, MinHeight = 40, MaxHeight = 480;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,23}$")]
    private static partial Regex FieldPattern();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex EntrySegment();

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();

    public static bool IsValidId(string? id) => id != null && IdPattern().IsMatch(id);

    /// <summary>Lit et vérifie un fichier .opalinewidget (5 Mo au plus).</summary>
    public static WidgetPackage Read(string path, out byte[] bytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new WidgetPackageException("plugins.error.not_found");
        if (info.Length > MaxFileBytes) throw new WidgetPackageException("plugins.error.too_big", MaxFileBytes / 1024 / 1024);
        bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    /// <summary>Vérifie un paquet en mémoire : JSON seul, ou .zip (signature « PK »).</summary>
    public static WidgetPackage Parse(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes) throw new WidgetPackageException("plugins.error.too_big", MaxFileBytes / 1024 / 1024);
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K') return ParseZip(bytes);
        if (bytes.Length > MaxManifestBytes) throw new WidgetPackageException("plugins.error.manifest_too_big");
        return ParseManifest(Decode(bytes), new Dictionary<string, byte[]>());
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new WidgetPackageException("plugins.error.not_json");
        }
    }

    // ------------------------------------------------------------------ .zip

    private static WidgetPackage ParseZip(byte[] bytes)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new WidgetPackageException("plugins.error.bad_zip");
        }
        using (archive)
        {
            if (archive.Entries.Count > MaxImages + 4) throw new WidgetPackageException("plugins.error.too_many_files");
            string? manifest = null;
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.EndsWith('/')) continue; // dossier
                if (!IsSafeEntry(name)) throw new WidgetPackageException("plugins.error.bad_path", Clip(name, 60));
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (string.Equals(name, ManifestName, StringComparison.OrdinalIgnoreCase))
                {
                    if (manifest != null) throw new WidgetPackageException("plugins.error.two_manifests");
                    manifest = Decode(ReadEntry(entry, MaxManifestBytes, "plugins.error.manifest_too_big"));
                    total += manifest.Length;
                }
                else if (extension == ".png")
                {
                    if (images.Count >= MaxImages) throw new WidgetPackageException("plugins.error.too_many_images", MaxImages);
                    if (images.ContainsKey(name)) throw new WidgetPackageException("plugins.error.bad_path", Clip(name, 60));
                    var data = ReadEntry(entry, MaxImageBytes, "plugins.error.image_too_big", Clip(name, 60));
                    CheckPng(name, data);
                    images[name] = data;
                    total += data.Length;
                }
                else if (extension is ".md" or ".txt")
                {
                    continue; // explications de l'auteur : jamais lues ni gardées
                }
                else
                {
                    throw new WidgetPackageException("plugins.error.bad_file", Clip(name, 60));
                }
                if (total > MaxFileBytes) throw new WidgetPackageException("plugins.error.too_big", MaxFileBytes / 1024 / 1024);
            }
            if (manifest == null) throw new WidgetPackageException("plugins.error.no_manifest", ManifestName);
            return ParseManifest(manifest, images);
        }
    }

    /// <summary>Chemin d'un fichier du zip : relatif, segments simples, jamais « .. », « \ », « : » ni chemin absolu.</summary>
    internal static bool IsSafeEntry(string name) =>
        name.Length is > 0 and <= 128 && !name.StartsWith('/') && !name.Contains('\\') && !name.Contains(':')
        && name.Split('/') is { Length: <= 2 } parts && parts.All(p => p != ".." && p != "." && EntrySegment().IsMatch(p));

    /// <summary>Lit un fichier du zip sans se fier à la taille annoncée (bombe zip) : au-delà de la limite, refus.</summary>
    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit, string errorKey, params object?[] values)
    {
        if (entry.Length > limit) throw new WidgetPackageException(errorKey, values);
        try
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                copy.Write(buffer, 0, read);
                if (copy.Length > limit) throw new WidgetPackageException(errorKey, values);
            }
            return copy.ToArray();
        }
        catch (InvalidDataException)
        {
            throw new WidgetPackageException("plugins.error.bad_zip");
        }
    }

    /// <summary>Vraie image PNG (signature + en-tête IHDR), 2048 × 2048 au plus. Le décodage, lui, se fait au rendu (protégé).</summary>
    private static void CheckPng(string name, byte[] data)
    {
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(PngSignature) || Encoding.ASCII.GetString(data, 12, 4) != "IHDR")
            throw new WidgetPackageException("plugins.error.not_png", Clip(name, 60));
        long width = (long)data[16] << 24 | (long)data[17] << 16 | (long)data[18] << 8 | data[19];
        long height = (long)data[20] << 24 | (long)data[21] << 16 | (long)data[22] << 8 | data[23];
        if (width is < 1 or > MaxImagePixels || height is < 1 or > MaxImagePixels)
            throw new WidgetPackageException("plugins.error.image_size", Clip(name, 60), MaxImagePixels);
    }

    // ------------------------------------------------------------------ widget.json

    private static WidgetPackage ParseManifest(string json, IReadOnlyDictionary<string, byte[]> images)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 32,
                CommentHandling = JsonCommentHandling.Skip, // le modèle est commenté
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException)
        {
            throw new WidgetPackageException("plugins.error.not_json");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new WidgetPackageException("plugins.error.not_json");
            Only(root, "", "format", "id", "name", "author", "version", "description", "size", "background", "source", "root");
            if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number || format.GetDouble() != Format)
                throw new WidgetPackageException("plugins.error.format", Format);
            var id = RequiredString(root, "id", 40);
            if (!IsValidId(id)) throw new WidgetPackageException("plugins.error.id", id);
            var name = RequiredString(root, "name", 60);
            if (name.Trim().Length == 0) throw new WidgetPackageException("plugins.error.missing", "name");

            double width = 240, height = 120;
            if (root.TryGetProperty("size", out var size))
            {
                if (size.ValueKind != JsonValueKind.Object) throw new WidgetPackageException("plugins.error.type", "size");
                Only(size, "size", "width", "height");
                width = Number(size, "width", MinWidth, MaxWidth, width);
                height = Number(size, "height", MinHeight, MaxHeight, height);
            }
            var background = OptionalString(root, "background", 10) ?? "card";
            if (background is not ("card" or "none")) throw new WidgetPackageException("plugins.error.value", "background", background);

            var source = root.TryGetProperty("source", out var sourceElement) ? ParseSource(sourceElement) : null;
            if (!root.TryGetProperty("root", out var rootElement)) throw new WidgetPackageException("plugins.error.missing", "root");
            var context = new Context(images, source);
            var tree = ParseElement(rootElement, 1, context);

            var definition = new WidgetDefinition
            {
                Id = id,
                Name = name.Trim(),
                Author = (OptionalString(root, "author", 60) ?? "").Trim(),
                Version = (OptionalString(root, "version", 20) ?? "1.0").Trim(),
                Description = (OptionalString(root, "description", MaxText) ?? "").Trim(),
                Width = width,
                Height = height,
                Card = background == "card",
                Source = source,
                Root = tree,
            };
            // Images du paquet que le widget n'utilise pas : jamais gardées.
            var used = definition.Elements().Where(e => e.Src != null).Select(e => e.Src!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kept = images.Where(i => used.Contains(i.Key)).ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase);
            return new WidgetPackage(definition, kept);
        }
    }

    private sealed class Context(IReadOnlyDictionary<string, byte[]> images, WebSourceDefinition? source)
    {
        public IReadOnlyDictionary<string, byte[]> Images { get; } = images;
        public WebSourceDefinition? Source { get; } = source;
        public int Count { get; set; }
    }

    private static WebSourceDefinition ParseSource(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new WidgetPackageException("plugins.error.type", "source");
        Only(element, "source", "url", "every", "fields");
        var text = RequiredString(element, "url", 400);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new WidgetPackageException("plugins.error.not_https");
        if (url.UserInfo.Length > 0 || !url.IsDefaultPort || url.Fragment.Length > 0 || !IsPublicHost(url))
            throw new WidgetPackageException("plugins.error.bad_url");
        int every = (int)Math.Round(Number(element, "every", 1, 24 * 60, 10));
        if (!element.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            throw new WidgetPackageException("plugins.error.missing", "source.fields");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields.EnumerateObject())
        {
            if (map.Count >= MaxFields) throw new WidgetPackageException("plugins.error.too_many_fields", MaxFields);
            if (!FieldPattern().IsMatch(field.Name)) throw new WidgetPackageException("plugins.error.field", Clip(field.Name, 30));
            if (field.Value.ValueKind != JsonValueKind.String || !JsonPath.IsValid(field.Value.GetString()))
                throw new WidgetPackageException("plugins.error.path", field.Name);
            map[field.Name] = field.Value.GetString()!;
        }
        if (map.Count == 0) throw new WidgetPackageException("plugins.error.missing", "source.fields");
        return new WebSourceDefinition { Url = url, EveryMinutes = Math.Max(MinEveryMinutes, every), Fields = map };
    }

    /// <summary>Un vrai nom de site : jamais une adresse IP, « localhost » ni un nom sans point (réseau local).</summary>
    internal static bool IsPublicHost(Uri url)
    {
        if (url.HostNameType != UriHostNameType.Dns) return false;
        var host = url.IdnHost.ToLowerInvariant();
        return host.Contains('.') && !host.EndsWith(".local", StringComparison.Ordinal) && !host.EndsWith(".localhost", StringComparison.Ordinal)
               && host != "localhost" && !IPAddress.TryParse(host, out _);
    }

    // ------------------------------------------------------------------ éléments

    private static readonly Dictionary<string, WidgetElementType> Types = new(StringComparer.Ordinal)
    {
        ["stack"] = WidgetElementType.Stack,
        ["grid"] = WidgetElementType.Grid,
        ["text"] = WidgetElementType.Text,
        ["icon"] = WidgetElementType.Icon,
        ["image"] = WidgetElementType.Image,
        ["ring"] = WidgetElementType.Ring,
        ["bar"] = WidgetElementType.Bar,
        ["spacer"] = WidgetElementType.Spacer,
    };

    private static WidgetElement ParseElement(JsonElement element, int depth, Context context)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new WidgetPackageException("plugins.error.type", "root");
        if (++context.Count > MaxElements) throw new WidgetPackageException("plugins.error.too_many_elements", MaxElements);
        var typeName = RequiredString(element, "type", 10);
        if (!Types.TryGetValue(typeName, out var type)) throw new WidgetPackageException("plugins.error.element", Clip(typeName, 20));
        var where = typeName;
        var align = OptionalString(element, "align", 10) ?? "start";
        if (align is not ("start" or "center" or "end" or "stretch")) throw new WidgetPackageException("plugins.error.value", "align", align);

        switch (type)
        {
            case WidgetElementType.Stack:
            case WidgetElementType.Grid:
            {
                if (type == WidgetElementType.Stack) Only(element, where, "type", "align", "direction", "gap", "padding", "children");
                else Only(element, where, "type", "align", "columns", "gap", "padding", "children");
                if (depth > MaxDepth) throw new WidgetPackageException("plugins.error.depth", MaxDepth);
                var direction = OptionalString(element, "direction", 12) ?? "vertical";
                if (direction is not ("vertical" or "horizontal")) throw new WidgetPackageException("plugins.error.value", "direction", direction);
                var children = new List<WidgetElement>();
                if (element.TryGetProperty("children", out var list))
                {
                    if (list.ValueKind != JsonValueKind.Array) throw new WidgetPackageException("plugins.error.type", "children");
                    foreach (var child in list.EnumerateArray()) children.Add(ParseElement(child, depth + 1, context));
                }
                return new WidgetElement
                {
                    Type = type,
                    Align = align,
                    Horizontal = direction == "horizontal",
                    Columns = (int)Math.Round(Number(element, "columns", 1, 6, 2)),
                    Gap = Number(element, "gap", 0, 48, 6),
                    Padding = Number(element, "padding", 0, 48, 0),
                    Children = children,
                };
            }
            case WidgetElementType.Text:
                Only(element, where, "type", "align", "text", "size", "weight", "font", "color", "lines");
                return new WidgetElement
                {
                    Type = type,
                    Align = align,
                    Text = RequiredString(element, "text", MaxText),
                    Size = FontSize(element, "size", TypeRamp.Body, TextSizes),
                    Weight = Choice(element, "weight", "regular", "light", "regular", "semibold", "bold"),
                    Font = Choice(element, "font", "ui", "ui", "display"),
                    Color = ColorName(element),
                    MaxLines = (int)Math.Round(Number(element, "lines", 1, 4, 1)),
                };
            case WidgetElementType.Icon:
            {
                Only(element, where, "type", "align", "name", "size", "color");
                var icon = RequiredString(element, "name", 60);
                if (!icon.Contains('{') && LucideNames.Glyph(icon) == null) throw new WidgetPackageException("plugins.error.icon", Clip(icon, 40));
                return new WidgetElement
                {
                    Type = type,
                    Align = align,
                    Icon = icon,
                    Size = FontSize(element, "size", IconSize.XL, IconSizes),
                    Color = ColorName(element),
                };
            }
            case WidgetElementType.Image:
            {
                Only(element, where, "type", "align", "src", "width", "height", "round");
                var src = RequiredString(element, "src", 128);
                if (!context.Images.ContainsKey(src)) throw new WidgetPackageException("plugins.error.no_image", Clip(src, 60));
                return new WidgetElement
                {
                    Type = type,
                    Align = align,
                    Src = src,
                    Width = Number(element, "width", 8, MaxWidth, 48),
                    Height = Number(element, "height", 8, MaxHeight, 48),
                    Round = element.TryGetProperty("round", out var round) && round.ValueKind == JsonValueKind.True,
                };
            }
            case WidgetElementType.Ring:
            case WidgetElementType.Bar:
            {
                if (type == WidgetElementType.Ring) Only(element, where, "type", "align", "value", "min", "max", "size", "thickness", "color", "text");
                else Only(element, where, "type", "align", "value", "min", "max", "height", "width", "color");
                var value = RequiredString(element, "value", 40);
                if (!WidgetTemplate.IsNumberKey(value, context.Source)) throw new WidgetPackageException("plugins.error.number_key", Clip(value, 40));
                double min = Number(element, "min", -1e9, 1e9, 0), max = Number(element, "max", -1e9, 1e9, 100);
                if (max <= min) throw new WidgetPackageException("plugins.error.value", "max", max);
                bool ring = type == WidgetElementType.Ring;
                return new WidgetElement
                {
                    Type = type,
                    Align = align,
                    Value = value,
                    Min = min,
                    Max = max,
                    Size = ring ? Number(element, "size", 24, 240, 64) : 0,
                    Thickness = ring ? Number(element, "thickness", 2, 24, 6) : Number(element, "height", 2, 24, Sizes.Bar),
                    Width = ring ? 0 : Number(element, "width", 0, MaxWidth, 0), // 0 = toute la largeur
                    Color = ColorName(element, "accent"),
                    Text = ring ? OptionalString(element, "text", MaxText) : null,
                };
            }
            default: // spacer
                Only(element, where, "type", "align", "size");
                return new WidgetElement { Type = type, Align = align, Size = Number(element, "size", 0, 200, 8) };
        }
    }

    // ------------------------------------------------------------------ valeurs

    /// <summary>Tailles de texte nommées (celles du TypeRamp d'Opaline).</summary>
    private static readonly Dictionary<string, double> TextSizes = new(StringComparer.Ordinal)
    {
        ["hero"] = TypeRamp.Hero, ["display"] = TypeRamp.Display, ["large-title"] = TypeRamp.LargeTitle, ["title1"] = TypeRamp.Title1,
        ["title2"] = TypeRamp.Title2, ["title3"] = TypeRamp.Title3, ["headline"] = TypeRamp.Headline, ["body"] = TypeRamp.Body,
        ["callout"] = TypeRamp.Callout, ["footnote"] = TypeRamp.Footnote, ["caption"] = TypeRamp.Caption, ["caption2"] = TypeRamp.Caption2,
    };

    private static readonly Dictionary<string, double> IconSizes = new(StringComparer.Ordinal)
    {
        ["xs"] = IconSize.XS, ["s"] = IconSize.S, ["m"] = IconSize.M, ["l"] = IconSize.L, ["xl"] = IconSize.XL, ["xxl"] = IconSize.XXL, ["huge"] = IconSize.Huge,
    };

    /// <summary>Couleurs nommées acceptées (en plus de #RRGGBB et #AARRGGBB).</summary>
    internal static readonly string[] ColorNames = ["accent", "accent-start", "accent-end", "text", "secondary", "tertiary", "white", "red", "orange", "green"];

    private static double FontSize(JsonElement element, string name, double fallback, Dictionary<string, double> named)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.String && named.TryGetValue(value.GetString()!, out var size)) return size;
        if (value.ValueKind == JsonValueKind.Number) return Number(element, name, 6, 160, fallback);
        throw new WidgetPackageException("plugins.error.value", name, Clip(value.ToString(), 20));
    }

    private static string ColorName(JsonElement element, string fallback = "text")
    {
        var color = OptionalString(element, "color", 12) ?? fallback;
        if (!ColorNames.Contains(color) && !HexColor().IsMatch(color)) throw new WidgetPackageException("plugins.error.value", "color", color);
        return color;
    }

    private static string Choice(JsonElement element, string name, string fallback, params string[] allowed)
    {
        var value = OptionalString(element, name, 12) ?? fallback;
        if (!allowed.Contains(value)) throw new WidgetPackageException("plugins.error.value", name, value);
        return value;
    }

    /// <summary>Refuse toute propriété inconnue (faute de frappe ou tentative de passer autre chose).</summary>
    private static void Only(JsonElement element, string where, params string[] allowed)
    {
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name))
                throw new WidgetPackageException("plugins.error.property", Clip(property.Name, 30), where.Length > 0 ? where : "widget");
    }

    private static string RequiredString(JsonElement element, string name, int max) =>
        OptionalString(element, name, max) ?? throw new WidgetPackageException("plugins.error.missing", name);

    private static string? OptionalString(JsonElement element, string name, int max)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new WidgetPackageException("plugins.error.type", name);
        var text = value.GetString() ?? "";
        if (text.Length > max) throw new WidgetPackageException("plugins.error.too_long", name, max);
        if (text.Any(c => char.IsControl(c) && c != '\n')) throw new WidgetPackageException("plugins.error.value", name, Clip(text, 20));
        return text;
    }

    private static double Number(JsonElement element, string name, double min, double max, double fallback)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsNaN(number))
            throw new WidgetPackageException("plugins.error.type", name);
        if (number < min || number > max) throw new WidgetPackageException("plugins.error.range", name, min, max);
        return number;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
