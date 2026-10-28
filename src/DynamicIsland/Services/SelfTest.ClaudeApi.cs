using System.Net.Http;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du moteur « clé API » du mini Claude (6D-3) : jamais avec une vraie clé. La vraie clé de l'utilisateur (s'il en a
/// une) n'est ni lue ni touchée ; une fausse clé est envoyée à l'API pour vérifier le kit officiel et le message « clé refusée »
/// (gratuit : rien n'est demandé à Claude).
/// </summary>
internal static partial class SelfTest
{
    private static void ClaudeApi(List<string> lines)
    {
        var defaults = new ClaudeSettings();
        Check(lines, defaults.Engine == ClaudeEngine.ClaudeCode && defaults.ApiModel == "claude-sonnet-5-5",
            "par défaut : Claude Code (la clé API est un 2e moteur à choisir), Sonnet 5.5 avec une clé");
        Check(lines, ClaudeApiKey.LooksValid("sk-ant-api03-" + new string('x', 40)) && !ClaudeApiKey.LooksValid("sk-test-123")
                     && !ClaudeApiKey.LooksValid("sk-ant-api03- avec espaces " + new string('x', 30)) && !ClaudeApiKey.LooksValid(""),
            "forme d'une clé Anthropic reconnue (sk-ant-…), le reste refusé avant tout envoi");
        var offline = ClaudeService.ExplainApi(new HttpRequestException("pas de réseau"));
        Check(lines, offline.Fix == ClaudeFix.None && offline.Message == Loc.T("claude.api.offline"), $"hors ligne → « {offline.Message} »");

        var refused = ClaudeService.CheckApiKeyAsync("sk-ant-api03-OpalineSelfTestFausseCle" + new string('0', 40)).GetAwaiter().GetResult();
        Check(lines, refused == Loc.T("claude.api.bad_key") || refused == Loc.T("claude.api.offline"),
            $"fausse clé envoyée à l'API (kit officiel « Anthropic ») → « {refused ?? "acceptée ?!"} »");
    }
}
