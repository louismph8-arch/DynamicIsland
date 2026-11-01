using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile À faire : la liste « À faire » du bloc-notes (<see cref="TodoList"/>). S = ce qui reste (nombre + prochaine tâche) ;
/// W = 2 tâches, L = 6, cochables d'un clic dans l'île ; « + » ouvre la palette sur « todo » (l'île ne prend jamais le clavier).
/// Le reste de la tuile ouvre l'outil Notes.
/// </summary>
public sealed class TodoFace : EverydayFace
{
    private readonly IconChip _chip = TilePieces.Chip(Icons.ListTodo);
    private readonly TextBlock _value;
    private readonly TextBlock _title;
    private readonly TextBlock _info = TilePieces.Info();
    private readonly StackPanel _tasks = new();
    private string _shown = "";

    /// <summary>Bloc-notes lu (auto-test : un bloc-notes à part, jamais le vrai).</summary>
    internal NotesStore Store { get; set; } = NotesService.Store;

    public TodoFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        _value = TilePieces.Number(TypeRamp.Title1);
        _title = TilePieces.Name(definition.Name);
        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto, Auto);
                Root.Children.Add(_chip);
                // Ce qui reste, en haut à droite (comme le chiffre d'une tuile simple) : le bas garde le nom et la prochaine tâche.
                _value.HorizontalAlignment = HorizontalAlignment.Right;
                _value.VerticalAlignment = VerticalAlignment.Top;
                _value.Margin = new Thickness(0, -Space.S1, 0, 0);
                Root.Children.Add(_value);
                Root.Children.Add(Place(_title, 2));
                _info.TextWrapping = TextWrapping.Wrap; // la prochaine tâche sur 2 lignes au plus
                _info.MaxHeight = 2 * TypeRamp.Caption2 * TileGridShape.LineHeight;
                Root.Children.Add(Place(_info, 3));
                break;
            default:
            {
                Rows(Auto, Star, Auto);
                var header = new Grid();
                header.Children.Add(_chip);
                _title.VerticalAlignment = VerticalAlignment.Center;
                _title.Margin = new Thickness(Sizes.IconChip + Space.S3, 0, EverydayTileShape.MiniButton + Space.S2, 0);
                header.Children.Add(_title);
                var plus = TilePieces.GlyphButton(Icons.Plus, Loc.T("tiles.todo.add"), () => Context.OpenPalette?.Invoke(Loc.T("tiles.todo.command") + " "));
                plus.HorizontalAlignment = HorizontalAlignment.Right;
                plus.IsEnabled = Context.OpenPalette != null || App.DeveloperMode;
                header.Children.Add(plus);
                Root.Children.Add(header);
                _tasks.Margin = new Thickness(0, Space.S3, 0, 0);
                Root.Children.Add(Place(_tasks, 1));
                if (Large) Root.Children.Add(Place(_info, 2));
                break;
            }
        }
    }

    private int Visible => TileSize switch { TileSize.W => 2, TileSize.L => 6, _ => 0 };

    protected override void Update()
    {
        var note = TodoList.Find(Store);
        var tasks = TodoList.Tasks(note);
        int left = tasks.Count(t => !t.Done);
        string key = note == null ? "-" : $"{note.Id}|{note.Modified.Ticks}|{note.Text.GetHashCode()}";
        if (key == _shown) return;
        _shown = key;

        _chip.IsActive = left > 0;
        _value.Text = left.ToString(Loc.Culture);
        string summary = tasks.Count == 0 ? Loc.T("tiles.todo.empty")
            : left == 0 ? Loc.T("tiles.todo.all_done")
            : Loc.F(left == 1 ? "tiles.todo.left.one" : "tiles.todo.left.many", left);
        if (Small)
        {
            _value.Visibility = tasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _info.Text = tasks.FirstOrDefault(t => !t.Done) is { Text: { } next } && left > 0 ? next : summary;
            return;
        }
        _title.Text = left > 0 ? $"{Definition.Name} · {left.ToString(Loc.Culture)}" : Definition.Name;
        _info.Text = tasks.Count(t => t.Done) is int done and > 0 && left > 0 ? Loc.F(done == 1 ? "tiles.todo.progress.one" : "tiles.todo.progress.many", left, done) : summary;
        _tasks.Children.Clear();
        if (tasks.Count == 0 || note == null)
        {
            var empty = TilePieces.Info();
            empty.Text = Loc.T("tiles.todo.hint");
            empty.TextWrapping = TextWrapping.Wrap;
            _tasks.Children.Add(empty);
            return;
        }
        string id = note.Id;
        foreach (var task in TodoList.Ordered(tasks).Take(Visible))
        {
            int line = task.Line;
            _tasks.Children.Add(TaskRows.Task(task.Text, task.Done, () =>
            {
                TodoList.Toggle(id, line, Store);
                Refresh();
            }));
        }
    }
}
