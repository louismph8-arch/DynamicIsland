using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using DynamicIsland.Views;

namespace DynamicIsland.Notes;

/// <summary>
/// Éditeur d'une note, DANS Opaline (jamais le Bloc-notes) : fenêtre activable (l'île ne prend pas le clavier), au style de la
/// carte d'Opaline, redimensionnable. Titre facultatif + texte en Markdown léger (gras, puces, cases « - [ ] » que l'île sait
/// cocher). Enregistré tout seul 500 ms après la dernière frappe (et en fermant) : pas de bouton obligatoire. Épingler,
/// couleur (pastilles), supprimer (confirmation). Échap = fermer, c'est déjà enregistré. Une note vide n'est pas gardée.
/// </summary>
public sealed class NoteEditorWindow : CardWindow
{
    private static readonly Dictionary<string, NoteEditorWindow> OpenEditors = [];

    private string? _id;
    private bool _pinned, _deleted;
    private string _color;
    private readonly TextBox _title = new(), _text = new();
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pin = new(), _delete = new();
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>
    /// Ouvre une note (<paramref name="id"/>) ou une nouvelle note (null, pré-remplie avec <paramref name="text"/>). Une note
    /// déjà ouverte revient simplement devant.
    /// </summary>
    public static void Open(string? id, string? text = null)
    {
        try
        {
            if (ModuleGuard.IsDisabled(ModuleGuard.NotesTab)) return;
            if (id != null && OpenEditors.TryGetValue(id, out var existing))
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            Note? note = null;
            if (id != null && (note = NotesService.Store.Get(id)) == null) return; // effacée entre-temps
            if (note == null && NotesService.Store.IsFull)
            {
                MessageBox.Show(Loc.T("notes.full"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information,
                    MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                return;
            }
            var editor = new NoteEditorWindow(note, text, snapshot: false);
            editor.Topmost = true; // l'île (au premier plan) ne doit pas la cacher
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            if (!ModuleGuard.TryFail(ex)) Log.Error("Notes : ouverture de l'éditeur", ex);
        }
    }

    private NoteEditorWindow(Note? note, string? text, bool snapshot)
        : base(Loc.T("notes.editor.title"), CardShape.NoteEditorWidth, CardShape.NoteEditorHeight, snapshot)
    {
        _id = note?.Id;
        _pinned = note?.Pinned ?? false;
        _color = note?.Color ?? "";
        if (_id != null && !snapshot) OpenEditors[_id] = this;
        Body.Children.Add(BuildBody(note?.Title ?? "", note?.Text ?? text ?? ""));
        BuildFooter();
        EnableResize(CardShape.NoteEditorMinWidth, CardShape.NoteEditorMinHeight);
        ShowPin();
        ShowSwatches();
        _status.Text = Loc.T(_id == null ? "notes.editor.autosave" : "notes.editor.saved");

        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            Commit();
        };
        _title.TextChanged += (_, _) => Typed();
        _text.TextChanged += (_, _) => Typed();
        Loaded += (_, _) =>
        {
            _text.Focus(); // on écrit tout de suite (le titre est facultatif)
            _text.CaretIndex = _text.Text.Length;
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            if (_id != null && OpenEditors.TryGetValue(_id, out var open) && open == this) OpenEditors.Remove(_id);
        };
    }

    private FrameworkElement BuildBody(string title, string text)
    {
        var page = new Grid { Margin = new Thickness(Space.S14, Space.S10, Space.S14, 0) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition());
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Barre du haut : pastilles de couleur à gauche, case / épingler / supprimer à droite.
        var bar = new Grid { Margin = new Thickness(0, 0, 0, Space.S6) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(_swatches);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var task = Tool(Icons.ListChecks, Loc.T("notes.editor.task"));
        task.Click += (_, _) => InsertTask();
        tools.Children.Add(task);
        _pin.Click += (_, _) => TogglePin();
        tools.Children.Add(Tool(_pin, Icons.Pin, ""));
        _delete.Click += (_, _) => Delete();
        Tool(_delete, Icons.Trash, Loc.T("notes.editor.delete"));
        _delete.Foreground = Paint.Danger;
        tools.Children.Add(_delete);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);
        page.Children.Add(bar);

        Field(_title, title, NotesStore.MaxTitleLength);
        _title.FontFamily = FontStack.Display;
        _title.FontSize = TypeRamp.Headline;
        _title.FontWeight = FontWeights.SemiBold;
        _title.ToolTip = Loc.T("notes.editor.title.tip");
        var titleBlock = new StackPanel();
        titleBlock.Children.Add(Label(Loc.T("notes.editor.title.label")));
        titleBlock.Children.Add(_title);
        Grid.SetRow(titleBlock, 1);
        page.Children.Add(titleBlock);

        Field(_text, text, NotesStore.MaxTextLength);
        _text.Height = double.NaN;
        _text.VerticalAlignment = VerticalAlignment.Stretch;
        _text.AcceptsReturn = true;
        _text.AcceptsTab = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.Padding = new Thickness(0, Space.S3, 0, Space.S3);
        _text.Margin = new Thickness(0, Space.S5, 0, 0);
        Grid.SetRow(_text, 2);
        page.Children.Add(_text);

        var hint = new TextBlock
        {
            Text = Loc.T("notes.editor.markdown"), FontSize = TypeRamp.Caption, Foreground = Paint.TextTertiary,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Space.S3, 0, 0),
        };
        Grid.SetRow(hint, 3);
        page.Children.Add(hint);

        int index = 0;
        foreach (UIElement child in page.Children) Reveal(child, index++);
        return page;
    }

    private void BuildFooter()
    {
        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Margin = new Thickness(0, 0, Space.S6, 0);
        Footer.Children.Add(_status);
        var close = PrimaryButton(Loc.T("notes.editor.close"));
        close.Margin = new Thickness(0, 0, Space.S8, 0); // le coin pour agrandir est juste à droite
        close.Click += (_, _) => CloseAnimated();
        Grid.SetColumn(close, 1);
        Footer.Children.Add(close);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _autosave.Stop();
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement == _title)
            {
                _text.Focus(); // Entrée dans le titre : on passe au texte
                e.Handled = true;
            }
        };
    }

    // ------------------------------------------------------------------ enregistrement

    private void Typed()
    {
        if (Snapshot) return;
        _autosave.Stop();
        _autosave.Start();
        _status.Text = Loc.T("notes.editor.typing");
    }

    /// <summary>Range le titre et le texte (la note n'est créée qu'au premier caractère tapé).</summary>
    private void Commit()
    {
        if (Snapshot || _deleted) return;
        try
        {
            var title = _title.Text;
            var text = _text.Text;
            bool empty = title.Trim().Length == 0 && text.Trim().Length == 0;
            if (_id == null)
            {
                if (empty) return;
                var note = NotesService.Store.Add(title, text, _pinned, _color);
                if (note == null)
                {
                    _status.Text = Loc.T("notes.full");
                    _status.Foreground = Paint.Timer;
                    return;
                }
                _id = note.Id;
                OpenEditors[_id] = this;
            }
            else
            {
                NotesService.Store.Update(_id, title, text);
            }
            _status.Text = Loc.F("notes.editor.saved_at", DateTime.Now.ToString("t", Loc.Culture));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Notes : enregistrement depuis l'éditeur", ex);
            _status.Text = Loc.T("notes.editor.error");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Snapshot) return;
        _autosave.Stop();
        Commit();
        // Une note vidée n'est pas gardée (titre et texte vides).
        if (!_deleted && _id != null && _title.Text.Trim().Length == 0 && _text.Text.Trim().Length == 0) NotesService.Store.Delete(_id);
        NotesService.Flush();
    }

    // ------------------------------------------------------------------ outils

    private void InsertTask()
    {
        // Au début de la ligne du curseur (« - [ ] »), ou sur une nouvelle ligne à la fin si la ligne a déjà du texte.
        int caret = _text.CaretIndex;
        var content = _text.Text;
        int start = caret == 0 ? 0 : content.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        int end = content.IndexOf('\n', caret);
        var line = content[start..(end < 0 ? content.Length : end)].Replace("\r", "");
        // Déjà une case : une nouvelle case en dessous ; sinon la ligne devient une case.
        bool below = NoteText.Task(line).IsTask;
        string insert = below ? Environment.NewLine + "- [ ] " : "- [ ] ";
        int at = below ? (end < 0 ? content.Length : end) : start;
        _text.Text = content.Insert(at, insert);
        _text.CaretIndex = at + insert.Length;
        _text.Focus();
    }

    private void TogglePin()
    {
        _pinned = !_pinned;
        if (_id != null && !Snapshot) NotesService.Store.Pin(_id, _pinned);
        ShowPin();
    }

    private void ShowPin()
    {
        _pin.ToolTip = Loc.T(_pinned ? "notes.editor.unpin" : "notes.editor.pin");
        // Épinglée : l'épingle prend la couleur d'accent (le bouton dit l'état, l'info-bulle l'action).
        if (_pinned) _pin.SetResourceReference(ForegroundProperty, "AccentBrush");
        else _pin.Foreground = Paint.TextSecondary;
    }

    private void ShowSwatches()
    {
        _swatches.Children.Clear();
        foreach (var (id, name, color) in Swatch.NoteColors)
        {
            bool selected = id == _color;
            var dot = new SquircleBorder
            {
                Width = CardShape.Swatch, Height = CardShape.Swatch, CornerRadius = Corners.Pill,
                Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, Space.S3, 0), Cursor = Cursors.Hand,
                BorderThickness = selected ? Borders.Selected : new Thickness(0), BorderBrush = Paint.TextPrimary,
                ToolTip = Loc.T(name),
            };
            if (id.Length == 0)
                dot.Child = new TextBlock { Text = Icons.Close, FontFamily = FontStack.Icons, FontSize = IconSize.XS, Foreground = Paint.TextSecondary,
                                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _color = id;
                if (_id != null && !Snapshot) NotesService.Store.SetColor(_id, id);
                ShowSwatches();
            };
            _swatches.Children.Add(dot);
        }
    }

    private void Delete()
    {
        if (Snapshot) return;
        if (_id == null && _title.Text.Trim().Length == 0 && _text.Text.Trim().Length == 0)
        {
            CloseAnimated(); // rien d'écrit : rien à supprimer
            return;
        }
        var name = _title.Text.Trim().Length > 0 ? _title.Text.Trim() : NoteText.Plain(NoteText.Lines(_text.Text).FirstOrDefault(l => l.Trim().Length > 0) ?? "");
        if (MessageBox.Show(this, Loc.F("notes.delete.confirm", name.Length > 0 ? name : Loc.T("notes.untitled")), AppInfo.Name,
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        _autosave.Stop();
        _deleted = true;
        if (_id != null) NotesService.Store.Delete(_id);
        NotesService.Flush();
        CloseAnimated();
    }

    // ------------------------------------------------------------------ petits éléments

    private Button Tool(string glyph, string tooltip) => Tool(new Button(), glyph, tooltip);

    private Button Tool(Button button, string glyph, string tooltip)
    {
        button.Style = (Style)FindResource("SettingsButton");
        button.Content = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.M };
        if (tooltip.Length > 0) button.ToolTip = tooltip;
        button.Margin = new Thickness(Space.S3, 0, 0, 0);
        return button;
    }

    private void Field(TextBox box, string text, int max)
    {
        box.Style = (Style)FindResource("HexBox");
        box.Width = double.NaN;
        box.MaxLength = max;
        box.FontFamily = FontStack.Ui;
        box.Text = text;
    }

    private TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("CardTitle"),
        Margin = new Thickness(0, 0, 0, Space.S3),
    };

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : une note d'exemple (rien n'est lu ni écrit dans notes.json).</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var sample = NotesView.SampleNotes()[0];
        var window = new NoteEditorWindow(sample, null, snapshot: true);
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "3m-note-editeur.png"));
        window.Close();
    }
}
