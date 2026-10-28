using DynamicIsland.Services.Seasons;

namespace DynamicIsland.Services;

/// <summary>Auto-test des thèmes saisonniers (6F) : les dates de chaque saison.</summary>
internal static partial class SelfTest
{
    private static void Seasons(List<string> lines)
    {
        var cases = new (DateTime Date, Season Expected)[]
        {
            (new(2026, 10, 23), Season.None), (new(2026, 10, 24), Season.Halloween), (new(2026, 10, 31), Season.Halloween), (new(2026, 11, 1), Season.Halloween),
            (new(2026, 11, 2), Season.None), (new(2026, 12, 1), Season.Christmas), (new(2026, 12, 25), Season.Christmas), (new(2026, 12, 27), Season.None),
            (new(2026, 12, 31), Season.NewYear), (new(2027, 1, 1), Season.NewYear), (new(2027, 1, 2), Season.None),
            (new(2027, 2, 14), Season.Valentine), (new(2027, 2, 15), Season.None),
        };
        var wrong = cases.Where(c => SeasonService.ForDate(c.Date) != c.Expected).Select(c => $"{c.Date:d MMM}").ToList();
        Check(lines, wrong.Count == 0, wrong.Count == 0
            ? $"saisons aux bonnes dates (aujourd'hui : {SeasonService.ForDate(DateTime.Now)})"
            : "dates fausses : " + string.Join(", ", wrong));
    }
}
