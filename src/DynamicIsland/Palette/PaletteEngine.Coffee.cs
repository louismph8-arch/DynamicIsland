using System.Text.RegularExpressions;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Palette;

/// <summary>
/// Palette : « Café » (tuile de l'île, O6) sans passer par la tuile — « café », « café 2 h », « café stop », « coffee », « keep
/// awake »… Toujours de quoi l'arrêter, même quand la tuile n'est pas à l'écran (disposition « Onglets », grille refaite).
/// </summary>
public static partial class PaletteEngine
{
    [GeneratedRegex(@"^(?:caf[ée]|coffee|caffeine|anti[-\s]?veille|rester\s+[ée]veill[ée]|keep\s+awake)\b\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CoffeeCommand();

    /// <summary>
    /// La commande seule : « Arrêter le Café » s'il est en marche, puis les durées proposées ; suivie de « stop » ou d'une durée,
    /// une seule ligne.
    /// </summary>
    private static void AddCoffee(string query, List<PaletteItem> items)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.Coffee)) return;
        var command = CoffeeCommand().Match(query.Trim());
        if (!command.Success) return;
        var rest = command.Groups["rest"].Value.Trim();
        string subtitle = KeepAwake.IsOn ? CoffeeState() : Loc.T("palette.coffee.description");
        PaletteItem Item(string title, Action run) => new(title, subtitle, run) { Glyph = Icons.Coffee };

        if (rest.Length > 0 && StopWord().IsMatch(rest))
        {
            items.Add(Item(Loc.T("palette.coffee.stop"), KeepAwake.Stop));
            return;
        }
        if (rest.Length > 0 && TryParseDuration(rest, out var duration))
        {
            items.Add(Item(Loc.F("tiles.coffee.for", Describe(duration)), () => KeepAwake.Start(duration)));
            return;
        }
        if (KeepAwake.IsOn) items.Add(Item(Loc.T("palette.coffee.stop"), KeepAwake.Stop));
        foreach (var choice in KeepAwake.Choices)
        {
            var picked = choice;
            items.Add(Item(Loc.F("tiles.coffee.for", KeepAwake.ChoiceLabel(choice)), () => KeepAwake.Start(picked)));
        }
    }

    private static string CoffeeState() =>
        KeepAwake.Remaining is { } left ? Loc.F("tiles.coffee.left", KeepAwake.FormatRemaining(left)) : Loc.T("tiles.coffee.on_always");

    /// <summary>--self-test : titres proposés pour une requête (vide = pas une commande Café).</summary>
    internal static List<string> CoffeeTitles(string query)
    {
        var items = new List<PaletteItem>();
        AddCoffee(query, items);
        return items.Select(i => i.Title).ToList();
    }
}
