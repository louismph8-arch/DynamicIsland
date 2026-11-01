using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Moteur « ChatGPT · abonnement (Codex) » du mini assistant : l'outil officiel d'OpenAI « codex » installé sur le PC, connecté
/// au compte ChatGPT de l'utilisateur (« Sign in with ChatGPT » : aucune clé, chaque question compte dans les limites de son
/// abonnement Plus / Pro…). Comme pour Claude Code : une question = un « codex exec » lancé dans un dossier vide, SANS outils
/// (commandes, recherche web, images et applis coupées), bac à sable en lecture seule, aucune approbation, réglages et règles
/// de l'utilisateur ignorés (pas de hooks ni de « notify »), rien gardé sur disque (--ephemeral, historique coupé). La question
/// passe par l'entrée standard (« - »), jamais dans la ligne de commande ; la réponse arrive en JSON Lines (--json).
/// Doc : github.com/openai/codex (codex-rs/exec/src/exec_events.rs pour les événements), learn.chatgpt.com (non-interactive mode).
/// </summary>
public static partial class ClaudeService
{
    public const string CodexInstallUrl = "https://github.com/openai/codex";

    /// <summary>
    /// Modèles proposés : « Par défaut de Codex » (vide = le modèle conseillé par la version installée), puis ceux que Codex a
    /// listés pour CE compte (fichier models_cache.json qu'il tient à jour lui-même, visibilité « list »). Rien n'est demandé
    /// au réseau : sans ce fichier, seul le choix par défaut est proposé.
    /// </summary>
    public static List<(string Id, string Name, string Description)> CodexModels()
    {
        var list = new List<(string, string, string)> { ("", Loc.T("codex.model.default"), Loc.T("codex.model.default.desc")) };
        try
        {
            var file = Path.Combine(CodexHome(), "models_cache.json");
            if (!File.Exists(file)) return list;
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            if (!json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return list;
            foreach (var model in models.EnumerateArray())
            {
                var slug = Text(model, "slug");
                if (slug.Length == 0 || Text(model, "visibility") != "list" || list.Any(m => m.Item1 == slug)) continue;
                var name = Text(model, "display_name");
                list.Add((slug, name.Length > 0 ? name : slug, Text(model, "description")));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Info($"Codex : liste des modèles illisible ({ex.GetType().Name})");
        }
        return list;
    }

    public static string CodexModelName(string id) =>
        id.Length == 0 ? Loc.T("codex.model.default") : CodexModels().FirstOrDefault(m => m.Id == id).Name ?? id;

    /// <summary>Lance « codex exec --json » et lit le flux : chaque message de l'agent s'affiche dès qu'il arrive.</summary>
    private static async Task RunCodexAsync(ClaudeTurn turn, string prompt, CancellationToken token, AiRequest? request = null)
    {
        var notify = request?.Notify ?? Raise;
        var start = CodexExecCommand(request?.Model ?? SettingsStore.Current.Claude.CodexModel, request?.System ?? SystemPrompt())
                    ?? throw new ClaudeException(Loc.T("codex.error.not_installed"), ClaudeFix.CodexInstall);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(start) ?? throw new ClaudeException(Loc.T("codex.error.start"));
        using var kill = token.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var stream = new CodexStream();
        while (await process.StandardOutput.ReadLineAsync(token) is { } line)
        {
            if (!stream.Feed(line)) continue;
            turn.Answer = stream.Answer;
            notify();
        }
        await process.WaitForExitAsync(token);

        if (stream.Failure is { } failure)
        {
            turn.Answer = ""; // l'erreur s'affiche à part, en clair
            throw ExplainCodex(failure);
        }
        if (turn.Answer.Length == 0)
        {
            var stderr = (await errors).Trim();
            var last = stream.LastError ?? (stderr.Length > 0 ? stderr.Split('\n').Last().Trim() : null);
            throw last != null ? ExplainCodex(last) : new ClaudeException(Loc.F("codex.error.exit", process.ExitCode));
        }
    }

    /// <summary>
    /// Arguments de « codex exec » (vérifiés sur « codex exec --help », 0.165) : aucun outil, lecture seule, pas d'approbation,
    /// réglages de l'utilisateur ignorés (sa connexion, elle, reste celle de CODEX_HOME), rien gardé, question lue sur l'entrée.
    /// </summary>
    internal static ProcessStartInfo? CodexExecCommand(string model, string instructions) => CodexCommand(CodexExecArguments(model, instructions));

    internal static List<string> CodexExecArguments(string model, string instructions)
    {
        var arguments = new List<string>
        {
            "exec", "--json", "--color", "never", "--ephemeral", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules",
            "--sandbox", "read-only",
            "-c", "approval_policy=\"never\"",
            "-c", "web_search=\"disabled\"",
            "-c", "features.shell_tool=false",
            "-c", "features.unified_exec=false",
            "-c", "features.apps=false",
            "-c", "tools.view_image=false",
            "-c", "history.persistence=\"none\"",
            "-c", "project_doc_max_bytes=0",
            "-c", "model_reasoning_effort=\"low\"",
            "-c", "developer_instructions=" + TomlString(instructions),
        };
        if (model.Length > 0) arguments.AddRange(["--model", model]);
        arguments.Add("-"); // la question arrive par l'entrée standard
        return arguments;
    }

    /// <summary>Chaîne TOML « de base » (les valeurs de -c sont lues en TOML).</summary>
    internal static string TomlString(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c < ' ' => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }
        return text.Append('"').ToString();
    }

    /// <summary>
    /// Message clair pour les erreurs de Codex (pas connecté, limite de l'abonnement, modèle refusé, version trop ancienne).
    /// Vérifié par <c>--self-test</c> sur des messages écrits à la main.
    /// </summary>
    internal static ClaudeException ExplainCodex(string message)
    {
        var detail = CodexDetail(message);
        bool Has(params string[] words) => words.Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));
        if (Has("usage limit", "usage_limit", "rate limit", "rate_limit", "429 Too Many", "quota"))
            return new ClaudeException(Loc.F("codex.error.limit", detail));
        if (Has("newer version of Codex", "upgrade Codex", "update Codex", "requires a newer", "version is too old", "out of date"))
            return new ClaudeException(Loc.F("codex.error.too_old", CodexModelSuffix()), ClaudeFix.CodexUpdate);
        if (Has("not logged in", "codex login", "401", "unauthorized", "refresh token", "token_expired", "sign in again", "log in again", "authentication"))
            return new ClaudeException(Loc.T("codex.error.not_logged_in"), ClaudeFix.CodexLogin);
        if (Has("model is not supported", "not supported when using Codex", "model_not_found", "does not exist", "unknown model", "invalid model"))
            return new ClaudeException(Loc.F("codex.error.model", CodexModelSuffix()));
        return new ClaudeException(detail.Length > 0 ? Loc.F("codex.error.failed", detail) : Loc.T("codex.error.no_answer"));
    }

    /// <summary>« (GPT-…) » après « le modèle demandé », rien pour le modèle par défaut de Codex.</summary>
    private static string CodexModelSuffix() =>
        SettingsStore.Current.Claude.CodexModel is { Length: > 0 } id ? $" ({CodexModelName(id)})" : "";

    /// <summary>« unexpected status 400: {"detail":"…"} » → le texte, sans JSON.</summary>
    private static string CodexDetail(string message)
    {
        int brace = message.IndexOf('{');
        if (brace < 0) return message.Trim();
        try
        {
            using var json = JsonDocument.Parse(message[brace..]);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String && error.GetString() is { Length: > 0 } plain) return plain;
                if (Text(error, "message") is { Length: > 0 } inner) return inner;
            }
            foreach (var name in new[] { "message", "detail" })
                if (Text(root, name) is { Length: > 0 } text) return text;
        }
        catch (JsonException)
        {
            // Pas du JSON : le message tel quel.
        }
        var before = message[..brace].Trim().TrimEnd(':').Trim();
        return before.Length > 0 ? before : message.Trim();
    }

    // ------------------------------------------------------------------ Codex sur ce PC

    /// <summary>Installé ? connecté avec ChatGPT ? — « codex login status » ne consomme rien de l'abonnement.</summary>
    public static async Task<ClaudeStatus> CodexCheckAsync()
    {
        try
        {
            var start = CodexCommand(["login", "status"]);
            if (start == null) return new ClaudeStatus(false, false, Loc.T("codex.status.not_installed"));
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return CodexStatusFrom((await output) + "\n" + (await errors), process.ExitCode);
        }
        catch (Exception ex)
        {
            Log.Error("Codex : état de la connexion", ex);
            return new ClaudeStatus(true, false, Loc.F("codex.status.check_failed", ex.Message));
        }
    }

    /// <summary>Texte de « codex login status » (« Logged in using ChatGPT », « … an API key … », « Not logged in »).</summary>
    internal static ClaudeStatus CodexStatusFrom(string output, int exitCode)
    {
        if (output.Contains("Not logged in", StringComparison.OrdinalIgnoreCase))
            return new ClaudeStatus(true, false, Loc.T("codex.status.not_logged_in"));
        if (output.Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase))
            return new ClaudeStatus(true, true, Loc.T("codex.status.connected"));
        if (output.Contains("API key", StringComparison.OrdinalIgnoreCase) && output.Contains("Logged in", StringComparison.OrdinalIgnoreCase))
            return new ClaudeStatus(true, true, Loc.T("codex.status.api_key"));
        if (exitCode == 0 && output.Contains("Logged in", StringComparison.OrdinalIgnoreCase))
            return new ClaudeStatus(true, true, Loc.T("codex.status.connected"));
        return new ClaudeStatus(true, false, exitCode == 0 ? Loc.T("codex.status.unreadable") : Loc.T("codex.status.not_logged_in"));
    }

    /// <summary>« codex login » dans une console visible : l'utilisateur se connecte LUI-MÊME à ChatGPT dans son navigateur.</summary>
    public static void OpenCodexLogin() => OpenCodexConsole(Loc.T("codex.console.login"), ["login"]);

    /// <summary>« codex update » dans une console visible (Codex se met à jour lui-même).</summary>
    public static void OpenCodexUpdate() => OpenCodexConsole(Loc.T("codex.console.update"), ["update"]);

    /// <summary>Page officielle de Codex (installation) dans le navigateur.</summary>
    public static void OpenCodexInstall()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CodexInstallUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Codex : page d'installation", ex);
        }
    }

    private static void OpenCodexConsole(string title, string[] arguments)
    {
        var start = CodexCommand(arguments);
        if (start == null)
        {
            if (MessageBox.Show(Loc.T("codex.status.not_installed") + "\n\n" + Loc.T("codex.install.ask"), AppInfo.Name, MessageBoxButton.OKCancel,
                    MessageBoxImage.Information) == MessageBoxResult.OK) OpenCodexInstall();
            return;
        }
        var line = string.Join(" ", new[] { start.FileName }.Concat(start.ArgumentList).Select(value => $"\"{value}\""));
        var console = new ProcessStartInfo("cmd.exe", $"/s /k \"title {title} & {line}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = start.WorkingDirectory,
        };
        CleanCodexEnvironment(console);
        Process.Start(console)?.Dispose();
    }

    /// <summary>Commande pour Codex : l'exe s'il est trouvé, sinon « node codex.js » de l'installation npm (pas de cmd.exe).</summary>
    private static ProcessStartInfo? CodexCommand(IEnumerable<string> arguments)
    {
        if (FindCodex() is not { } found) return null;
        var start = new ProcessStartInfo(found.Program);
        if (found.Script != null) start.ArgumentList.Add(found.Script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WorkingDirectory = CodexWorkFolder();
        CleanCodexEnvironment(start);
        return start;
    }

    /// <summary>Dossier vide : Codex n'y trouve aucun projet ni AGENTS.md.</summary>
    private static string CodexWorkFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "Codex");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string CodexHome() =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    /// <summary>
    /// Toujours le compte ChatGPT de l'utilisateur : une clé API dans l'environnement (OPENAI_API_KEY, CODEX_API_KEY) ferait
    /// payer à l'usage à la place de l'abonnement ; les variables d'une session Codex qui aurait lancé l'app sont retirées
    /// (sauf CODEX_HOME, où est la connexion).
    /// </summary>
    private static void CleanCodexEnvironment(ProcessStartInfo start)
    {
        CleanEnvironment(start);
        foreach (var key in start.Environment.Keys.ToList())
        {
            if ((key.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && !key.Equals("CODEX_HOME", StringComparison.OrdinalIgnoreCase))
                || key.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        }
    }

    /// <summary>
    /// Codex sur ce PC, dans l'ordre : codex.exe du PATH (installation autonome), ~\.codex\bin ou ~\.local\bin, installation npm
    /// (« npm i -g @openai/codex » : son exe natif s'il est là, sinon node + bin\codex.js), puis le Codex de l'appli de bureau
    /// d'OpenAI (%LOCALAPPDATA%\OpenAI\Codex\bin\…\codex.exe, le plus récent). Rien n'est lancé pour chercher.
    /// </summary>
    internal static (string Program, string? Script, string Source)? FindCodex()
    {
        if (PathFolders().Select(folder => Path.Combine(folder, "codex.exe")).FirstOrDefault(File.Exists) is { } onPath) return (onPath, null, "PATH");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var file in new[] { Path.Combine(CodexHome(), "bin", "codex.exe"), Path.Combine(profile, ".local", "bin", "codex.exe") })
            if (File.Exists(file)) return (file, null, "autonome");

        var npmFolders = PathFolders().Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm")).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in npmFolders)
        {
            var package = Path.Combine(folder, "node_modules", "@openai", "codex");
            var script = Path.Combine(package, "bin", "codex.js");
            if (!File.Exists(script)) continue;
            foreach (var native in new[]
                     {
                         Path.Combine(package, "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"),
                         Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"),
                         Path.Combine(folder, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe"),
                     })
                if (File.Exists(native)) return (native, null, "npm");
            var local = Path.Combine(folder, "node.exe");
            var node = File.Exists(local) ? local : PathFolders().Select(f => Path.Combine(f, "node.exe")).FirstOrDefault(File.Exists);
            if (node != null) return (node, script, "npm");
        }

        try
        {
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(desktop)
                && Directory.EnumerateDirectories(desktop).Select(d => Path.Combine(d, "codex.exe")).Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } app)
                return (app, null, "appli de bureau");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier illisible : pas de Codex de ce côté.
        }
        return null;
    }
}

/// <summary>
/// Lecture du flux « codex exec --json » (une ligne JSON par événement : thread.started, turn.started, item.started /
/// item.updated / item.completed, turn.completed, turn.failed, error). La réponse = les messages de l'agent (« agent_message »),
/// dans l'ordre ; la réflexion, les commandes et le reste sont ignorés. Les « error » seuls sont souvent des reconnexions :
/// gardés seulement pour expliquer une fin sans réponse ; « turn.failed » est l'échec définitif.
/// </summary>
internal sealed class CodexStream
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _messages = [];

    public string Answer { get; private set; } = "";
    public string? Failure { get; private set; }
    public string? LastError { get; private set; }
    public bool Completed { get; private set; }

    /// <summary>Lit une ligne ; vrai si la réponse a changé.</summary>
    public bool Feed(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return false;
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            switch (Str(root, "type"))
            {
                case "item.started" or "item.updated" or "item.completed"
                    when root.TryGetProperty("item", out var item) && Str(item, "type") == "agent_message":
                    var id = Str(item, "id");
                    if (id.Length == 0) id = "#" + _order.Count;
                    if (!_messages.ContainsKey(id)) _order.Add(id);
                    _messages[id] = Str(item, "text");
                    var answer = string.Join("\n\n", _order.Select(key => _messages[key]).Where(text => text.Length > 0));
                    if (answer == Answer) return false;
                    Answer = answer;
                    return true;
                case "turn.failed":
                    Failure = root.TryGetProperty("error", out var error) ? Str(error, "message") : "";
                    if (Failure.Length == 0) Failure = LastError ?? "turn.failed";
                    return false;
                case "error":
                    LastError = Str(root, "message");
                    return false;
                case "turn.completed":
                    Completed = true;
                    return false;
            }
        }
        catch (JsonException)
        {
            // Ligne inattendue : ignorée.
        }
        return false;
    }

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
