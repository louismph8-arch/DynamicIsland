using System.Text.RegularExpressions;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using DynamicIsland.Tiles.Everyday;

namespace DynamicIsland.Palette;

/// <summary>Palette : liste « À faire » de la tuile de l'île (« todo acheter du lait », « à faire appeler Max »).</summary>
public static partial class PaletteEngine
{
    [GeneratedRegex(@"^(?:todo|to-do|to\s+do|[àa]\s+faire|t[âa]che)(?:\s*:)?(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TodoCommand();

    /// <summary>« todo » + texte = une tâche ajoutée à la liste « À faire » ; « todo » seul = la liste (onglet Notes).</summary>
    private static void AddTodo(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.NotesTab)) return;
        var command = TodoCommand().Match(query.Trim());
        if (!command.Success) return;
        var text = command.Groups["rest"].Value.Trim();
        if (text.Length == 0)
        {
            if (actions.OpenNotes is { } open)
                items.Add(new PaletteItem(Loc.T("palette.todo.list"), Loc.T("palette.todo.list.description"), open) { Glyph = Icons.ListTodo });
            return;
        }
        items.Add(new PaletteItem(Loc.F("palette.todo.add", Shown(text)), Loc.T("palette.todo.add.description"), () =>
        {
            var note = TodoList.Add(text);
            actions.Notice?.Invoke(Icons.ListTodo, Loc.T(note == null ? "notes.full" : "palette.todo.added"));
        }) { Glyph = Icons.ListTodo });
    }

    /// <summary>--self-test : la commande reconnue et le texte de la tâche (null = pas une commande « À faire »).</summary>
    internal static string? TodoText(string query)
    {
        var command = TodoCommand().Match(query.Trim());
        return command.Success ? command.Groups["rest"].Value.Trim() : null;
    }
}
