using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Notes;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Notes » de l'île : les notes (épinglées d'abord), leur titre (1re ligne s'il n'y en a pas), un aperçu en Markdown
/// léger et la date ; les cases « - [ ] » se cochent d'un clic dans l'île. Clic sur une note = l'éditeur (fenêtre activable :
/// l'île ne prend jamais le clavier) ; « + Nouvelle note » en haut. Mis à jour seulement quand l'onglet est ouvert.
/// </summary>
public sealed class NotesView : UserControl
{
    private readonly TextBlock _count;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroller;
    private bool _active, _dirty = true;
    private List<Note>? _sample; // rendus --snapshot : notes d'exemple en mémoire

    public NotesView()
    {
        Width = IslandShape.NotesContentWidth;
        var panel = new DockPanel { LastChildFill = true };

        var header = new Grid { Margin = new Thickness(Space.S2, 0, Space.S1, Space.S3) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _count = new TextBlock { FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(_count);
        var create = new Button { Style = (Style)FindResource("ChipButton"), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("notes.new.tooltip") };
        var label = new TextBlock { FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, Foreground = Paint.White };
        label.Inlines.Add(new Run(Icons.Plus + "  ") { FontFamily = FontStack.Icons });
        label.Inlines.Add(new Run(Loc.T("notes.new")));
        create.Content = label;
        create.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        create.Click += (_, _) =>
        {
            if (_sample == null) NoteEditorWindow.Open(null);
        };
        Grid.SetColumn(create, 1);
        header.Children.Add(create);
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        panel.Children.Add(header);

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _list,
            Height = IslandShape.NotesListHeight,
        };
        if (Application.Current?.TryFindResource("ThinScrollBar") is Style thin) _scroller.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), thin);
        panel.Children.Add(_scroller);
        Content = panel;

        NotesService.Store.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            _dirty = true;
            if (_active) Refresh();
        });
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value && _dirty) Refresh();
        }
    }

    private void Refresh()
    {
        _dirty = false;
        _list.Children.Clear();
        var notes = _sample ?? NotesService.Store.Items;
        _count.Text = notes.Count == 0 ? Loc.T("island.notes") : Loc.F("notes.count", notes.Count);
        if (notes.Count == 0)
        {
            _list.Children.Add(Empty());
            return;
        }
        foreach (var note in notes) _list.Children.Add(Card(note));
    }

    private FrameworkElement Card(Note note)
    {
        var grid = new Grid { Margin = new Thickness(Space.S3, Space.S3, Space.S4, Space.S3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var stripe = new SquircleBorder
        {
            Width = IslandShape.NoteStripe, CornerRadius = Corners.Pill, Margin = new Thickness(0, 0, Space.S4, 0),
            Background = new SolidColorBrush(Swatch.NoteColor(note.Color)),
        };
        grid.Children.Add(stripe);

        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = note.DisplayTitle;
        top.Children.Add(new TextBlock
        {
            Text = title.Length > 0 ? title : Loc.T("notes.untitled"),
            FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = title.Length > 0 ? Paint.TextPrimary : Paint.TextTertiary,
        });
        var meta = new TextBlock { FontSize = TypeRamp.Caption, Foreground = Paint.TextTertiary, Margin = new Thickness(Space.S4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var (done, total) = NoteText.Progress(note.Text);
        if (note.Pinned) meta.Inlines.Add(new Run(Icons.Pin + "  ") { FontFamily = FontStack.Icons });
        if (total > 0) meta.Inlines.Add(new Run($"{done}/{total}  ·  "));
        meta.Inlines.Add(new Run(When(note.Modified)));
        Grid.SetColumn(meta, 1);
        top.Children.Add(meta);
        content.Children.Add(top);

        foreach (var (index, line) in note.PreviewLines().Take(IslandShape.NotePreviewLines))
            content.Children.Add(PreviewLine(note, index, line));

        var card = Design.OpalTile.Apply(new SquircleBorder { CornerRadius = Corners.M, Child = grid, Margin = new Thickness(0, 0, 0, Space.S2), Cursor = Cursors.Hand, ToolTip = Loc.T("notes.open.hint") }, glow: false);
        card.MouseEnter += (_, _) => Design.OpalTile.Hover(card, true); // verre opalin (O1) : un peu plus laiteuse au survol
        card.MouseLeave += (_, _) => Design.OpalTile.Hover(card, false);
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (_sample == null) NoteEditorWindow.Open(note.Id);
            e.Handled = true;
        };
        return card;
    }

    /// <summary>Une ligne d'aperçu : case à cocher cliquable, sinon le Markdown léger sur une ligne.</summary>
    private FrameworkElement PreviewLine(Note note, int index, string line)
    {
        var task = NoteText.Task(line);
        if (!task.IsTask)
        {
            var text = new TextBlock { FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, Space.S1, 0, 0) };
            ClaudeView.Markdown(line.Trim(), text.Inlines);
            return text;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S1, 0, 0) };
        var box = new TextBlock
        {
            Text = task.Done ? Icons.TaskDone : Icons.TaskOpen, FontFamily = FontStack.Icons, FontSize = IconSize.M,
            Foreground = task.Done ? Paint.TextPrimary : Paint.TextSecondary, Background = Paint.HitTarget,
            Padding = new Thickness(0, 0, Space.S3, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T(task.Done ? "notes.task.uncheck" : "notes.task.check"),
        };
        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // la note ne s'ouvre pas : on coche seulement
            if (_sample == null) NotesService.Store.ToggleTask(note.Id, index);
        };
        row.Children.Add(box);
        var label = new TextBlock
        {
            FontSize = TypeRamp.Caption, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = IslandShape.NotesContentWidth - Space.S18 * 2,
            Foreground = task.Done ? Paint.TextTertiary : Paint.TextSecondary,
            TextDecorations = task.Done ? TextDecorations.Strikethrough : null,
        };
        ClaudeView.Markdown(task.Text, label.Inlines);
        row.Children.Add(label);
        return row;
    }

    private static FrameworkElement Empty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(Space.S8, Space.S10, Space.S8, 0) };
        panel.Children.Add(new TextBlock { Text = Icons.Notes, FontFamily = FontStack.Icons, FontSize = IconSize.XL, Foreground = Paint.TextTertiary, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = Loc.T("notes.empty"), FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, Space.S3, 0, 0) });
        return panel;
    }

    internal static string When(DateTime time) =>
        time.Date == DateTime.Today ? time.ToString("t", Loc.Culture)
        : time.Date == DateTime.Today.AddDays(-1) ? Loc.F("clipboard.yesterday", time.ToString("t", Loc.Culture))
        : time.ToString("d MMM", Loc.Culture);

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : notes d'exemple (notes.json n'est jamais lu ni écrit).</summary>
    internal void ShowSample()
    {
        _sample = SampleNotes();
        _scroller.ScrollToTop();
        Refresh();
    }

    internal static List<Note> SampleNotes()
    {
        var now = DateTime.Now;
        return
        [
            new Note { Title = Loc.T("notes.sample.shopping"), Text = Loc.T("notes.sample.shopping.text"), Pinned = true, Color = "yellow", Modified = now.AddMinutes(-5) },
            new Note { Text = Loc.T("notes.sample.idea"), Color = "violet", Modified = now.AddHours(-2) },
            new Note { Title = Loc.T("notes.sample.games"), Text = Loc.T("notes.sample.games.text"), Color = "green", Modified = now.AddDays(-1) },
        ];
    }
}
