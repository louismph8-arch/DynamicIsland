using System.Text.RegularExpressions;
using DynamicIsland.Notes;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using DynamicIsland.Vault;

namespace DynamicIsland.Palette;

/// <summary>Palette : notes (« note … », « notes ») et textes ajoutés au presse-papiers (« copier … », « + … »).</summary>
public static partial class PaletteEngine
{
    [GeneratedRegex(@"^(?:note|nouvelle\s+note|new\s+note)(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteCommand();

    [GeneratedRegex(@"^(?:(?:copier|copie|copy)\s+|\+\s+)(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ClipCommand();

    private static string Shown(string text) => text.Length > 70 ? text[..70] + "…" : text;

    /// <summary>« note acheter du lait » = nouvelle note avec ce texte ; « note » seul = l'éditeur ; « notes » = l'onglet.</summary>
    private static void AddNotes(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.NotesTab)) return;
        var command = NoteCommand().Match(query);
        if (command.Success)
        {
            var text = command.Groups["rest"].Value.Trim();
            if (text.Length == 0)
            {
                items.Add(new PaletteItem(Loc.T("palette.note.new"), Loc.T("palette.note.new.description"), () => NoteEditorWindow.Open(null)) { Glyph = Icons.NotePen });
            }
            else
            {
                items.Add(new PaletteItem(Loc.F("palette.note.add", Shown(text)), Loc.T("palette.note.add.description"), () =>
                {
                    var note = NotesService.Store.Add("", text);
                    actions.Notice?.Invoke(Icons.Notes, Loc.T(note == null ? "notes.full" : "palette.note.added"));
                }) { Glyph = Icons.Notes });
                items.Add(new PaletteItem(Loc.T("palette.note.edit"), Loc.T("palette.note.edit.description"), () => NoteEditorWindow.Open(null, text)) { Glyph = Icons.NotePen });
            }
        }
        if (actions.OpenNotes is { } open && AppCatalog.MatchScore(Loc.T("palette.notes.alias"), query) >= 3)
            items.Add(new PaletteItem(Loc.T("palette.notes"), Loc.T("palette.notes.description"), open) { Glyph = Icons.Notes });
    }

    /// <summary>« copier un texte » ou « + un texte » : ajouté en tête de l'historique et copié (par Opaline : pas en double).</summary>
    private static void AddClipText(string query, PaletteActions actions, List<PaletteItem> items)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.ClipboardTab)) return;
        var command = ClipCommand().Match(query);
        if (!command.Success) return;
        var text = command.Groups["rest"].Value.Trim();
        if (text.Length == 0) return;
        items.Add(new PaletteItem(Loc.F("palette.clip.add", Shown(text)), Loc.T("palette.clip.add.description"), () =>
        {
            bool copied = ClipboardHistory.Store.AddManual(text, pinned: false) is { } item && ClipboardHistory.Copy(item);
            actions.Notice?.Invoke(Icons.ClipboardPlus, Loc.T(copied ? "palette.clip.added" : "clipboard.copy.failed"));
        }) { Glyph = Icons.ClipboardPlus });
        items.Add(new PaletteItem(Loc.T("palette.clip.edit"), Loc.T("palette.clip.edit.description"), () => ClipTextWindow.Open(null, text)) { Glyph = Icons.Pencil });
    }
}
