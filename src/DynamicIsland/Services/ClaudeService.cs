using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>Un échange avec Claude : la question, la réponse (écrite au fil de l'eau) et une éventuelle erreur.</summary>
public sealed class ClaudeTurn(string question)
{
    public string Question { get; } = question;
    public string Answer { get; set; } = "";
    public bool Done { get; set; }
    public string? Error { get; set; }
    /// <summary>Arrêtée par l'utilisateur (bouton « Arrêter », ou une nouvelle question).</summary>
    public bool Stopped { get; set; }
    /// <summary>Ce qui règle l'erreur : se connecter, ou mettre Claude Code à jour (un bouton sous le message).</summary>
    public ClaudeFix Fix { get; set; }
}

/// <summary>Action proposée sous une erreur de Claude.</summary>
public enum ClaudeFix
{
    None,
    Login,
    Update,
    /// <summary>Clé API à ajouter ou à changer (Paramètres → Île → Claude).</summary>
    ApiKey,
}

/// <summary>Claude Code sur ce PC : trouvé ou non, connecté ou non (vérifié sans rien consommer du forfait).</summary>
public sealed record ClaudeStatus(bool Installed, bool LoggedIn, string Description);

/// <summary>
/// Mini Claude (5J), choix de l'utilisateur : passe par Claude Code installé sur le PC et son compte Claude (abonnement :
/// aucune clé, chaque question compte dans les limites du forfait). Une question = un « claude -p » SANS outils (ni
/// fichiers, ni commandes, ni web), lancé dans un dossier vide, sans session gardée sur disque ; la question passe par
/// l'entrée standard (jamais dans une ligne de commande) et la réponse arrive en flux (stream-json). La conversation
/// (derniers échanges) reste en mémoire seulement, pour les questions de suite.
/// </summary>
public static partial class ClaudeService
{
    public const string DefaultModel = "claude-opus-5-5";
    /// <summary>Modèles proposés ; Description = clé du texte dans les fichiers de langue.</summary>
    public static readonly (string Id, string Name, string Description)[] Models =
    [
        ("claude-opus-5-5", "Opus 5.5", "claude.model.opus"),
        ("claude-sonnet-5-5", "Sonnet 5.5", "claude.model.sonnet"),
        ("claude-haiku-5-5", "Haiku 5.5", "claude.model.haiku"),
    ];
    private const int HistoryTurns = 6;          // échanges renvoyés comme contexte d'une question de suite
    private const int HistoryAnswerChars = 1500; // une longue réponse est raccourcie dans ce contexte
    private const int KeptTurns = 20;            // échanges affichés dans l'onglet
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(3);

    private static readonly List<ClaudeTurn> TurnList = [];
    private static CancellationTokenSource? _running;
    private static ClaudeTurn? _current;

    public static IReadOnlyList<ClaudeTurn> Turns => TurnList;
    public static bool IsBusy => _running != null;
    /// <summary>Levé (thread de l'interface) à chaque morceau de réponse, à la fin, et quand la conversation change.</summary>
    public static event Action? Changed;

    public static string ModelName(string id) => Models.FirstOrDefault(m => m.Id == id).Name ?? id;

    // ------------------------------------------------------------------ questions

    /// <summary>Pose une question (une seule à la fois : la précédente est arrêtée).</summary>
    public static async void Ask(string question)
    {
        question = question.Trim();
        if (question.Length == 0 || ModuleGuard.IsDisabled(ModuleGuard.Claude)) return;
        Stop();
        var turn = new ClaudeTurn(question);
        var history = TurnList.Where(t => t.Done && t.Error == null && t.Answer.Length > 0).TakeLast(HistoryTurns).ToList();
        TurnList.Add(turn);
        if (TurnList.Count > KeptTurns) TurnList.RemoveAt(0);
        using var cancel = new CancellationTokenSource(MaxDuration);
        _running = cancel;
        _current = turn;
        Raise();
        try
        {
            if (SettingsStore.Current.Claude.Engine == ClaudeEngine.ApiKey) await RunApiAsync(turn, question, history, cancel.Token);
            else await RunAsync(turn, Prompt(question, history), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            if (turn.Answer.Length > 0) turn.Answer += " …";
            else turn.Error = Loc.T(turn.Stopped ? "claude.stopped" : "claude.timeout");
        }
        catch (Exception ex)
        {
            Log.Error("Claude", ex);
            turn.Error = ex is ClaudeException ? ex.Message : Loc.F("claude.error.failed", ex.Message);
            turn.Fix = (ex as ClaudeException)?.Fix ?? ClaudeFix.None;
        }
        finally
        {
            turn.Done = true;
            if (_running == cancel)
            {
                _running = null;
                _current = null;
            }
            Raise();
        }
    }

    /// <summary>Arrête la réponse en cours (le processus de Claude Code est fermé).</summary>
    public static void Stop()
    {
        if (_current is { } turn) turn.Stopped = true;
        _running?.Cancel();
    }

    /// <summary>« Nouvelle conversation ».</summary>
    public static void Clear()
    {
        Stop();
        TurnList.Clear();
        Raise();
    }

    /// <summary>Question de suite : les derniers échanges sont donnés comme contexte (rien n'est gardé côté Claude Code).</summary>
    private static string Prompt(string question, List<ClaudeTurn> history)
    {
        if (history.Count == 0) return question;
        var text = new StringBuilder("Pour contexte, notre conversation jusqu'ici :\n\n");
        foreach (var turn in history)
        {
            var answer = turn.Answer.Length > HistoryAnswerChars ? turn.Answer[..HistoryAnswerChars] + "…" : turn.Answer;
            text.Append("Moi : ").Append(turn.Question).Append("\n\nToi : ").Append(answer).Append("\n\n");
        }
        return text.Append("Ma nouvelle question : ").Append(question).ToString();
    }

    private static string SystemPrompt() =>
        $"Tu es Claude, intégré à {AppInfo.Name} : une petite « île » en haut de l'écran Windows de l'utilisateur, où ta réponse "
        + "s'affiche dans un espace réduit (environ 440 × 190 pixels, avec défilement). Réponds dans la langue de la question "
        + (Loc.Language == Loc.English ? "(souvent l'anglais)" : "(souvent le français)") + ", directement et brièvement : en général 1 à 5 phrases, une courte liste seulement si c'est "
        + "vraiment utile, pas de titres ni de tableaux, un extrait de code court si on t'en demande. Tu n'as accès à aucun "
        + "fichier, aucun outil ni à Internet : si la question en a besoin, dis-le simplement. "
        + $"Nous sommes le {DateTime.Now.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))}.";

    /// <summary>Lance « claude -p » et lit le flux : texte au fil de l'eau, puis le résultat (ou l'erreur).</summary>
    private static async Task RunAsync(ClaudeTurn turn, string prompt, CancellationToken token)
    {
        var start = Command([
            "-p", "--model", SettingsStore.Current.Claude.Model, "--effort", "low",
            "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--tools", "", "--no-session-persistence", "--system-prompt", SystemPrompt(),
        ]) ?? throw new ClaudeException(Loc.T("claude.error.not_installed_command"));
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;

        using var process = Process.Start(start) ?? throw new ClaudeException(Loc.T("claude.error.start"));
        using var kill = token.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        string? result = null;
        bool failed = false;
        while (await process.StandardOutput.ReadLineAsync(token) is { } line)
        {
            if (line.Length == 0 || line[0] != '{') continue;
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue; // ligne inattendue : on l'ignore
            }
            using var parsed = json;
            var root = json.RootElement;
            switch (Text(root, "type"))
            {
                case "stream_event" when root.TryGetProperty("event", out var e) && Text(e, "type") == "content_block_delta"
                                         && e.TryGetProperty("delta", out var delta) && Text(delta, "type") == "text_delta":
                    turn.Answer += Text(delta, "text");
                    Raise();
                    break;
                case "assistant" when turn.Answer.Length == 0 && root.TryGetProperty("message", out var message)
                                      && Text(message, "model") != "<synthetic>" // message d'erreur fabriqué par Claude Code
                                      && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array:
                    // Sans morceaux (ancienne version) : le message complet.
                    foreach (var block in content.EnumerateArray())
                        if (Text(block, "type") == "text") turn.Answer += Text(block, "text");
                    break;
                case "result":
                    result = Text(root, "result");
                    failed = root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
                    break;
            }
        }
        await process.WaitForExitAsync(token);

        if (failed)
        {
            turn.Answer = ""; // l'erreur s'affiche à part, en clair
            throw Explain(result ?? "");
        }
        if (turn.Answer.Length == 0 && !string.IsNullOrWhiteSpace(result)) turn.Answer = result;
        if (turn.Answer.Length == 0)
        {
            var stderr = (await errors).Trim();
            throw Explain(stderr.Length > 0 ? stderr.Split('\n').Last().Trim() : Loc.F("claude.error.exit", process.ExitCode));
        }
    }

    [GeneratedRegex(@"Claude Code (?<have>\d[\w.\-]*) does not support this model; version (?<need>\d[\w.\-]*) or newer")]
    private static partial Regex TooOld();

    /// <summary>
    /// Message clair pour les erreurs connues de Claude Code (« API Error: 400 {json} » : on garde le message du JSON).
    /// Vérifié par <c>--self-test</c>.
    /// </summary>
    internal static ClaudeException Explain(string message)
    {
        // Claude Code trop ancien pour le modèle choisi (vu chez l'utilisateur : 2.1.92 pour Opus 5.5, 2.1.280 requise).
        if (message.Contains("claude_code_version_too_old", StringComparison.OrdinalIgnoreCase) || TooOld().IsMatch(message))
        {
            var match = TooOld().Match(message);
            var versions = match.Success ? " " + Loc.F("claude.error.versions", match.Groups["have"].Value, match.Groups["need"].Value) : "";
            return new ClaudeException(Loc.F("claude.error.too_old", ModelName(SettingsStore.Current.Claude.Model), versions), ClaudeFix.Update);
        }
        if (message.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) || message.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authentication_error", StringComparison.OrdinalIgnoreCase) || message.Contains("OAuth token", StringComparison.OrdinalIgnoreCase))
            return new ClaudeException(Loc.T("claude.error.not_logged_in"), ClaudeFix.Login);
        var detail = ApiMessage(message) ?? message;
        if (detail.Contains("usage limit", StringComparison.OrdinalIgnoreCase) || detail.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate_limit_error", StringComparison.OrdinalIgnoreCase))
            return new ClaudeException(Loc.F("claude.error.limit", detail));
        return new ClaudeException(detail.Length > 0 ? Loc.F("claude.error.failed", detail) : Loc.T("claude.error.no_answer"));
    }

    /// <summary>« API Error: 400 {"error":{"message":"…"}} » → le message, lisible ; null si ce n'est pas ce format.</summary>
    private static string? ApiMessage(string message)
    {
        int brace = message.IndexOf('{');
        if (brace < 0) return null;
        try
        {
            using var json = JsonDocument.Parse(message[brace..]);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error) && Text(error, "message") is { Length: > 0 } text) return text;
            return Text(root, "message") is { Length: > 0 } top ? top : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Déjà terminé.
        }
    }

    private static void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Claude : affichage", ex);
        }
    }

    // ------------------------------------------------------------------ Claude Code sur ce PC

    /// <summary>Installé ? connecté (et avec quel compte) ? — « claude auth status » ne consomme rien du forfait.</summary>
    public static async Task<ClaudeStatus> CheckAsync()
    {
        try
        {
            var start = Command(["auth", "status"]);
            if (start == null) return new ClaudeStatus(false, false, Loc.T("claude.status.not_installed"));
            start.RedirectStandardOutput = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            int brace = output.IndexOf('{');
            if (brace < 0) return new ClaudeStatus(true, false, Loc.T("claude.status.unreadable"));
            using var json = JsonDocument.Parse(output[brace..]);
            var root = json.RootElement;
            bool loggedIn = root.TryGetProperty("loggedIn", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (!loggedIn) return new ClaudeStatus(true, false, Loc.T("claude.status.not_logged_in"));
            var plan = Text(root, "subscriptionType");
            var method = Text(root, "authMethod");
            return new ClaudeStatus(true, true, plan.Length > 0 ? Loc.F("claude.status.connected_plan", plan) : method.Length > 0 ? Loc.F("claude.status.connected_method", method) : Loc.T("claude.status.connected"));
        }
        catch (Exception ex)
        {
            Log.Error("Claude : état de Claude Code", ex);
            return new ClaudeStatus(true, false, Loc.F("claude.status.check_failed", ex.Message));
        }
    }

    /// <summary>
    /// Ouvre une fenêtre de commande sur « claude auth login » : l'utilisateur se connecte lui-même dans son navigateur
    /// (rien n'est saisi par l'app).
    /// </summary>
    public static void OpenLogin() => OpenConsole(Loc.T("claude.console.login"), ["auth", "login"]);

    /// <summary>« claude update », dans une fenêtre visible : Claude Code se met à jour lui-même (sa propre source).</summary>
    public static void OpenUpdate() => OpenConsole(Loc.T("claude.console.update"), ["update"]);

    /// <summary>Lance Claude Code avec ces arguments dans une console visible, qui reste ouverte pour lire le résultat.</summary>
    private static void OpenConsole(string title, string[] arguments)
    {
        var start = Command(arguments);
        if (start == null)
        {
            MessageBox.Show(Loc.T("claude.status.not_installed"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Dans une console visible, qui reste ouverte pour lire le résultat. « /s /k "…" » : cmd retire seulement les
        // guillemets extérieurs, ceux des chemins restent.
        var line = string.Join(" ", new[] { start.FileName }.Concat(start.ArgumentList).Select(value => $"\"{value}\""));
        var console = new ProcessStartInfo("cmd.exe", $"/s /k \"title {title} & {line}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = start.WorkingDirectory,
        };
        CleanEnvironment(console);
        Process.Start(console)?.Dispose();
    }

    /// <summary>
    /// Commande pour Claude Code : l'exe natif s'il existe, sinon « node cli.js » de l'installation npm (pas de passage
    /// par cmd.exe : les options sont transmises telles quelles). Dossier de travail vide, environnement nettoyé.
    /// </summary>
    private static ProcessStartInfo? Command(IEnumerable<string> arguments)
    {
        ProcessStartInfo? start = null;
        if (FindExe() is { } exe) start = new ProcessStartInfo(exe);
        else if (FindNpmScript() is { } script && FindNode(Path.GetDirectoryName(script)) is { } node)
        {
            start = new ProcessStartInfo(node);
            start.ArgumentList.Add(script);
        }
        if (start == null) return null;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WorkingDirectory = WorkFolder();
        CleanEnvironment(start);
        return start;
    }

    /// <summary>Dossier vide : Claude Code n'y trouve aucun projet ni CLAUDE.md.</summary>
    private static string WorkFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "Claude");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Retire les variables d'une session Claude Code qui aurait lancé l'app (mode minimal sans compte, proxy de l'app de
    /// bureau…) : le mini Claude utilise toujours le compte de l'utilisateur.
    /// </summary>
    private static void CleanEnvironment(ProcessStartInfo start)
    {
        bool fromClaudeSession = start.Environment.ContainsKey("CLAUDECODE");
        foreach (var key in start.Environment.Keys.ToList())
        {
            if (key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) && !key.Equals("CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
            else if (fromClaudeSession && key.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        }
    }

    private static IEnumerable<string> PathFolders() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Installation native (« claude install ») : ~\.local\bin\claude.exe, ou claude.exe dans le PATH.</summary>
    private static string? FindExe()
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(native)) return native;
        return PathFolders().Select(folder => Path.Combine(folder, "claude.exe")).FirstOrDefault(File.Exists);
    }

    /// <summary>Installation npm (« npm i -g @anthropic-ai/claude-code ») : le cli.js à côté du raccourci claude.cmd.</summary>
    private static string? FindNpmScript()
    {
        var folders = PathFolders().Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        foreach (var folder in folders)
        {
            var script = Path.Combine(folder, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
            if (File.Exists(Path.Combine(folder, "claude.cmd")) && File.Exists(script)) return script;
        }
        return null;
    }

    /// <summary>node.exe : celui à côté du raccourci npm s'il y en a un, sinon celui du PATH.</summary>
    private static string? FindNode(string? scriptFolder)
    {
        for (var folder = scriptFolder; folder != null; folder = Path.GetDirectoryName(folder))
        {
            if (File.Exists(Path.Combine(folder, "claude.cmd")))
            {
                var local = Path.Combine(folder, "node.exe");
                if (File.Exists(local)) return local;
                break;
            }
        }
        return PathFolders().Select(folder => Path.Combine(folder, "node.exe")).FirstOrDefault(File.Exists);
    }
}

/// <summary>Erreur à montrer telle quelle dans l'onglet Claude, avec l'action qui la règle.</summary>
public sealed class ClaudeException(string message, ClaudeFix fix = ClaudeFix.None) : Exception(message)
{
    public ClaudeFix Fix { get; } = fix;
}
