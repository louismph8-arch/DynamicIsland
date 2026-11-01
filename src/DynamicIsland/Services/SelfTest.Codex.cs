using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du moteur « ChatGPT · abonnement (Codex) » : Codex est cherché SANS être lancé, aucune question n'est posée et
/// « codex login » n'est jamais appelé. Le flux JSON est écrit à la main d'après la doc (codex-rs/exec/src/exec_events.rs).
/// </summary>
internal static partial class SelfTest
{
    private static void Codex(List<string> lines)
    {
        var defaults = new ClaudeSettings();
        Check(lines, defaults.Engine == ClaudeEngine.ClaudeCode && defaults.CodexModel == "", "par défaut : Claude Code ; avec Codex, le modèle par défaut de Codex");
        var odd = new ClaudeSettings { Engine = (ClaudeEngine)42, CodexModel = "  gpt-x  " };
        odd.Normalize();
        Check(lines, odd.Engine == ClaudeEngine.ClaudeCode && odd.CodexModel == "gpt-x", "moteur inconnu → Claude Code ; nom de modèle nettoyé");

        var found = ClaudeService.FindCodex();
        Check(lines, found == null || System.IO.File.Exists(found.Value.Program) && (found.Value.Script == null || System.IO.File.Exists(found.Value.Script)),
            found is { } f ? $"Codex trouvé sans le lancer ({f.Source}) : {System.IO.Path.GetFileName(f.Program)}{(f.Script != null ? " + codex.js" : "")}" : "Codex introuvable sur ce PC (le moteur proposera de l'installer)");

        var models = ClaudeService.CodexModels();
        Check(lines, models.Count > 0 && models[0].Id == "" && models.Skip(1).All(m => m.Id.Length > 0),
            $"modèles : « par défaut » puis {models.Count - 1} listé(s) par Codex pour ce compte");

        const string question = "Question secrète 123";
        var args = ClaudeService.CodexExecArguments("gpt-test", "Consigne \"entre guillemets\" \\ fin");
        string joined = string.Join(" ", args);
        Check(lines, args[0] == "exec" && args[^1] == "-" && args.Contains("--json") && args.Contains("--ephemeral") && args.Contains("--ignore-user-config")
                     && args.Contains("--ignore-rules") && args.Contains("--skip-git-repo-check")
                     && args.IndexOf("--sandbox") is var s and >= 0 && args[s + 1] == "read-only"
                     && args.Contains("approval_policy=\"never\"") && args.Contains("web_search=\"disabled\"") && args.Contains("features.shell_tool=false")
                     && args.IndexOf("--model") is var m and >= 0 && args[m + 1] == "gpt-test" && !joined.Contains(question),
            "codex exec : JSON, rien gardé, lecture seule, sans approbation, sans commandes ni web, question par l'entrée standard (« - »)");
        Check(lines, !ClaudeService.CodexExecArguments("", "x").Contains("--model"), "modèle par défaut : pas de --model");
        Check(lines, ClaudeService.TomlString("a \"b\" \\ c\nd") == "\"a \\\"b\\\" \\\\ c\\nd\"", "consigne écrite en chaîne TOML (guillemets, \\, retours à la ligne)");

        var stream = new CodexStream();
        string[] sample =
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"0199a213-81c0-7800-8aa1-bbab2a035a53\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"reasoning\",\"text\":\"**Je réfléchis**\"}}",
            "{\"type\":\"error\",\"message\":\"Reconnecting... 1/5\"}",
            "pas du JSON",
            "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"dir\",\"aggregated_output\":\"\",\"exit_code\":null,\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"La RAM est rapide.\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_3\",\"type\":\"agent_message\",\"text\":\"Le SSD garde tout.\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":24763,\"cached_input_tokens\":24448,\"output_tokens\":122,\"reasoning_output_tokens\":0}}",
        ];
        int changes = sample.Count(stream.Feed);
        Check(lines, stream.Answer == "La RAM est rapide.\n\nLe SSD garde tout." && changes == 2 && stream.Completed && stream.Failure == null
                     && stream.LastError == "Reconnecting... 1/5",
            "flux JSON : messages de l'agent dans l'ordre, réflexion / commandes / lignes inattendues ignorées");

        var failed = new CodexStream();
        failed.Feed("{\"type\":\"turn.started\"}");
        failed.Feed("{\"type\":\"turn.failed\",\"error\":{\"message\":\"You've hit your usage limit. Upgrade to Pro or try again in 2 hours.\"}}");
        var limit = ClaudeService.ExplainCodex(failed.Failure ?? "");
        Check(lines, failed.Answer == "" && limit.Fix == ClaudeFix.None && limit.Message.Contains("2 hours"), $"limite de l'abonnement → « {limit.Message} »");

        var login = ClaudeService.ExplainCodex("unexpected status 401 Unauthorized: {\"detail\":\"Could not parse your authentication token.\"}");
        Check(lines, login.Fix == ClaudeFix.CodexLogin && login.Message == Loc.T("codex.error.not_logged_in"), $"pas connecté → « {login.Message} » + bouton de connexion");
        var model = ClaudeService.ExplainCodex("{\"detail\":\"The 'gpt-test' model is not supported when using Codex with a ChatGPT account.\"}");
        Check(lines, model.Fix == ClaudeFix.None && !model.Message.Contains('{'), $"modèle refusé → « {model.Message} »");
        var old = ClaudeService.ExplainCodex("This model requires a newer version of Codex. Please upgrade.");
        Check(lines, old.Fix == ClaudeFix.CodexUpdate, $"Codex trop ancien → « {old.Message} » + bouton de mise à jour");
        var other = ClaudeService.ExplainCodex("stream error: {\"error\":{\"message\":\"Server overloaded\"}}");
        Check(lines, other.Fix == ClaudeFix.None && other.Message.EndsWith("Server overloaded") && !other.Message.Contains('{'), $"autre erreur → « {other.Message} » (sans JSON)");

        Check(lines, ClaudeService.CodexStatusFrom("Logged in using ChatGPT\n", 0) is { Installed: true, LoggedIn: true }
                     && ClaudeService.CodexStatusFrom("Not logged in\n", 1) is { LoggedIn: false }
                     && ClaudeService.CodexStatusFrom("Logged in using an API key - sk-proj-***ABCD\n", 0).Description == Loc.T("codex.status.api_key")
                     && ClaudeService.CodexStatusFrom("", 1) is { LoggedIn: false },
            "« codex login status » lu (ChatGPT, clé API signalée, pas connecté)");
    }
}
