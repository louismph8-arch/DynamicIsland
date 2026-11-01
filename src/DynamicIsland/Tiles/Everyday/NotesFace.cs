using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile Notes (enrichie) : la dernière note modifiée — titre puis ses premières lignes (S : le titre, W : 2 lignes, L : 5
/// lignes dont les cases « - [ ] » se cochent au clic, comme dans l'onglet). « + » ouvre la palette sur « note » (l'île ne prend
/// jamais le clavier). Le reste de la tuile ouvre l'outil Notes. La liste « À faire » a sa tuile : elle n'est montrée ici que
/// s'il n'y a pas d'autre note.
/// </summary>
public sealed class NotesFace : EverydayFace
{
    private readonly IconChip _chip = TilePieces.Chip(Icons.Notes);
    private readonly TextBlock _noteTitle;
    private readonly TextBlock _info = TilePieces.Info();
    private readonly StackPanel _lines = new();
    private string _shown = "";

    /// <summary>Bloc-notes lu (auto-test : un bloc-notes à part).</summary>
    internal NotesStore Store { get; set; } = NotesService.Store;

    public NotesFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        _noteTitle = TilePieces.Text(size == TileSize.L ? TypeRamp.Footnote : TypeRamp.Caption, InkRole.TextPrimary, FontWeights.SemiBold);
        var plus = TilePieces.GlyphButton(Icons.Plus, Loc.T("tiles.notes.add"), () => Context.OpenPalette?.Invoke(Loc.T("tiles.notes.command") + " "));
        plus.HorizontalAlignment = HorizontalAlignment.Right;
        plus.VerticalAlignment = VerticalAlignment.Top;
        var name = TilePieces.Name(definition.Name);
        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto, Auto);
                Root.Children.Add(_chip);
                Root.Children.Add(plus);
                Root.Children.Add(Place(name, 2));
                _info.TextWrapping = TextWrapping.Wrap;
                _info.MaxHeight = 2 * TypeRamp.Caption2 * TileGridShape.LineHeight;
                Root.Children.Add(Place(_info, 3));
                break;
            case TileSize.W:
                // Titre à côté de l'icône, puis 2 lignes (pas de pied : le nom de la tuile s'affiche sous la grille au survol).
                Rows(Auto, Star);
                Root.Children.Add(_chip);
                Root.Children.Add(plus);
                _noteTitle.VerticalAlignment = VerticalAlignment.Center;
                _noteTitle.Margin = new Thickness(Sizes.IconChip + Space.S3, 0, EverydayTileShape.MiniButton + Space.S2, 0);
                Root.Children.Add(_noteTitle);
                _lines.Margin = new Thickness(0, Space.S2, 0, 0);
                _lines.ClipToBounds = true;
                Root.Children.Add(Place(_lines, 1));
                break;
            default:
            {
                Rows(Auto, Auto, Star, Auto);
                Root.Children.Add(_chip);
                Root.Children.Add(plus);
                _noteTitle.Margin = new Thickness(0, Space.S3, 0, 0);
                Root.Children.Add(Place(_noteTitle, 1));
                _lines.Margin = new Thickness(0, Space.S1, 0, 0);
                _lines.ClipToBounds = true;
                Root.Children.Add(Place(_lines, 2));
                var footer = new Grid();
                footer.Children.Add(name);
                _info.HorizontalAlignment = HorizontalAlignment.Right;
                _info.VerticalAlignment = VerticalAlignment.Center;
                footer.Children.Add(_info);
                Root.Children.Add(Place(footer, 3));
                break;
            }
        }
    }

    private int Visible => TileSize switch { TileSize.W => 2, TileSize.L => 5, _ => 0 };

    protected override void Update()
    {
        var note = Store.Latest(TodoList.Find(Store)?.Id);
        int count = Store.Count;
        string key = note == null ? $"-|{count}" : $"{note.Id}|{note.Modified.Ticks}|{note.Text.GetHashCode()}|{note.Title}|{count}";
        if (key == _shown) return;
        _shown = key;
        _chip.IsActive = false;
        string counted = TileRegistry.Count(count, "tiles.notes");
        _lines.Children.Clear();
        if (note == null)
        {
            _noteTitle.Text = Loc.T("tiles.notes.empty");
            _info.Text = Small ? Loc.T("tiles.notes.empty") : "";
            var hint = TilePieces.Info();
            hint.Text = Loc.T("tiles.notes.hint");
            hint.TextWrapping = TextWrapping.Wrap;
            if (!Small) _lines.Children.Add(hint);
            return;
        }
        string title = note.DisplayTitle.Length > 0 ? note.DisplayTitle : Loc.T("tiles.notes.untitled");
        _noteTitle.Text = title;
        if (Small)
        {
            _info.Text = title;
            return;
        }
        _info.Text = counted;
        string id = note.Id;
        foreach (var (index, line) in note.PreviewLines().Take(Visible))
        {
            var task = NoteText.Task(line);
            if (task.IsTask)
            {
                int lineIndex = index;
                // Cases cochables seulement en L (en W la ligne est trop petite pour viser juste : elle ouvre l'outil).
                Action? toggle = Large ? () =>
                {
                    Store.ToggleTask(id, lineIndex);
                    Refresh();
                } : null;
                _lines.Children.Add(TaskRows.Task(task.Text, task.Done, toggle, TypeRamp.Caption2));
            }
            else _lines.Children.Add(TaskRows.Line(line));
        }
    }
}
