namespace DynamicIsland.Settings;

/// <summary>Comportement de l'île, comme celui du dock (priorité 2, point 13).</summary>
public enum IslandMode
{
    /// <summary>Toujours là (comportement d'origine).</summary>
    Fixed,
    /// <summary>Se cache quand une appli est juste en dessous (navigateur agrandi…) ; revient quand la souris touche le bord.</summary>
    Smart,
    /// <summary>Cachée ; apparaît quand la souris touche le bord de l'écran au-dessus d'elle.</summary>
    Peek,
}

/// <summary>Règle d'une appli pour l'île ou le dock.</summary>
public enum AppBehavior
{
    /// <summary>Le mode choisi décide.</summary>
    Default,
    /// <summary>Se cache quand cette appli est au premier plan ou en dessous.</summary>
    Hide,
    /// <summary>Reste visible avec cette appli (plein écran, mode Intelligent, Peek…).</summary>
    Show,
}

/// <summary>
/// Règle par application (point 13) : « Chrome = se cache, Spotify = toujours visible », pour l'île et pour le dock. L'appli est
/// reconnue par le nom de son processus (sans « .exe », en minuscules), comme les exceptions du mode Gaming.
/// </summary>
public sealed class AppRule
{
    public string Process { get; set; } = "";
    /// <summary>Nom affiché (nom du produit), sinon le processus.</summary>
    public string Name { get; set; } = "";
    public AppBehavior Island { get; set; }
    public AppBehavior Dock { get; set; }
    /// <summary>Stage Manager (7G) : ses fenêtres restent où elles sont (jamais réduites ni déplacées).</summary>
    public bool StageIgnore { get; set; }

    public const int Max = 40;

    /// <summary>Une règle par processus, sans les règles vides (île et dock « par défaut »).</summary>
    public static List<AppRule> NormalizeList(List<AppRule>? rules) =>
        (rules ?? [])
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Process))
            .Select(r =>
            {
                r.Process = Clean(r.Process);
                if (string.IsNullOrWhiteSpace(r.Name)) r.Name = r.Process;
                if (!Enum.IsDefined(r.Island)) r.Island = AppBehavior.Default;
                if (!Enum.IsDefined(r.Dock)) r.Dock = AppBehavior.Default;
                return r;
            })
            .DistinctBy(r => r.Process)
            .Take(Max)
            .ToList();

    /// <summary>« Chrome.exe » / « C:\…\chrome.exe » → « chrome ».</summary>
    public static string Clean(string process)
    {
        var name = System.IO.Path.GetFileName(process.Trim());
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    /// <summary>Règle de ce processus (null = aucune).</summary>
    public static AppRule? For(IEnumerable<AppRule> rules, string? process) =>
        process == null ? null : rules.FirstOrDefault(r => r.Process == Clean(process));
}

/// <summary>Un onglet de l'île : affiché ou non, dans l'ordre de la liste (point 12).</summary>
public sealed class IslandTabItem
{
    public const string Music = "music";
    public const string Calendar = "calendar";
    public const string Control = "control";
    public const string Widgets = "widgets";
    public const string Shelf = "shelf";
    public const string Voice = "voice";
    public const string Claude = "claude";
    public const string Clocks = "clocks";
    public const string Health = "health";
    public const string Friends = "friends";
    public const string Mixer = "mixer";
    /// <summary>Ordre par défaut ; un onglet ajouté plus tard (Mélangeur) se range à la fin des listes déjà enregistrées.</summary>
    public const string Clipboard = "clipboard";
    public const string Notes = "notes";
    public const string Translate = "translate";
    public static readonly string[] All = [Music, Calendar, Control, Mixer, Widgets, Shelf, Voice, Claude, Clocks, Health, Friends, Clipboard, Notes, Translate];

    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;

    public static List<IslandTabItem> Defaults() => All.Select(id => new IslandTabItem { Id = id }).ToList();

    /// <summary>Chaque onglet connu une seule fois, dans l'ordre choisi ; les nouveaux s'ajoutent à la fin. Au moins un visible.</summary>
    public static List<IslandTabItem> NormalizeList(List<IslandTabItem>? items)
    {
        var result = (items ?? []).Where(i => i != null && All.Contains(i.Id)).DistinctBy(i => i.Id).ToList();
        foreach (var id in All.Where(id => result.All(i => i.Id != id))) result.Add(new IslandTabItem { Id = id });
        if (!result.Any(i => i.Visible)) result.First(i => i.Id == Music).Visible = true;
        return result;
    }
}
