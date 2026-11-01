using System.Net.Http;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des moteurs « clé API » du mini assistant (6D-3 : Anthropic ; priorité 3 n° 27 : OpenAI) : jamais avec une vraie
/// clé. Les vraies clés de l'utilisateur (s'il en a) ne sont ni lues ni touchées ; une fausse clé est envoyée à chaque API pour
/// vérifier le message « clé refusée » (gratuit : rien n'est demandé à l'assistant).
/// </summary>
internal static partial class SelfTest
{
    private static void ClaudeApi(List<string> lines)
    {
        var defaults = new ClaudeSettings();
        Check(lines, defaults.Engine == ClaudeEngine.ClaudeCode && defaults.ApiModel == "claude-sonnet-5-5"
                     && defaults.OpenAiModel == ClaudeService.OpenAiDefaultModel,
            $"par défaut : Claude Code (les clés API sont des moteurs à choisir), Sonnet 5.5 / {ClaudeService.OpenAiDefaultModel} avec une clé");
        var odd = new ClaudeSettings { OpenAiModel = "gpt-inconnu", Engine = (ClaudeEngine)42 };
        odd.Normalize();
        Check(lines, odd.OpenAiModel == ClaudeService.OpenAiDefaultModel && odd.Engine == ClaudeEngine.ClaudeCode,
            "réglages inconnus (modèle OpenAI, moteur) ramenés aux valeurs par défaut");

        var anthropic = ApiKeys.Anthropic;
        Check(lines, anthropic.LooksValid("sk-ant-api03-" + new string('x', 40)) && !anthropic.LooksValid("sk-test-123")
                     && !anthropic.LooksValid("sk-ant-api03- avec espaces " + new string('x', 30)) && !anthropic.LooksValid(""),
            "forme d'une clé Anthropic reconnue (sk-ant-…), le reste refusé avant tout envoi");
        var openAi = ApiKeys.OpenAi;
        Check(lines, openAi.LooksValid("sk-proj-" + new string('x', 60)) && openAi.LooksValid("sk-svcacct-" + new string('y', 60))
                     && !openAi.LooksValid("sk-ant-api03-" + new string('x', 40)) && !openAi.LooksValid("sk-court")
                     && !openAi.LooksValid("sk-proj- avec espaces " + new string('x', 30)) && !openAi.LooksValid(""),
            "forme d'une clé OpenAI reconnue (sk-proj-…, sk-svcacct-…), une clé Anthropic ou mal formée refusée");
        Check(lines, ApiKeys.For(ClaudeEngine.ApiKey) == anthropic && ApiKeys.For(ClaudeEngine.OpenAi) == openAi
                     && ApiKeys.For(ClaudeEngine.ClaudeCode) == null,
            "une clé par fournisseur (fichiers séparés), aucune pour Claude Code");

        var offline = ClaudeService.ExplainApi(new HttpRequestException("pas de réseau"));
        Check(lines, offline.Fix == ClaudeFix.None && offline.Message == Loc.T("claude.api.offline"), $"hors ligne → « {offline.Message} »");

        // Flux SSE de la Responses API d'OpenAI, écrit à la main d'après la documentation.
        string[] stream =
        [
            "event: response.created",
            """data: {"type":"response.created","sequence_number":0,"response":{"id":"resp_1","status":"in_progress"}}""",
            "",
            "event: response.output_text.delta",
            """data: {"type":"response.output_text.delta","item_id":"msg_1","output_index":0,"content_index":0,"delta":"Bonjour","sequence_number":1}""",
            "",
            """data: {"type":"response.output_text.delta","item_id":"msg_1","output_index":0,"content_index":0,"delta":" Louis « é » !","sequence_number":2}""",
            """data: {"type":"response.output_text.done","item_id":"msg_1","text":"Bonjour Louis « é » !","sequence_number":3}""",
            "data: pas du json",
            """data: {"type":"response.completed","sequence_number":4,"response":{"id":"resp_1","status":"completed"}}""",
        ];
        string text = "";
        bool done = false, error = false;
        foreach (var line in stream)
        {
            var read = ClaudeService.ReadOpenAiLine(line);
            text += read.Delta ?? "";
            error |= read.Error != null;
            if (read.Done) { done = true; break; }
        }
        Check(lines, text == "Bonjour Louis « é » !" && done && !error, $"flux SSE d'OpenAI lu : « {text} », fin reconnue, lignes inconnues ignorées");
        var failed = ClaudeService.ReadOpenAiLine("""data: {"type":"response.failed","response":{"error":{"code":"rate_limit_exceeded","message":"Slow down"}}}""");
        var quota = ClaudeService.ReadOpenAiLine("""data: {"type":"error","code":"insufficient_quota","message":"You exceeded your current quota"}""");
        Check(lines, failed is { Done: true, Error: { } f } && f.Message == Loc.T("claude.api.rate_limit")
                     && quota.Error?.Message == Loc.T("openai.api.no_credit") && quota.Error.Fix == ClaudeFix.ApiKey,
            "erreurs dans le flux (response.failed, error) traduites : trop de questions, plus de crédit");
        var http401 = ClaudeService.ExplainOpenAi(401, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}""");
        var http429 = ClaudeService.ExplainOpenAi(429, """{"error":{"message":"Rate limit","type":"requests","code":"rate_limit_exceeded"}}""");
        var http500 = ClaudeService.ExplainOpenAi(503, "<html>indisponible</html>");
        Check(lines, http401.Message == Loc.T("openai.api.bad_key") && http429.Message == Loc.T("claude.api.rate_limit")
                     && http500.Message == Loc.T("openai.api.overloaded"),
            "réponses HTTP d'erreur d'OpenAI traduites (401 clé refusée, 429 trop de questions, 503 surcharge)");

        // Historique de la vraie conversation : 20 échanges au plus, les plus anciens retirés d'abord, et une limite de taille.
        var turns = Enumerable.Range(1, 30).Select(i => new ClaudeTurn($"question {i}") { Answer = $"réponse {i}", Done = true }).ToList();
        var history = ClaudeService.ApiHistory(turns);
        var big = Enumerable.Range(1, 10).Select(i => new ClaudeTurn($"q{i}") { Answer = new string('a', 5000), Done = true }).ToList();
        var bigHistory = ClaudeService.ApiHistory(big);
        Check(lines, history.Count == ClaudeService.ApiHistoryTurns && history[0].Question == "question 11" && history[^1].Question == "question 30"
                     && bigHistory.Count == 7 && bigHistory[^1].Question == "q10" && ClaudeService.ApiHistory([]).Count == 0,
            $"historique tronqué : {history.Count} échanges sur 30 (les plus récents, dans l'ordre), {bigHistory.Count} longs échanges sur 10 (≤ {ClaudeService.ApiHistoryChars} caractères)");

        var refused = ClaudeService.CheckApiKeyAsync("sk-ant-api03-OpalineSelfTestFausseCle" + new string('0', 40)).GetAwaiter().GetResult();
        Check(lines, refused == Loc.T("claude.api.bad_key") || refused == Loc.T("claude.api.offline"),
            $"fausse clé envoyée à l'API d'Anthropic (kit officiel « Anthropic ») → « {refused ?? "acceptée ?!"} »");
        var refusedOpenAi = ClaudeService.CheckOpenAiKeyAsync("sk-proj-OpalineSelfTestFausseCle" + new string('0', 40)).GetAwaiter().GetResult();
        Check(lines, refusedOpenAi == Loc.T("openai.api.bad_key") || refusedOpenAi == Loc.T("openai.api.offline"),
            $"fausse clé envoyée à l'API d'OpenAI (GET /v1/models) → « {refusedOpenAi ?? "acceptée ?!"} »");
    }
}
