using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Dock;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Palette;

/// <summary>Une ligne de résultat de la palette.</summary>
public sealed record PaletteItem(string Title, string Subtitle, Action Run)
{
    public ImageSource? Icon { get; init; }
    /// <summary>Glyphe de la police d'icônes (Lucide, voir Themes/Icons) quand il n'y a pas d'icône d'appli.</summary>
    public string? Glyph { get; init; }
    public Brush? GlyphBackground { get; init; }
    /// <summary>Astuce : la choisir écrit ce texte dans la barre au lieu de lancer quelque chose.</summary>
    public string? FillQuery { get; init; }
}

/// <summary>Ce que la palette peut demander à l'île (AskClaude : null sans île affichée).</summary>
public sealed record PaletteActions(Action<TimeSpan> StartTimer, Action StartPomodoro, Action<string>? AskClaude = null, Action<string>? Surprise = null,
    Action<bool>? OpenClipboard = null, Action<string, string>? Notice = null, Action? OpenNotes = null,
    Action<string, string?>? Translate = null, Action<string>? TranslatePage = null, Action? TranslateClipboard = null, Action? OpenTranslate = null);

/// <summary>
/// Interprète ce qu'on tape dans la palette : calcul, minuteur, profil, applis, dossiers, raccourcis
/// Windows, puis recherche dans les fichiers et sur le web. Tout est local et instantané (les listes d'applis
/// et de dossiers sont chargées en arrière-plan).
/// </summary>
public static partial class PaletteEngine
{
    [GeneratedRegex(@"^(?:timer|minuteur|minuterie|chrono|compte\s*à\s*rebours)\b\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimerCommand();

    // (?!\d) : un nombre n'est jamais coupé en deux (« 90 s » lisait 9 min 0 s, « 45s » 4 min 5 s).
    [GeneratedRegex(@"^(?:(?<h>\d+)(?!\d)\s*h(?:eures?|ours?|rs?)?)?\s*(?:(?<m>\d+)(?!\d)\s*(?:m|mn|min|mins|minutes?)?)?\s*(?:(?<s>\d+)(?!\d)\s*(?:s|sec|secs|secondes?|seconds?))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Duration();

    [GeneratedRegex(@"^(?:(?<h>\d+):(?=\d{2}:))?(?<m>\d+):(?<s>\d{2})$")]
    private static partial Regex ClockDuration();

    [GeneratedRegex(@"^(?:profils?|profiles?)\b\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileCommand();

    [GeneratedRegex(@"^(?:mots?\s+de\s+passe|mdp|passwords?|pwd)\b\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordCommand();

    // « ne pas déranger 1 h », « dnd 30 min », « npd stop », « do not disturb »… (7F)
    [GeneratedRegex(@"^(?:ne\s+pas\s+d[ée]ranger|dnd|npd|do\s+not\s+disturb|concentration|focus)\b\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FocusCommand();

    [GeneratedRegex(@"^(?:veilleuse|night\s*light|lumi[èe]re\s+bleue|blue\s+light)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NightLightCommand();

    [GeneratedRegex(@"^(?:stop|arr[êe]ter?|off|non|fin|end)$", RegexOptions.IgnoreCase)]
    private static partial Regex StopWord();

    [GeneratedRegex(@"^(?:demain|tomorrow|matin|morning)$", RegexOptions.IgnoreCase)]
    private static partial Regex MorningWord();

    /// <summary>
    /// « Ne pas déranger » (7F) : la commande seule propose les 4 durées (et « Arrêter » s'il est actif) ; suivie d'une durée
    /// (« 1 h », « 45 »), de « demain » ou de « stop », une seule ligne. Aussi « veilleuse » = allumer / éteindre la veilleuse.
    /// </summary>
    private static void AddFocus(string query, List<PaletteItem> items)
    {
        if (NightLightCommand().IsMatch(query) || AppCatalog.MatchScore(Loc.T("palette.nightlight.alias"), query) >= 3)
        {
            bool lit = Services.NightLight.NightLightService.IsOn;
            items.Add(new PaletteItem(Loc.T(lit ? "palette.nightlight.off" : "palette.nightlight.on"), Services.NightLight.NightLightService.StateText(),
                Services.NightLight.NightLightService.Toggle) { Glyph = Icons.NightLight });
        }

        var command = FocusCommand().Match(query);
        bool named = command.Success || AppCatalog.MatchScore(Loc.T("palette.dnd.alias"), query) >= 3;
        if (!named) return;
        var rest = command.Success ? command.Groups["rest"].Value.Trim() : "";
        string subtitle = FocusService.DoNotDisturb ? FocusService.Describe(DateTime.Now) : Loc.T("palette.dnd.description");
        PaletteItem Item(string title, Action run) => new(title, subtitle, run) { Glyph = Icons.DoNotDisturb };

        if (rest.Length > 0 && StopWord().IsMatch(rest))
        {
            items.Add(Item(Loc.T("palette.dnd.stop"), FocusService.Stop));
            return;
        }
        if (rest.Length > 0 && MorningWord().IsMatch(rest))
        {
            items.Add(Item(Loc.F("palette.dnd.for", FocusService.Label(FocusService.Choice.Morning)), () => FocusService.TurnOn(FocusService.Choice.Morning)));
            return;
        }
        if (rest.Length > 0 && TryParseDuration(rest, out var duration))
        {
            items.Add(Item(Loc.F("palette.dnd.for", Describe(duration)), () => FocusService.TurnOn(duration)));
            return;
        }
        if (FocusService.DoNotDisturb) items.Add(Item(Loc.T("palette.dnd.stop"), FocusService.Stop));
        foreach (var choice in FocusService.Choices)
        {
            var picked = choice;
            items.Add(Item(Loc.F("palette.dnd.for", FocusService.Label(choice)), () => FocusService.TurnOn(picked)));
        }
    }

    /// <summary>Raccourcis Windows proposés par leur nom (dans la langue de l'interface).</summary>
    private static (string Name, string Target, string Glyph)[] Places =>
    [
        (Loc.T("palette.place.this_pc"), "shell:MyComputerFolder", Icons.Monitor),
        (Loc.T("palette.place.recycle_bin"), "shell:RecycleBinFolder", Icons.Trash),
        (Loc.T("palette.place.windows_settings"), "ms-settings:", Icons.Settings),
        (Loc.T("palette.place.control_panel"), "control", Icons.ControlPanel),
        (Loc.T("palette.place.task_manager"), "taskmgr", Icons.Activity),
    ];

    public static List<PaletteItem> Search(string text, PaletteActions actions)
    {
        var query = text.Trim();
        if (query.Length == 0) return Tips();
        if (query == "?") return [ClaudeTip()];

        var items = new List<PaletteItem>();
        // « ? question », « claude … » ou une phrase qui finit par « ? » : Claude en premier.
        bool claudeFirst = query.StartsWith('?') || query.EndsWith('?') || query.StartsWith("claude ", StringComparison.OrdinalIgnoreCase)
                           || query.StartsWith("chatgpt ", StringComparison.OrdinalIgnoreCase);
        if (claudeFirst) AddClaude(query, actions, items);
        if (query.StartsWith('?')) return items;
        AddTranslate(query, actions, items); // « tr … » : avant les calculs (« tr 2+2 » se traduit)
        AddCalculation(query, items);
        AddTimer(query, actions, items);
        AddProfiles(query, items);
        AddClipboard(query, actions, items);
        AddFocus(query, items);
        AddClipText(query, actions, items);
        AddNotes(query, actions, items);
        AddSurprises(query, actions, items);

        foreach (var app in AppCatalog.Search(query, 4))
            items.Add(new PaletteItem(app.Name, Loc.T("search.kind.app"), app.Launch) { Icon = IconService.Get(app.ParsingName) });

        AddPath(query, items);
        foreach (var folder in FolderIndex.Search(query, 3))
            items.Add(new PaletteItem(folder.Name, ShortPath(folder.Path), () => Open(folder.Path)) { Icon = IconService.Get(folder.Path) });

        foreach (var (name, target, glyph) in Places.Where(p => AppCatalog.MatchScore(p.Name, query) >= 2))
            items.Add(new PaletteItem(name, "Windows", () => Open(target)) { Glyph = glyph });
        if (AppCatalog.MatchScore(Loc.T("palette.dock.alias"), query) >= 3 && !App.SafeMode)
            items.Add(new PaletteItem(Dock.TaskbarSwitch.Label, Loc.T("palette.dock.description"), Dock.TaskbarSwitch.Toggle) { Glyph = Icons.Dock });
        if (AppCatalog.MatchScore(Loc.T("palette.stage.alias"), query) >= 3 && !App.SafeMode)
            items.Add(new PaletteItem(Loc.T(Stage.StageManager.IsOn ? "palette.stage.off" : "palette.stage.on"), Loc.T("palette.stage.description"),
                Stage.StageManager.Toggle) { Glyph = Icons.Stage });
        if (Stage.StageManager.IsOn && (AppCatalog.MatchScore(Loc.T("palette.stage.alias"), query) >= 3
                                        || AppCatalog.MatchScore(Loc.T("palette.stage.column.alias"), query) >= 3))
            items.Add(new PaletteItem(Loc.T(Stage.StageManager.AutoHideOn ? "palette.stage.always" : "palette.stage.autohide"),
                Loc.T("palette.stage.column.description"), Stage.StageManager.ToggleAutoHide) { Glyph = Icons.Stage });
        if (Stage.StageManager.IsOn && Stage.StageManager.AutoHideOn && (AppCatalog.MatchScore(Loc.T("palette.stage.alias"), query) >= 3
                                                                         || AppCatalog.MatchScore(Loc.T("palette.stage.peek.alias"), query) >= 3))
            items.Add(new PaletteItem(Loc.T("palette.stage.peek"), Loc.T("palette.stage.peek.description"), Stage.StageManager.PeekColumn) { Glyph = Icons.Stage });
        if (AppCatalog.MatchScore($"{Loc.T("settings.parametres")} {AppInfo.Name}", query) >= 3 || AppCatalog.MatchScore(Loc.T("palette.settings.alias"), query) >= 3)
            items.Add(new PaletteItem(Loc.F("palette.settings", AppInfo.Name), Loc.T("palette.settings.description"), () => SettingsWindow.Open()) { Glyph = Icons.Settings });

        if (!claudeFirst) AddClaude(query, actions, items);
        items.Add(new PaletteItem(Loc.F("search.files", query), Loc.T("search.kind.explorer"), () => SearchWindow.SearchFiles(query)) { Glyph = Icons.Folder });
        items.Add(new PaletteItem(Loc.F("search.web", query), "Google", () => SearchWindow.SearchWeb(query)) { Glyph = Icons.Globe });
        return items;
    }

    /// <summary>
    /// « presse-papiers » = l'onglet de l'île ; « mot de passe &lt;nom&gt; » = copier ce mot de passe du coffre (exclu des
    /// historiques). Coffre verrouillé : une seule ligne qui le déverrouille (Windows Hello) puis copie la meilleure correspondance.
    /// </summary>
    private static void AddClipboard(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (actions.OpenClipboard is not { } open || ModuleGuard.IsDisabled(ModuleGuard.ClipboardTab)) return;
        if (AppCatalog.MatchScore(Loc.T("palette.clipboard.alias"), query) >= 3)
            items.Add(new PaletteItem(Loc.T("palette.clipboard"), Loc.T("palette.clipboard.description"), () => open(false)) { Glyph = Icons.Clipboard });

        var command = PasswordCommand().Match(query);
        if (!command.Success) return;
        var name = command.Groups["rest"].Value.Trim();
        void Copied(VaultEntry? entry) => actions.Notice?.Invoke(Icons.Key,
            entry == null ? Loc.F("palette.password.none", name) : Loc.F("palette.password.copied", entry.Name));
        if (name.Length == 0)
        {
            items.Add(new PaletteItem(Loc.T("palette.vault"), Loc.T("palette.vault.description"), () => open(true)) { Glyph = Icons.Key });
            return;
        }
        if (PasswordVault.IsLocked)
        {
            items.Add(new PaletteItem(Loc.F("palette.password", name), Loc.T("palette.password.locked"),
                async () => Copied(await PasswordVault.CopyPasswordByNameAsync(name))) { Glyph = Icons.Key });
            return;
        }
        var matches = PasswordVault.Match(name).Take(4).ToList();
        if (matches.Count == 0) items.Add(new PaletteItem(Loc.F("palette.password.none", name), Loc.T("palette.vault.description"), () => open(true)) { Glyph = Icons.Key });
        foreach (var entry in matches)
            items.Add(new PaletteItem(Loc.F("palette.password", entry.Name), entry.Login.Length > 0 ? entry.Login : Loc.T("vault.no_login"), () =>
            {
                if (PasswordVault.CopyPassword(entry)) Copied(entry);
            }) { Glyph = Icons.Key });
    }

    /// <summary>Surprises (6F) : seulement pour qui tape exactement la bonne phrase.</summary>

    private static void AddSurprises(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (actions.Surprise is not { } surprise) return;
        var text = query.Trim().ToLowerInvariant();
        if (text is "do a barrel roll" or "fais un tonneau")
            items.Add(new PaletteItem(Loc.T("fun.roll"), Loc.T("fun.roll.description"), () => surprise("roll")) { Glyph = Icons.Plane });
        else if (text == "opaline")
            items.Add(new PaletteItem(Loc.T("fun.credits"), Loc.T("fun.credits.description"), () => surprise("credits")) { Glyph = Icons.Heart });
    }

    /// <summary>« Demander à Claude » (5J) : la réponse s'écrit dans l'île, onglet Claude.</summary>
    private static void AddClaude(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (actions.AskClaude is not { } ask || !SettingsStore.Current.Claude.Enabled) return;
        var question = query.TrimStart('?').Trim();
        if (question.StartsWith("claude ", StringComparison.OrdinalIgnoreCase)) question = question[7..].Trim();
        else if (question.StartsWith("chatgpt ", StringComparison.OrdinalIgnoreCase)) question = question[8..].Trim();
        if (question.Length == 0) return;
        var shown = question.Length > 70 ? question[..70] + "…" : question;
        items.Add(new PaletteItem(Loc.F("palette.claude.ask", ClaudeService.AssistantName), Loc.F("palette.claude.ask.description", shown), () => ask(question))
        {
            Glyph = Icons.Sparkles,
            GlyphBackground = (Brush)Application.Current.FindResource(AppearanceService.AccentGradientKey),
        });
    }

    private static PaletteItem ClaudeTip() =>
        new(Loc.F("palette.claude.tip", ClaudeService.AssistantName), Loc.T("palette.claude.tip.description"), () => { })
        {
            Glyph = Icons.Sparkles,
            GlyphBackground = (Brush)Application.Current.FindResource(AppearanceService.AccentGradientKey),
            FillQuery = "? ",
        };

    /// <summary>Barre vide : quelques exemples (les choisir les écrit dans la barre).</summary>
    private static List<PaletteItem> Tips() =>
    [
        ..(SettingsStore.Current.Claude.Enabled ? new[] { ClaudeTip() } : []),
        new(Loc.T("palette.tip.calc"), Loc.T("palette.tip.calc.description"), () => { }) { Glyph = Icons.Calculator, FillQuery = Loc.T("palette.tip.calc") },
        new("timer 10 min", Loc.T("palette.tip.timer.description"), () => { }) { Glyph = Icons.Timer, FillQuery = "timer 10 min" },
        new(Loc.T("palette.tip.profile"), Loc.T("palette.profile.switch"), () => { }) { Glyph = Icons.Profiles, FillQuery = Loc.T("palette.tip.profile") + " " },
        new(Loc.T("folders.downloads"), Loc.T("palette.tip.open.description"), () => { }) { Glyph = Icons.Folder, FillQuery = Loc.T("folders.downloads") },
    ];

    private static void AddCalculation(string query, List<PaletteItem> items)
    {
        if (!Calculator.TryEvaluate(query, out double result)) return;
        var shown = Calculator.Format(result);
        items.Add(new PaletteItem(shown, Loc.F("palette.calc.copy", query), () => CopyToClipboard(Calculator.FormatPlain(result)))
        {
            Glyph = Icons.Calculator,
            GlyphBackground = (Brush)Application.Current.FindResource(AppearanceService.AccentGradientKey),
        });
    }

    private static void AddTimer(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (AppCatalog.MatchScore("pomodoro", query) >= 4 && query.Length >= 3)
        {
            var island = SettingsStore.Current.Island;
            items.Add(new PaletteItem(Loc.T("palette.pomodoro"), Loc.F("palette.pomodoro.description", island.PomodoroFocusMinutes, island.PomodoroBreakMinutes),
                actions.StartPomodoro) { Glyph = Icons.Timer, GlyphBackground = Paint.Timer });
        }

        var command = TimerCommand().Match(query);
        if (!command.Success) return;
        var rest = command.Groups["rest"].Value.Trim();
        if (rest.Length == 0)
        {
            foreach (int minutes in new[] { 5, 10, 25 })
                items.Add(TimerItem(TimeSpan.FromMinutes(minutes), actions));
            return;
        }
        if (TryParseDuration(rest, out var duration)) items.Add(TimerItem(duration, actions));
    }

    private static PaletteItem TimerItem(TimeSpan duration, PaletteActions actions) =>
        new(Loc.F("palette.timer", Describe(duration)), Loc.T("palette.timer.description"), () => actions.StartTimer(duration))
        {
            Glyph = Icons.Timer,
            GlyphBackground = Paint.Timer,
        };

    /// <summary>
    /// « 10 », « 10 min », « 1h30 », « 90 s », « 2:30 », « 1:30:00 » (sans unité = minutes) ; jusqu'à 24 h, ramené à 23:59:59 (le
    /// plus long minuteur de l'île, correctif 19).
    /// </summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        text = text.Trim();
        var clock = ClockDuration().Match(text);
        var match = clock.Success ? clock : Duration().Match(text);
        if (!match.Success || text.Length == 0) return false;

        long seconds = 0;
        foreach (var (name, unit) in new[] { ("h", 3600L), ("m", 60L), ("s", 1L) })
        {
            if (!match.Groups[name].Success) continue;
            // Nombre énorme (« 99999999999 s ») : refusé plutôt qu'une exception.
            if (!long.TryParse(match.Groups[name].Value, out long value) || value > 24 * 3600) return false;
            seconds += value * unit;
        }
        if (seconds <= 0 || seconds > 24 * 3600) return false;
        duration = TimeSpan.FromSeconds(Math.Min(seconds, TimerService.MaxDuration.TotalSeconds));
        return true;
    }

    private static string Describe(TimeSpan duration)
    {
        var parts = new List<string>();
        if (duration.Hours > 0) parts.Add($"{duration.Hours} h");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes} min");
        if (duration.Seconds > 0) parts.Add($"{duration.Seconds} s");
        return string.Join(" ", parts);
    }

    private static void AddProfiles(string query, List<PaletteItem> items)
    {
        var command = ProfileCommand().Match(query);
        var search = command.Success ? command.Groups["rest"].Value.Trim() : query;
        foreach (var profile in ProfileService.All)
        {
            bool matches = search.Length == 0 ? command.Success : AppCatalog.MatchScore(profile.Name, search) >= (command.Success ? 1 : 3);
            if (!matches) continue;
            bool active = profile == ProfileService.Active;
            var (start, end) = ProfileService.AccentOf(profile);
            var id = profile.Id;
            items.Add(new PaletteItem(Loc.F("palette.profile", profile.Name), Loc.T(active ? "profiles.is_active" : "palette.profile.switch"),
                () => ProfileService.Switch(id)) { Glyph = Icons.Glyph(profile.Icon), GlyphBackground = Paint.Gradient(start, end, 45) });
        }
    }

    /// <summary>Chemin tapé directement (« C:\… », « ~\Documents », « %APPDATA% »).</summary>
    private static void AddPath(string query, List<PaletteItem> items)
    {
        if (!query.Contains('\\') && !query.Contains('/') && !query.StartsWith('%') && !query.StartsWith('~')) return;
        var path = Environment.ExpandEnvironmentVariables(query.Replace('/', '\\'));
        if (path.StartsWith('~')) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        var name = Path.GetFileName(path.TrimEnd('\\'));
        items.Add(new PaletteItem(Loc.F("palette.open", name.Length > 0 ? name : path), ShortPath(path), () => Open(path))
        {
            Icon = IconService.Get(path),
        });
    }

    private static string ShortPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Palette : ouverture de « {target} »", ex);
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Log.Error("Palette : copie du résultat", ex);
        }
    }
}
