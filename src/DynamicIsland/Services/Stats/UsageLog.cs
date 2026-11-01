using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Services.Stats;

/// <summary>Une journée (date locale) : secondes d'écran, Pomodoros terminés, secondes par jeu et par appli au premier plan.</summary>
public sealed class DayUsage
{
    /// <summary>« yyyy-MM-dd ».</summary>
    public string Date { get; set; } = "";
    public int ScreenSeconds { get; set; }
    public int Pomodoros { get; set; }
    public Dictionary<string, int> Games { get; set; } = [];
    public Dictionary<string, int> Apps { get; set; } = [];

    [JsonIgnore] public DateOnly Day => DateOnly.ParseExact(Date, UsageLog.DateFormat, CultureInfo.InvariantCulture);
    [JsonIgnore] public int GameSeconds => Games.Values.Sum();
}

/// <summary>Une journée d'une semaine résumée (pour les graphiques).</summary>
public sealed record DaySummary(DateOnly Date, int ScreenSeconds, int GameSeconds, int Pomodoros);

/// <summary>Une semaine (lundi → dimanche) : chaque jour, les totaux, les applis et jeux les plus utilisés.</summary>
public sealed record WeekSummary(DateOnly Start, IReadOnlyList<DaySummary> Days, int ScreenSeconds, int GameSeconds, int Pomodoros,
    IReadOnlyList<KeyValuePair<string, int>> TopApps, IReadOnlyList<KeyValuePair<string, int>> TopGames)
{
    public bool HasData => ScreenSeconds > 0 || GameSeconds > 0 || Pomodoros > 0;
}

/// <summary>
/// Historique de l'activité (7F, statistiques.json) : un enregistrement par jour, <see cref="KeepDays"/> jours gardés. Classe
/// PURE (aucune horloge, aucun Windows) : les intervalles sont coupés à minuit, les semaines commencent le lundi. Vérifiée par
/// --self-test. Rien ne quitte le PC.
/// </summary>
public sealed class UsageLog
{
    public const string DateFormat = "yyyy-MM-dd";
    public const int KeepDays = 90;
    /// <summary>Applis gardées par jour (les moins utilisées partent) : le fichier reste petit.</summary>
    public const int MaxAppsPerDay = 40;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public int Version { get; set; } = 1;
    public List<DayUsage> Days { get; set; } = [];

    /// <summary>Jour existant ou nouveau (liste gardée triée).</summary>
    public DayUsage Day(DateOnly date)
    {
        var key = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        int index = Days.FindIndex(d => string.CompareOrdinal(d.Date, key) >= 0);
        if (index >= 0 && Days[index].Date == key) return Days[index];
        var day = new DayUsage { Date = key };
        if (index < 0) Days.Add(day);
        else Days.Insert(index, day);
        return day;
    }

    public DayUsage? Find(DateOnly date)
    {
        var key = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        return Days.FirstOrDefault(d => d.Date == key);
    }

    /// <summary>
    /// Temps passé de <paramref name="from"/> à <paramref name="to"/> (heure locale), coupé à chaque minuit. <paramref name="game"/> :
    /// jeu en cours (compté aussi dans le temps d'écran) ; <paramref name="app"/> : appli au premier plan (sinon rien).
    /// </summary>
    public void Record(DateTime from, DateTime to, string? game, string? app)
    {
        if (to <= from) return;
        var start = from;
        while (start < to)
        {
            var midnight = start.Date.AddDays(1);
            var end = to < midnight ? to : midnight;
            int seconds = (int)Math.Round((end - start).TotalSeconds);
            if (seconds > 0)
            {
                var day = Day(DateOnly.FromDateTime(start));
                day.ScreenSeconds += seconds;
                if (!string.IsNullOrWhiteSpace(game)) Add(day.Games, game.Trim(), seconds);
                else if (!string.IsNullOrWhiteSpace(app))
                {
                    Add(day.Apps, app.Trim(), seconds);
                    if (day.Apps.Count > MaxAppsPerDay) day.Apps.Remove(day.Apps.MinBy(p => p.Value).Key);
                }
            }
            start = end;
        }
    }

    public void AddPomodoro(DateTime at) => Day(DateOnly.FromDateTime(at)).Pomodoros++;

    private static void Add(Dictionary<string, int> totals, string name, int seconds) =>
        totals[name] = totals.TryGetValue(name, out int previous) ? previous + seconds : seconds;

    /// <summary>Garde les <see cref="KeepDays"/> derniers jours (aujourd'hui compris) ; vrai si quelque chose a été retiré.</summary>
    public bool Prune(DateOnly today)
    {
        var oldest = today.AddDays(-(KeepDays - 1)).ToString(DateFormat, CultureInfo.InvariantCulture);
        return Days.RemoveAll(d => string.CompareOrdinal(d.Date, oldest) < 0) > 0;
    }

    /// <summary>Lundi de la semaine de <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <param name="days">Jours comptés depuis le lundi (les suivants restent à zéro) : comparer une semaine en cours au même nombre de jours.</param>
    public WeekSummary Week(DateOnly anyDay, int days = 7)
    {
        var start = WeekStart(anyDay);
        var list = new List<DaySummary>();
        var apps = new Dictionary<string, int>();
        var games = new Dictionary<string, int>();
        for (int i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            var day = i < days ? Find(date) : null;
            list.Add(new DaySummary(date, day?.ScreenSeconds ?? 0, day?.GameSeconds ?? 0, day?.Pomodoros ?? 0));
            if (day == null) continue;
            foreach (var (name, seconds) in day.Apps) Add(apps, name, seconds);
            foreach (var (name, seconds) in day.Games) Add(games, name, seconds);
        }
        static List<KeyValuePair<string, int>> Top(Dictionary<string, int> totals, int count) =>
            [.. totals.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).Take(count)];
        return new WeekSummary(start, list, list.Sum(d => d.ScreenSeconds), list.Sum(d => d.GameSeconds), list.Sum(d => d.Pomodoros),
            Top(apps, 5), Top(games, 3));
    }

    /// <summary>Évolution en % par rapport à la semaine d'avant ; null si la semaine d'avant est vide (pas de comparaison).</summary>
    public static double? Change(int current, int previous) => previous <= 0 ? null : (current - previous) * 100.0 / previous;

    [JsonIgnore] public bool HasData => Days.Any(d => d.ScreenSeconds > 0 || d.Pomodoros > 0);

    // ------------------------------------------------------------------ fichier

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static UsageLog FromJson(string json)
    {
        var log = JsonSerializer.Deserialize<UsageLog>(json) ?? new UsageLog();
        log.Days ??= [];
        log.Days.RemoveAll(d => d == null || !DateOnly.TryParseExact(d.Date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        foreach (var day in log.Days)
        {
            day.Games ??= [];
            day.Apps ??= [];
            day.ScreenSeconds = Math.Max(0, day.ScreenSeconds);
            day.Pomodoros = Math.Max(0, day.Pomodoros);
        }
        log.Days = [.. log.Days.GroupBy(d => d.Date).Select(g => g.First()).OrderBy(d => d.Date, StringComparer.Ordinal)];
        return log;
    }

    /// <summary>Fichier absent = historique vide ; illisible = mis de côté (« .illisible ») puis historique vide.</summary>
    public static UsageLog Load(string path)
    {
        if (!File.Exists(path)) return new UsageLog();
        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException)
        {
            try
            {
                File.Move(path, path + ".illisible", overwrite: true);
            }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException)
            {
                // on repartira de zéro, le fichier abîmé reste là
            }
            return new UsageLog();
        }
    }

    /// <summary>Écriture atomique : fichier temporaire puis remplacement.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }
}
