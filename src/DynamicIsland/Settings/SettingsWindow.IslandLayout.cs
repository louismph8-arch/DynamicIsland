using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Settings;

/// <summary>
/// Page Île, priorité 2 : taille de l'île, onglets (cacher, réordonner, en colonne), mode Fixe / Intelligent / Peek, raccourci
/// « Cacher / afficher l'île » et règles par application (partagées avec le dock).
/// </summary>
public partial class SettingsWindow
{
    private static readonly (string Id, string Glyph, string Label)[] IslandTabInfo =
    [
        (IslandTabItem.Music, Icons.Music, "island.musique"), (IslandTabItem.Calendar, Icons.Calendar, "island.calendrier"),
        (IslandTabItem.Control, Icons.Controls, "island.controles"), (IslandTabItem.Widgets, Icons.Widgets, "island.widgets"),
        (IslandTabItem.Shelf, Icons.Shelf, "island.etagere"), (IslandTabItem.Voice, Icons.Voice, "island.vocal"),
        (IslandTabItem.Claude, Icons.Sparkles, ""), (IslandTabItem.Clocks, Icons.Clock, "island.clocks"),
        (IslandTabItem.Health, Icons.Health, "island.health"), (IslandTabItem.Friends, Icons.Friends, "island.friends"),
        (IslandTabItem.Mixer, Icons.Mixer, "island.mixer"),
        (IslandTabItem.Clipboard, Icons.Clipboard, "island.clipboard"),
        (IslandTabItem.Notes, Icons.Notes, "island.notes"),
    ];

    private void ShowIslandLayout()
    {
        var island = SettingsStore.Current.Island;
        bool loading = _loading;
        _loading = true;
        IslandScaleSlider.Value = island.Scale;
        IslandScaleText.Text = Loc.F("format.percent", Math.Round(island.Scale * 100));
        IslandVerticalTabsToggle.IsChecked = island.VerticalTabs;
        (island.Mode switch
        {
            IslandMode.Smart => IslandSmartMode,
            IslandMode.Peek => IslandPeekMode,
            _ => IslandFixedMode,
        }).IsChecked = true;
        _loading = loading;
        ShowIslandTabs();
        ShowAppRules();
    }

    // ------------------------------------------------------------------ taille

    private void IslandScale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        double scale = Math.Round(IslandScaleSlider.Value, 2);
        IslandScaleText.Text = Loc.F("format.percent", Math.Round(scale * 100));
        SettingsStore.Update(s => s.Island.Scale = scale);
    }

    private void IslandScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)) return;
        SettingsStore.Update(s => s.Island.Scale = scale);
        ShowIslandLayout();
    }

    // ------------------------------------------------------------------ onglets

    private void ShowIslandTabs()
    {
        IslandTabsList.Children.Clear();
        var items = SettingsStore.Current.Island.Tabs;
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var id = item.Id;
            var info = IslandTabInfo.First(t => t.Id == id);
            var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new TextBlock
            {
                Text = info.Glyph, FontFamily = FontStack.Icons, FontSize = IconSize.M, Width = Sizes.Choice,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Paint.TextSecondary,
            };
            var name = new TextBlock { Text = info.Label.Length > 0 ? Loc.T(info.Label) : ClaudeService.AssistantName, VerticalAlignment = VerticalAlignment.Center };
            var up = MoveButton(Icons.ArrowUp, Loc.T("settings.island.tabs.up"), index > 0, () => MoveIslandTab(id, -1));
            var down = MoveButton(Icons.ArrowDown, Loc.T("settings.island.tabs.down"), index < items.Count - 1, () => MoveIslandTab(id, 1));
            var toggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = item.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, 0, 0) };
            toggle.Click += (_, _) =>
            {
                bool on = toggle.IsChecked == true;
                SettingsStore.Update(s => s.Island.Tabs.First(i => i.Id == id).Visible = on);
                ShowIslandTabs(); // au moins un onglet reste affiché (Normalize)
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(up, 2);
            Grid.SetColumn(down, 3);
            Grid.SetColumn(toggle, 4);
            row.Children.Add(glyph);
            row.Children.Add(name);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(toggle);
            IslandTabsList.Children.Add(row);
        }
    }

    private void MoveIslandTab(string id, int step)
    {
        SettingsStore.Update(s =>
        {
            var list = s.Island.Tabs;
            int index = list.FindIndex(i => i.Id == id);
            int target = index + step;
            if (index < 0 || target < 0 || target >= list.Count) return;
            (list[index], list[target]) = (list[target], list[index]);
        });
        ShowIslandTabs();
    }

    private void IslandVerticalTabs_Click(object sender, RoutedEventArgs e)
    {
        bool on = IslandVerticalTabsToggle.IsChecked == true;
        SettingsStore.Update(s => s.Island.VerticalTabs = on);
    }

    // ------------------------------------------------------------------ mode

    private void IslandMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var mode = IslandSmartMode.IsChecked == true ? IslandMode.Smart : IslandPeekMode.IsChecked == true ? IslandMode.Peek : IslandMode.Fixed;
        SettingsStore.Update(s => s.Island.Mode = mode);
    }

    private void DefaultIslandHotkey_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Island.ToggleHotkey = Hotkeys.DefaultIsland);

    private void OpenAppRules_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage.Island);
        Dispatcher.BeginInvoke(() =>
        {
            Scroller.UpdateLayout();
            var top = AppRulesCard.TranslatePoint(new Point(0, 0), PagesHost).Y;
            Scroller.ScrollToVerticalOffset(Math.Max(0, top - SettingsShape.SearchScrollMargin));
            Highlight(AppRulesCard);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ règles par application

    private static string BehaviorText(AppBehavior behavior) => Loc.T(behavior switch
    {
        AppBehavior.Hide => "settings.rules.hide",
        AppBehavior.Show => "settings.rules.show",
        _ => "settings.rules.default",
    });

    private void ShowAppRules()
    {
        AppRulesList.Children.Clear();
        var rules = SettingsStore.Current.AppRules;
        if (rules.Count == 0)
        {
            AppRulesList.Children.Add(new TextBlock { Text = Loc.T("settings.rules.empty"), Style = (Style)FindResource("CardDescription") });
            return;
        }
        foreach (var rule in rules)
        {
            var process = rule.Process;
            var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = rule.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = rule.Process + ".exe" };
            var island = BehaviorButton(Loc.F("settings.rules.island", BehaviorText(rule.Island)), choice =>
                SettingsStore.Update(s => s.AppRules.First(r => r.Process == process).Island = choice));
            var dock = BehaviorButton(Loc.F("settings.rules.dock_label", BehaviorText(rule.Dock)), choice =>
                SettingsStore.Update(s => s.AppRules.First(r => r.Process == process).Dock = choice));
            var remove = new Button
            {
                Style = (Style)FindResource("SmallIconButton"), Content = Icons.Close, FontSize = IconSize.S, ToolTip = Loc.T("settings.rules.remove"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S3, 0, 0, 0),
            };
            remove.Click += (_, _) =>
            {
                SettingsStore.Update(s => s.AppRules.RemoveAll(r => r.Process == process));
                ShowAppRules();
            };
            var stage = StageRuleButton(rule.StageIgnore, ignore => SettingsStore.Update(s => s.AppRules.First(r => r.Process == process).StageIgnore = ignore));
            island.Margin = new Thickness(Space.S3, 0, 0, 0);
            dock.Margin = new Thickness(Space.S3, 0, 0, 0);
            stage.Margin = new Thickness(Space.S3, 0, 0, 0);
            Grid.SetColumn(island, 1);
            Grid.SetColumn(dock, 2);
            Grid.SetColumn(stage, 3);
            Grid.SetColumn(remove, 4);
            row.Children.Add(name);
            row.Children.Add(island);
            row.Children.Add(dock);
            row.Children.Add(stage);
            row.Children.Add(remove);
            AppRulesList.Children.Add(row);
        }
    }

    /// <summary>Bouton « Île : se cache ▾ » : menu Par défaut / Se cache / Toujours visible.</summary>
    private Button BehaviorButton(string label, Action<AppBehavior> pick)
    {
        var button = new Button { Style = (Style)FindResource("SettingsButton"), Content = label + "  ▾", VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var choice in new[] { AppBehavior.Default, AppBehavior.Hide, AppBehavior.Show })
            {
                var item = new MenuItem { Header = BehaviorText(choice) };
                item.Click += (_, _) =>
                {
                    pick(choice);
                    ShowAppRules();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    /// <summary>Bouton « Stage : géré ▾ » (7G) : menu Géré / Ignorer (ses fenêtres ne sont jamais réduites ni déplacées).</summary>
    private Button StageRuleButton(bool ignored, Action<bool> pick)
    {
        string Text(bool ignore) => Loc.T(ignore ? "settings.rules.stage.ignore" : "settings.rules.stage.managed");
        var button = new Button
        {
            Style = (Style)FindResource("SettingsButton"), Content = Loc.F("settings.rules.stage_label", Text(ignored)) + "  ▾",
            VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("settings.rules.stage.tooltip"),
        };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var choice in new[] { false, true })
            {
                var item = new MenuItem { Header = Text(choice), IsCheckable = true, IsChecked = choice == ignored };
                item.Click += (_, _) =>
                {
                    pick(choice);
                    ShowAppRules();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    /// <summary>« Ajouter une appli » : les applis ouvertes (avec une fenêtre), ou un programme choisi sur le disque.</summary>
    private void AddAppRule_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddAppRuleButton, Placement = PlacementMode.Bottom };
        var existing = SettingsStore.Current.AppRules.Select(r => r.Process).ToHashSet();
        var open = OpenApps().Where(a => !existing.Contains(a.Process)).Take(15).ToList();
        if (open.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("settings.rules.none_open"), IsEnabled = false });
        foreach (var (process, title) in open)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => AddRule(process, title);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = Loc.T("settings.rules.browse") };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = Loc.T("settings.rules.programs") + " (*.exe)|*.exe" };
            if (dialog.ShowDialog(this) == true) AddRule(AppRule.Clean(dialog.FileName), FileVersionInfo.GetVersionInfo(dialog.FileName).ProductName ?? "");
        };
        menu.Items.Add(browse);
        menu.IsOpen = true;
    }

    private void AddRule(string process, string name)
    {
        SettingsStore.Update(s => s.AppRules.Add(new AppRule
        {
            Process = AppRule.Clean(process),
            Name = string.IsNullOrWhiteSpace(name) ? AppRule.Clean(process) : name.Trim(),
            Island = AppBehavior.Hide, // le cas le plus courant (navigateur) ; à changer d'un clic
        }));
        ShowAppRules();
    }

    /// <summary>Applis ouvertes avec une fenêtre visible (nom de produit), sans Opaline ni les processus de Windows.</summary>
    private static List<(string Process, string Title)> OpenApps()
    {
        var apps = new List<(string, string)>();
        var seen = new HashSet<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero || process.Id == Environment.ProcessId) continue;
                    var name = process.ProcessName.ToLowerInvariant();
                    if (name is "explorer" or "applicationframehost" or "systemsettings" or "textinputhost" || !seen.Add(name)) continue;
                    string title = name;
                    try
                    {
                        if (process.MainModule?.FileVersionInfo.ProductName is { Length: > 0 } product) title = product;
                    }
                    catch (Exception)
                    {
                        // Processus protégé : on garde son nom.
                    }
                    apps.Add((name, title));
                }
                catch (InvalidOperationException)
                {
                    // Terminé entre-temps.
                }
            }
        }
        return apps.OrderBy(a => a.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
