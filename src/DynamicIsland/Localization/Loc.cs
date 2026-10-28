using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DynamicIsland;

/// <summary>
/// Textes de l'interface en français ou en anglais (6A). Fichiers de langue <c>Localization/fr.json</c> et
/// <c>Localization/en.json</c> (embarqués dans l'exe) : une clé → un texte, avec <c>{0}</c>, <c>{1}</c>… pour les
/// valeurs. Langue réglée dans les paramètres (« auto » = celle de Windows), appliquée au démarrage : en changer
/// redémarre l'app. Une clé absente en anglais affiche le français ; absente partout, la clé elle-même (repérable).
/// XAML : <c>{ui:L clé}</c> ; code : <c>Loc.T("clé")</c> ou <c>Loc.F("clé", valeurs…)</c>.
/// </summary>
public static class Loc
{
    public const string French = "fr";
    public const string English = "en";
    public static readonly string[] Languages = [French, English];

    private static Dictionary<string, string>? _texts;
    private static Dictionary<string, string>? _french;

    /// <summary>Langue affichée (« fr » ou « en »).</summary>
    public static string Language { get; private set; } = French;

    /// <summary>Culture des dates, des nombres et des noms de jours / mois de la langue affichée.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Au démarrage, d'après le réglage (« auto », « fr » ou « en »).</summary>
    public static void Initialize(string? setting)
    {
        Language = Resolve(setting);
        Culture = CultureInfo.GetCultureInfo(Language == French ? "fr-FR" : "en-US");
        _french = Read(French);
        _texts = Language == French ? _french : Read(Language);
    }

    /// <summary>« auto » → français si Windows est en français, sinon anglais.</summary>
    public static string Resolve(string? setting) =>
        setting is French or English ? setting : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == French ? French : English;

    public static string T(string key)
    {
        if (_texts == null) Initialize(French); // textes demandés avant le démarrage complet (rendus, modes développeur)
        return _texts!.TryGetValue(key, out var text) ? text : _french!.TryGetValue(key, out var french) ? french : key;
    }

    /// <summary>Texte avec valeurs : <c>{0}</c>, <c>{1}</c>… formatés dans la culture de la langue.</summary>
    public static string F(string key, params object?[] values)
    {
        try
        {
            return string.Format(Culture, T(key), values);
        }
        catch (FormatException)
        {
            return T(key); // fichier de langue mal formé : on montre au moins le texte
        }
    }

    /// <summary>Toutes les clés d'une langue (vérification des fichiers par --self-test).</summary>
    internal static IReadOnlyDictionary<string, string> All(string language) => Read(language);

    private static Dictionary<string, string> Read(string language)
    {
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"DynamicIsland.Localization.{language}.json");
            if (stream == null) return [];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Error($"Langue « {language} »", ex);
            return [];
        }
    }
}
