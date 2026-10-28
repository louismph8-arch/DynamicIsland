using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using System.Net.Http;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// 2e moteur du mini Claude (6D-3, désactivé par défaut) : l'API d'Anthropic avec la clé de l'utilisateur (payée à l'usage
/// par son propriétaire), par le kit officiel « Anthropic » pour C#. Pour les potes qui n'ont pas Claude Code. Même île, même
/// consigne, mêmes questions de suite (les derniers échanges sont renvoyés comme conversation) ; pas d'outils.
/// </summary>
public static partial class ClaudeService
{
    /// <summary>Modèle par défaut avec une clé API (choix de l'utilisateur : bon et nettement moins cher qu'Opus).</summary>
    public const string ApiDefaultModel = "claude-sonnet-5-5";
    private const int ApiMaxTokens = 4096;

    private static async Task RunApiAsync(ClaudeTurn turn, string question, List<ClaudeTurn> history, CancellationToken token)
    {
        var key = ClaudeApiKey.Load() ?? throw new ClaudeException(Loc.T("claude.api.no_key"), ClaudeFix.ApiKey);
        var model = SettingsStore.Current.Claude.ApiModel;
        var messages = new List<MessageParam>();
        foreach (var past in history)
        {
            messages.Add(new UserMessageParam { Content = past.Question });
            messages.Add(new AssistantMessageParam
            {
                Content = past.Answer.Length > HistoryAnswerChars ? past.Answer[..HistoryAnswerChars] + "…" : past.Answer,
            });
        }
        messages.Add(new UserMessageParam { Content = question });

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = ApiMaxTokens,
            System = SystemPrompt(),
            Messages = messages,
            // Réponses courtes dans l'île : peu de réflexion, comme « --effort low » pour Claude Code (Haiku n'a pas ce réglage).
            OutputConfig = model.Contains("haiku", StringComparison.Ordinal) ? null : new OutputConfig { Effort = Effort.Low },
        };

        var client = new AnthropicClient { ApiKey = key };
        try
        {
            await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, cancellationToken: token))
            {
                if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                {
                    turn.Answer += text.Text;
                    Raise();
                }
            }
        }
        catch (Exception ex) when (ex is AnthropicException or HttpRequestException && !token.IsCancellationRequested)
        {
            turn.Answer = ""; // l'erreur s'affiche à part, en clair
            throw ExplainApi(ex);
        }
        if (turn.Answer.Length == 0) throw new ClaudeException(Loc.T("claude.api.empty"));
    }

    /// <summary>« Vérifier la clé » : liste des modèles (gratuit, ne pose aucune question). Null si la clé marche.</summary>
    public static async Task<string?> CheckApiKeyAsync(string key)
    {
        try
        {
            var client = new AnthropicClient { ApiKey = key };
            await client.Models.List();
            return null;
        }
        catch (Exception ex) when (ex is AnthropicException or HttpRequestException)
        {
            return ExplainApi(ex).Message;
        }
    }

    /// <summary>Message clair pour les erreurs de l'API (clé refusée, plus de crédit, trop de questions, surcharge, hors ligne).</summary>
    internal static ClaudeException ExplainApi(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => new ClaudeException(Loc.T("claude.api.bad_key"), ClaudeFix.ApiKey),
        AnthropicForbiddenException => new ClaudeException(Loc.T("claude.api.forbidden"), ClaudeFix.ApiKey),
        AnthropicRateLimitException => new ClaudeException(Loc.T("claude.api.rate_limit")),
        Anthropic5xxException => new ClaudeException(Loc.T("claude.api.overloaded")),
        AnthropicNotFoundException => new ClaudeException(Loc.F("claude.api.model", ModelName(SettingsStore.Current.Claude.ApiModel)), ClaudeFix.ApiKey),
        AnthropicBadRequestException when ex.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
            => new ClaudeException(Loc.T("claude.api.no_credit"), ClaudeFix.ApiKey),
        AnthropicIOException or HttpRequestException => new ClaudeException(Loc.T("claude.api.offline")),
        _ => new ClaudeException(Loc.F("claude.error.failed", ex.Message.Split('\n')[0])),
    };
}
