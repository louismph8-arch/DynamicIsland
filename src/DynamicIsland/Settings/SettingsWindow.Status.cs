using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Dock;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Paramètres → Dock → « Zone d'état » (6B) : affichage et ordre de chaque case, langue du clavier dans l'île.</summary>
public partial class SettingsWindow
{
    private static string StatusGlyph(string id) => id switch
    {
        StatusItem.Keyboard => Icons.Keyboard,
        StatusItem.Updates => Icons.WindowsUpdate,
        StatusItem.Trash => Icons.Trash,
        _ => Icons.FolderDown,
    };

    private void ShowStatusItems()
    {
        var dock = SettingsStore.Current.Dock;
        KeyboardIslandToggle.IsChecked = dock.KeyboardInIsland;
        StatusItemsList.Children.Clear();
        var items = dock.Status;
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = item.Id;
            var row = new Grid { Margin = new Thickness(0, Space.S3, 0, Space.S3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var glyph = new TextBlock
            {
                Text = StatusGlyph(id),
                FontFamily = FontStack.Icons,
                FontSize = IconSize.M,
                Width = Sizes.Choice,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Paint.TextSecondary,
            };
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S5, 0) };
            texts.Children.Add(new TextBlock { Text = Loc.T($"settings.status.{id}"), Style = (Style)FindResource("CardTitle") });
            texts.Children.Add(new TextBlock { Text = Loc.T($"settings.status.{id}.description"), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });

            var up = MoveButton(Icons.ArrowUp, Loc.T("settings.status.up"), index > 0, () => MoveStatus(id, -1));
            var down = MoveButton(Icons.ArrowDown, Loc.T("settings.status.down"), index < items.Count - 1, () => MoveStatus(id, 1));
            var toggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = item.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, 0, 0) };
            toggle.Click += (_, _) =>
            {
                bool on = toggle.IsChecked == true;
                SettingsStore.Update(s => s.Dock.Status.First(i => i.Id == id).Visible = on);
            };

            Grid.SetColumn(texts, 1);
            Grid.SetColumn(up, 2);
            Grid.SetColumn(down, 3);
            Grid.SetColumn(toggle, 4);
            row.Children.Add(glyph);
            row.Children.Add(texts);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(toggle);
            StatusItemsList.Children.Add(row);
        }
    }

    private Button MoveButton(string glyph, string tooltip, bool enabled, Action move)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = glyph,
            FontSize = IconSize.S,
            ToolTip = tooltip,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.35,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.S1, 0, 0, 0),
        };
        button.Click += (_, _) => move();
        return button;
    }

    /// <summary>Monte (-1) ou descend (+1) une case : l'ordre de la liste = l'ordre de gauche à droite dans le dock.</summary>
    private void MoveStatus(string id, int step)
    {
        SettingsStore.Update(s =>
        {
            var list = s.Dock.Status;
            int index = list.FindIndex(i => i.Id == id);
            int target = index + step;
            if (index < 0 || target < 0 || target >= list.Count) return;
            (list[index], list[target]) = (list[target], list[index]);
        });
        ShowStatusItems();
    }

    private void KeyboardIsland_Click(object sender, RoutedEventArgs e)
    {
        bool on = KeyboardIslandToggle.IsChecked == true;
        SettingsStore.Update(s => s.Dock.KeyboardInIsland = on);
    }
}
