using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Palette;

/// <summary>
/// Palette : « tr texte », « traduire texte », « translate text » (+ « en anglais : … », « to German: … », « es: … ») → l'île
/// s'ouvre sur l'onglet Traduire et traduit ; « tr https://… » → la page dans Google Traduction ; la commande seule → traduire ce
/// qui a été copié, ou ouvrir l'onglet.
/// </summary>
public static partial class PaletteEngine
{
    [GeneratedRegex(@"^(?:tr|trad|traduire|traduis|traduction|translate|translation)(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TranslateCommand();

    // « en anglais : … », « vers l'allemand : … », « to German: … », « into es: … », « es: … »
    [GeneratedRegex(@"^(?:(?:en|in|to|into|vers(?:\s+l['’]|\s+le|\s+la)?)\s*)?(?<lang>[\p{L}-]{2,20})\s*:\s*(?<text>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TranslateTarget();

    /// <summary>Noms des langues reconnus dans la palette (français, anglais, et le nom dans la langue elle-même).</summary>
    private static readonly (string Code, string[] Names)[] LanguageNames =
    [
        ("fr", ["francais", "french", "francaise"]), ("en", ["anglais", "english", "anglaise"]), ("es", ["espagnol", "spanish", "espanol"]),
        ("de", ["allemand", "german", "deutsch"]), ("it", ["italien", "italian", "italiano"]), ("pt", ["portugais", "portuguese", "portugues"]),
        ("nl", ["neerlandais", "hollandais", "dutch", "nederlands"]), ("pl", ["polonais", "polish", "polski"]), ("ru", ["russe", "russian"]),
        ("uk", ["ukrainien", "ukrainian"]), ("tr", ["turc", "turkish", "turkce"]), ("ar", ["arabe", "arabic"]),
        ("ja", ["japonais", "japanese"]), ("zh-CN", ["chinois", "chinese", "mandarin"]), ("ko", ["coreen", "korean"]),
    ];

    /// <summary>« anglais », « English », « en », « zh » → code de la langue ; null si inconnu.</summary>
    internal static string? LanguageFromWord(string word)
    {
        var simple = Plain(word);
        if (TranslateService.Find(simple) is { } byCode && simple.Length <= 5) return byCode.Code;
        foreach (var (code, names) in LanguageNames)
            if (names.Contains(simple)) return code;
        return TranslateService.Languages.FirstOrDefault(l => Plain(l.Name) == simple)?.Code;
    }

    /// <summary>Minuscules sans accents.</summary>
    private static string Plain(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString();
    }

    /// <summary>
    /// Lit une commande de traduction : (texte, langue cible ou null, adresse web ou null). Null si ce n'en est pas une. Texte vide =
    /// la commande seule. Vérifié par <c>--self-test</c>.
    /// </summary>
    internal static (string Text, string? Target, string? Url)? ParseTranslate(string query)
    {
        var command = TranslateCommand().Match(query.Trim());
        if (!command.Success) return null;
        var rest = command.Groups["rest"].Value.Trim();
        if (rest.Length == 0) return ("", null, null);
        string? target = null;
        var aimed = TranslateTarget().Match(rest);
        if (aimed.Success && LanguageFromWord(aimed.Groups["lang"].Value) is { } code && !rest.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            target = code;
            rest = aimed.Groups["text"].Value.Trim();
        }
        bool looksLikeUrl = !rest.Any(char.IsWhiteSpace) && (rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
        return looksLikeUrl ? ("", target, rest) : (rest, target, null);
    }

    private static void AddTranslate(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.TranslateTab) || actions.Translate is not { } translate) return;
        var parsed = ParseTranslate(query);
        var accent = (Brush)Application.Current.FindResource(AppearanceService.AccentGradientKey);
        if (parsed is not { } command)
        {
            if (AppCatalog.MatchScore(Loc.T("palette.translate.alias"), query) >= 3 && actions.OpenTranslate is { } openTab)
                items.Add(new PaletteItem(Loc.T("palette.translate.open"), Loc.T("palette.translate.open.description"), openTab) { Glyph = Icons.Translate });
            return;
        }
        var target = command.Target ?? TranslateService.TargetCode;
        var targetName = TranslateService.NameOf(target);
        if (command.Url is { } url)
        {
            var problem = TranslateService.Check(url, out _);
            items.Add(new PaletteItem(Loc.T("palette.translate.page"),
                problem == PageProblem.None ? Loc.F("palette.translate.page.description", targetName) : TranslateService.Explain(problem),
                () => actions.TranslatePage?.Invoke(url)) { Glyph = Icons.Globe });
            return;
        }
        if (command.Text.Length == 0)
        {
            if (actions.TranslateClipboard is { } clipboard)
                items.Add(new PaletteItem(Loc.T("palette.translate.clipboard"), Loc.F("palette.translate.to", targetName, ClaudeService.AssistantName), clipboard)
                    { Glyph = Icons.Paste, GlyphBackground = accent });
            if (actions.OpenTranslate is { } open)
                items.Add(new PaletteItem(Loc.T("palette.translate.open"), Loc.T("palette.translate.open.description"), open) { Glyph = Icons.Translate });
            return;
        }
        var text = command.Text;
        var subtitle = text.Length > TranslateService.MaxChars
            ? Loc.F("translate.error.too_long", text.Length.ToString("N0", Loc.Culture), TranslateService.MaxChars.ToString("N0", Loc.Culture))
            : Loc.F("palette.translate.to", targetName, ClaudeService.AssistantName);
        items.Add(new PaletteItem(Loc.F("palette.translate.text", Shown(text)), subtitle, () => translate(text, command.Target))
            { Glyph = Icons.Translate, GlyphBackground = accent });
    }
}
