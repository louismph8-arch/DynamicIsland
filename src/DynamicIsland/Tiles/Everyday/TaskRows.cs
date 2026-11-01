using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Lignes d'une note dans une tuile (Notes, À faire) : une case à cocher (« - [ ] ») se coche au clic quand
/// <paramref name="onToggle"/> est donné ; une ligne de texte reste un aperçu. Le clic sur une case ne déplace pas l'île et
/// n'ouvre pas l'outil.
/// </summary>
public static class TaskRows
{
    public static FrameworkElement Task(string text, bool done, Action? onToggle, double fontSize = TypeRamp.Caption)
    {
        var row = new Grid { MinHeight = EverydayTileShape.TaskRowHeight, Background = onToggle != null ? Paint.HitTarget : null };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var box = TilePieces.Glyph(done ? Icons.TaskDone : Icons.TaskOpen, IconSize.S, done ? InkRole.TextTertiary : InkRole.TextSecondary);
        if (done) box.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentBrushKey);
        box.VerticalAlignment = VerticalAlignment.Center;
        var label = TilePieces.Text(fontSize, done ? InkRole.TextTertiary : InkRole.TextPrimary);
        label.Text = text;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(Space.S3, 0, 0, 0);
        if (done) label.TextDecorations = TextDecorations.Strikethrough;
        Grid.SetColumn(label, 1);
        row.Children.Add(box);
        row.Children.Add(label);
        if (onToggle != null)
        {
            row.Cursor = Cursors.Hand;
            row.ToolTip = Loc.T(done ? "tiles.todo.uncheck" : "tiles.todo.check");
            bool pressed = false;
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                pressed = row.CaptureMouse();
            };
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!pressed) return;
                pressed = false;
                var point = e.GetPosition(row);
                row.ReleaseMouseCapture();
                if (point.X >= 0 && point.Y >= 0 && point.X <= row.ActualWidth && point.Y <= row.ActualHeight) onToggle();
            };
            row.LostMouseCapture += (_, _) => pressed = false;
        }
        return row;
    }

    /// <summary>Ligne d'aperçu ordinaire (texte d'une note, sans marques Markdown).</summary>
    public static FrameworkElement Line(string text, double fontSize = TypeRamp.Caption2)
    {
        var label = TilePieces.Text(fontSize, InkRole.TextSecondary);
        label.Text = NoteText.Plain(text);
        label.MinHeight = EverydayTileShape.TaskRowHeight - Space.S2;
        return label;
    }
}
