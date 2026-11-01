using DynamicIsland.Services;
using DynamicIsland.Settings;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>Une tâche de la liste « À faire » : ligne dans la note (pour la cocher), texte, faite ou non.</summary>
public readonly record struct TodoTask(int Line, string Text, bool Done);

/// <summary>
/// Liste « À faire » (tuile de l'île, palette « todo … » / « à faire … ») : une note ordinaire du bloc-notes, titrée « À faire »,
/// faite de cases « - [ ] … ». Elle se modifie aussi dans l'onglet Notes ou l'éditeur. Son id est gardé dans les réglages
/// (<see cref="EverydayTileSettings.TodoNoteId"/>) ; une note « À faire » qui existe déjà est reprise ; supprimée, elle est
/// recréée au prochain ajout.
/// </summary>
public static class TodoList
{
    public const int MaxTaskLength = 200;

    /// <summary>Titres reconnus pour reprendre une note existante (les deux langues).</summary>
    private static readonly string[] Titles = ["À faire", "A faire", "To do", "To-do", "Todo"];

    /// <summary>La note « À faire » (copie), ou null s'il n'y en a pas encore.</summary>
    public static Note? Find(NotesStore? store = null)
    {
        store ??= NotesService.Store;
        var data = SettingsStore.Current.TileData;
        if (data.TodoNoteId.Length > 0 && store.Get(data.TodoNoteId) is { } note) return note;
        string title = Loc.T("tiles.todo.note_title");
        var adopted = store.Items.FirstOrDefault(n => Titles.Append(title).Any(t => string.Equals(n.Title.Trim(), t, StringComparison.CurrentCultureIgnoreCase)));
        return adopted;
    }

    /// <summary>Tâches de la note, dans l'ordre (calcul pur).</summary>
    public static List<TodoTask> Tasks(Note? note)
    {
        var tasks = new List<TodoTask>();
        if (note == null) return tasks;
        var lines = NoteText.Lines(note.Text);
        for (int i = 0; i < lines.Length; i++)
        {
            var task = NoteText.Task(lines[i]);
            if (task.IsTask && task.Text.Length > 0) tasks.Add(new TodoTask(i, task.Text, task.Done));
        }
        return tasks;
    }

    /// <summary>À afficher : d'abord ce qui reste à faire (dans l'ordre), puis ce qui est fait (calcul pur).</summary>
    public static List<TodoTask> Ordered(IEnumerable<TodoTask> tasks) => tasks.OrderBy(t => t.Done).ToList();

    /// <summary>Ajoute une tâche à la fin (la note est créée si besoin). Null si le bloc-notes est plein ou la tâche vide.</summary>
    public static Note? Add(string text, NotesStore? store = null)
    {
        store ??= NotesService.Store;
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length == 0) return null;
        if (text.Length > MaxTaskLength) text = text[..MaxTaskLength];
        var note = Find(store);
        string line = "- [ ] " + text;
        if (note == null)
        {
            note = store.Add(Loc.T("tiles.todo.note_title"), line);
            if (note == null) return null;
        }
        else
        {
            string body = note.Text.TrimEnd();
            if (!store.Update(note.Id, note.Title, body.Length == 0 ? line : body + "\n" + line)) return null;
        }
        var data = SettingsStore.Current.TileData;
        if (data.TodoNoteId != note.Id)
        {
            data.TodoNoteId = note.Id;
            SettingsStore.ScheduleSave();
        }
        return store.Get(note.Id);
    }

    /// <summary>Coche / décoche une tâche (depuis l'île).</summary>
    public static void Toggle(string noteId, int line, NotesStore? store = null) => (store ?? NotesService.Store).ToggleTask(noteId, line);
}
