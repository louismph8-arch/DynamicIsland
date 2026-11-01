using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Plugins;

/// <summary>
/// Données qu'un widget peut afficher : UNIQUEMENT celles de la liste blanche ci-dessous (ce qu'Opaline montre déjà), plus les
/// champs de sa source web autorisée (« web.nom »). Rien d'autre n'est accessible : pas de fichiers, pas de réglages.
/// </summary>
public interface IWidgetData
{
    /// <summary>Texte d'une clé connue (null = clé inconnue : le gabarit reste affiché tel quel).</summary>
    string? Text(string key, string? format);

    /// <summary>Valeur numérique (jauges, anneaux), null si indisponible.</summary>
    double? Number(string key);
}

/// <summary>Gabarits « {clé} » / « {clé:format} » dans les textes des widgets. Calcul pur, vérifié par --self-test.</summary>
public static partial class WidgetTemplate
{
    /// <summary>Clés texte de la liste blanche (time et date acceptent un format : {time:HH:mm}, {date:dddd}).</summary>
    public static readonly string[] TextKeys =
    [
        "time", "date",
        "weather.temp", "weather.min", "weather.max", "weather.city", "weather.desc", "weather.icon",
        "music.title", "music.artist", "music.app",
        "cpu", "ram", "gpu", "gpu.temp", "net.down", "net.up",
        "friends.online",
    ];

    /// <summary>Clés numériques (jauges, anneaux, barres), en plus des champs de la source web.</summary>
    public static readonly string[] NumberKeys = ["cpu", "ram", "gpu", "gpu.temp", "weather.temp", "friends.online"];

    [GeneratedRegex(@"\{([a-z][a-z0-9_]*(?:\.[a-z0-9_]+)?)(?::([^{}]{1,24}))?\}")]
    private static partial Regex Placeholder();

    /// <summary>Format de date / heure accepté (lettres de format et ponctuation seulement).</summary>
    [GeneratedRegex(@"^[dMyHhmstf :/.,'\-]{1,24}$")]
    private static partial Regex DateFormat();

    /// <summary>Format de nombre accepté : « 0 », « 0.00 », « n0 »… « n4 », « f2 », « p1 ».</summary>
    [GeneratedRegex(@"^(?:[nfp][0-4]|0(?:\.0{1,4})?)$")]
    private static partial Regex NumberFormat();

    public static IEnumerable<(string Key, string? Format)> Placeholders(string text)
    {
        foreach (Match match in Placeholder().Matches(text))
            yield return (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null);
    }

    /// <summary>Remplit les gabarits connus ; un gabarit inconnu (ou indisponible) reste affiché tel quel.</summary>
    public static string Fill(string text, IWidgetData data) => Placeholder().Replace(text, match =>
        data.Text(match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null) ?? match.Value);

    public static bool IsNumberKey(string key, WebSourceDefinition? source) =>
        NumberKeys.Contains(key) || (key.StartsWith("web.", StringComparison.Ordinal) && source?.Fields.ContainsKey(key[4..]) == true);

    /// <summary>Format de date sûr (sinon celui par défaut).</summary>
    public static string DateFormatOr(string? format, string fallback) => format != null && DateFormat().IsMatch(format) ? format : fallback;

    /// <summary>Nombre mis en forme selon la culture d'Opaline, avec un format sûr (sinon le texte brut).</summary>
    public static string FormatNumber(double value, string? format, CultureInfo culture) =>
        format != null && NumberFormat().IsMatch(format) ? value.ToString(format, culture) : value.ToString("0.##", culture);
}

/// <summary>
/// Données d'un widget : la liste blanche commune + les champs de SA source web. Un champ déclaré mais pas encore lu (ou
/// site pas autorisé) s'affiche « — » ; un champ qui n'est pas déclaré reste affiché tel quel, comme tout gabarit inconnu.
/// </summary>
public sealed class ScopedWidgetData(IWidgetData shared, IReadOnlyDictionary<string, string>? web, WebSourceDefinition? source) : IWidgetData
{
    public string? Text(string key, string? format)
    {
        if (!key.StartsWith("web.", StringComparison.Ordinal)) return shared.Text(key, format);
        if (web == null || !web.TryGetValue(key[4..], out var raw)) return source?.Fields.ContainsKey(key[4..]) == true ? "—" : null;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && format != null
            ? WidgetTemplate.FormatNumber(number, format, Loc.Culture)
            : raw;
    }

    public double? Number(string key)
    {
        if (!key.StartsWith("web.", StringComparison.Ordinal)) return shared.Number(key);
        return web != null && web.TryGetValue(key[4..], out var raw)
               && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

/// <summary>
/// Chemins simples dans une réponse JSON : « $.data.price », « $.items[0].name ». Pas de filtres, pas de jokers : on va
/// droit au champ. Calcul pur, vérifié par --self-test.
/// </summary>
public static partial class JsonPath
{
    [GeneratedRegex(@"^\$(?:\.[A-Za-z_][A-Za-z0-9_\-]{0,63}|\[[0-9]{1,4}\]){1,12}$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\.([A-Za-z_][A-Za-z0-9_\-]{0,63})|\[([0-9]{1,4})\]")]
    private static partial Regex Step();

    public static bool IsValid(string? path) => path != null && Pattern().IsMatch(path);

    /// <summary>Valeur au bout du chemin (nombre en écriture invariante, texte coupé à 200 caractères), ou null.</summary>
    public static string? Extract(JsonElement root, string path)
    {
        if (!IsValid(path)) return null;
        var current = root;
        foreach (Match step in Step().Matches(path))
        {
            if (step.Groups[1].Success)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(step.Groups[1].Value, out current)) return null;
            }
            else
            {
                int index = int.Parse(step.Groups[2].Value, CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength()) return null;
                current = current[index];
            }
        }
        return current.ValueKind switch
        {
            JsonValueKind.Number => current.TryGetDouble(out var number) ? number.ToString("R", CultureInfo.InvariantCulture) : null,
            JsonValueKind.String => current.GetString() is { } text ? (text.Length <= WidgetPackageReader.MaxText ? text : text[..WidgetPackageReader.MaxText]) : null,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null, // objet, tableau, null : rien à afficher
        };
    }
}
