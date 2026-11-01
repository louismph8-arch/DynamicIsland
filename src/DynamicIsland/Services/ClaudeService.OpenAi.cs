using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// 3e moteur du mini assistant (priorité 3 n° 27, désactivé par défaut) : ChatGPT par l'API officielle d'OpenAI avec la clé de
/// l'utilisateur (payée à l'usage par son propriétaire). Appel HTTP direct, sans paquet en plus : « Responses API »
/// (POST /v1/responses, <c>stream: true</c>) lue en flux SSE ; <c>store: false</c> = OpenAI ne garde pas la conversation, elle
/// reste en mémoire dans Opaline et est renvoyée en vrais messages user / assistant. Aucun outil (ni web, ni fichiers).
/// </summary>
public static partial class ClaudeService
{
    private const string OpenAiBase = "https://api.openai.com/v1/";
    public const string OpenAiDefaultModel = "gpt-6.1-sol";
    /// <summary>Modèles OpenAI proposés (doc officielle des modèles, oct. 2026) ; Description = clé du texte.</summary>
    public static readonly (string Id, string Name, string Description)[] OpenAiModels =
    [
        ("gpt-6.1-sol", "GPT-6.1 Sol", "openai.model.sol"),
        ("gpt-6-astra", "GPT-6 Astra", "openai.model.astra"),
        ("gpt-6.1-terra", "GPT-6.1 Terra", "openai.model.terra"),
    ];

    /// <summary>Pas de délai propre : la question a déjà son délai maximal (3 min) et « Arrêter ».</summary>
    private static readonly HttpClient OpenAiHttp = new() { BaseAddress = new Uri(OpenAiBase), Timeout = Timeout.InfiniteTimeSpan };

    private static async Task RunOpenAiAsync(ClaudeTurn turn, string question, List<ClaudeTurn> history, CancellationToken token)
    {
        var key = ApiKeys.OpenAi.Load() ?? throw new ClaudeException(Loc.F("claude.api.no_key", "OpenAI"), ClaudeFix.ApiKey);
        var input = new JsonArray();
        foreach (var past in history)
        {
            input.Add(new JsonObject { ["role"] = "user", ["content"] = past.Question });
            input.Add(new JsonObject { ["role"] = "assistant", ["content"] = HistoryAnswer(past) });
        }
        input.Add(new JsonObject { ["role"] = "user", ["content"] = question });
        var body = new JsonObject
        {
            ["model"] = SettingsStore.Current.Claude.OpenAiModel,
            ["instructions"] = SystemPrompt(),
            ["input"] = input,
            ["stream"] = true,
            ["store"] = false,
            ["max_output_tokens"] = ApiMaxTokens,
            // Réponses courtes dans l'île : peu de réflexion (comme « effort low » pour Claude).
            ["reasoning"] = new JsonObject { ["effort"] = "low" },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        try
        {
            using var response = await OpenAiHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw ExplainOpenAi((int)response.StatusCode, await response.Content.ReadAsStringAsync(token));

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(token) is { } line)
            {
                var sse = ReadOpenAiLine(line);
                if (sse.Delta is { Length: > 0 } delta)
                {
                    turn.Answer += delta;
                    Raise();
                }
                if (sse.Error != null)
                {
                    turn.Answer = ""; // l'erreur s'affiche à part, en clair
                    throw sse.Error;
                }
                if (sse.Done) break;
            }
        }
        catch (HttpRequestException) when (!token.IsCancellationRequested)
        {
            turn.Answer = "";
            throw new ClaudeException(Loc.T("openai.api.offline"));
        }
        catch (IOException) when (!token.IsCancellationRequested)
        {
            if (turn.Answer.Length == 0) throw new ClaudeException(Loc.T("openai.api.offline"));
            turn.Answer += " …"; // coupure en cours de réponse : on garde ce qui est arrivé
        }
        if (turn.Answer.Length == 0) throw new ClaudeException(Loc.T("openai.api.empty"));
    }

    /// <summary>Une ligne du flux SSE : morceau de texte, erreur, ou fin.</summary>
    internal readonly record struct OpenAiLine(string? Delta, ClaudeException? Error, bool Done);

    /// <summary>
    /// Lit une ligne « data: {json} » du flux de la Responses API : <c>response.output_text.delta</c> (texte),
    /// <c>response.completed</c> / <c>response.incomplete</c> (fin), <c>response.failed</c> et <c>error</c> (erreur). Les autres
    /// lignes (« event: … », vides, autres événements) ne donnent rien. Vérifié par <c>--self-test</c> sur un flux écrit à la main.
    /// </summary>
    internal static OpenAiLine ReadOpenAiLine(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal)) return default;
        var data = line[5..].Trim();
        if (data == "[DONE]") return new OpenAiLine(null, null, true);
        try
        {
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            switch (Text(root, "type"))
            {
                case "response.output_text.delta":
                    return new OpenAiLine(Text(root, "delta"), null, false);
                case "response.completed":
                case "response.incomplete":
                    return new OpenAiLine(null, null, true);
                case "response.failed":
                    var error = root.TryGetProperty("response", out var r) && r.TryGetProperty("error", out var e) ? e : default;
                    return new OpenAiLine(null, ExplainOpenAiCode(Text(error, "code"), Text(error, "message")), true);
                case "error":
                    // Forme documentée : code / message à la racine ; certaines versions les mettent dans « error ».
                    var inner = root.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
                    return new OpenAiLine(null, ExplainOpenAiCode(Text(inner, "code"), Text(inner, "message")), true);
                default:
                    return default;
            }
        }
        catch (JsonException)
        {
            return default; // ligne inattendue : ignorée
        }
    }

    /// <summary>« Vérifier la clé » : liste des modèles (GET /v1/models, gratuit, ne pose aucune question). Null si la clé marche.</summary>
    public static async Task<string?> CheckOpenAiKeyAsync(string key)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await OpenAiHttp.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode) return null;
            return ExplainOpenAi((int)response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token)).Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return Loc.T("openai.api.offline");
        }
    }

    /// <summary>Réponse HTTP en erreur (corps JSON <c>{"error":{"code","message","type"}}</c>) → message clair.</summary>
    internal static ClaudeException ExplainOpenAi(int status, string body)
    {
        string code = "", message = "";
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                code = Text(error, "code");
                if (code.Length == 0) code = Text(error, "type");
                message = Text(error, "message");
            }
        }
        catch (JsonException)
        {
            // Corps illisible (proxy, page HTML…) : on s'en tient au code HTTP.
        }
        return status switch
        {
            (int)HttpStatusCode.Unauthorized => new ClaudeException(Loc.T("openai.api.bad_key"), ClaudeFix.ApiKey),
            (int)HttpStatusCode.Forbidden => new ClaudeException(Loc.T("claude.api.forbidden"), ClaudeFix.ApiKey),
            (int)HttpStatusCode.NotFound => new ClaudeException(Loc.F("claude.api.model", ModelName(SettingsStore.Current.Claude.OpenAiModel)), ClaudeFix.ApiKey),
            >= 500 => new ClaudeException(Loc.T("openai.api.overloaded")),
            429 when code is not ("insufficient_quota" or "billing_hard_limit_reached") => new ClaudeException(Loc.T("claude.api.rate_limit")),
            _ => ExplainOpenAiCode(code, message),
        };
    }

    /// <summary>Code d'erreur d'OpenAI (dans le corps ou dans le flux) → message clair.</summary>
    private static ClaudeException ExplainOpenAiCode(string code, string message) => code switch
    {
        "invalid_api_key" or "invalid_authentication" => new ClaudeException(Loc.T("openai.api.bad_key"), ClaudeFix.ApiKey),
        "insufficient_quota" or "billing_hard_limit_reached" => new ClaudeException(Loc.T("openai.api.no_credit"), ClaudeFix.ApiKey),
        "rate_limit_exceeded" => new ClaudeException(Loc.T("claude.api.rate_limit")),
        "model_not_found" => new ClaudeException(Loc.F("claude.api.model", ModelName(SettingsStore.Current.Claude.OpenAiModel)), ClaudeFix.ApiKey),
        "server_error" or "server_is_overloaded" => new ClaudeException(Loc.T("openai.api.overloaded")),
        _ => new ClaudeException(message.Length > 0 ? Loc.F("claude.error.failed_named", "ChatGPT", message.Split('\n')[0])
                                                    : Loc.T("openai.api.empty")),
    };
}
