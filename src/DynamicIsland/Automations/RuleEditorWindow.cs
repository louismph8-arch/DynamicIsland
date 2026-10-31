using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Automations;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Automations;

/// <summary>
/// Éditeur d'une règle « Quand… alors… » (7D) : nom, déclencheur (8 cartes + ses réglages), actions dans l'ordre (ajouter,
/// retirer, chacune avec son choix). Les choix sont des menus sombres d'Opaline (profils, sorties audio, durées…) ; un fichier
/// ou un dossier se choisit dans la fenêtre de Windows. « Enregistrer » range la règle (activée) ; rien n'est fait avant.
/// </summary>
public sealed class RuleEditorWindow : CardWindow
{
    private static readonly int[] TimerChoices = [5, 10, 15, 20, 25, 30, 45, 60, 90];
    private static readonly int[] DoNotDisturbChoices = [0, 30, 60, 120, 240];
    private static readonly int[] IdleChoices = [5, 10, 15, 30, 60, 120];

    private readonly AutomationRule _rule;
    private readonly Action<AutomationRule> _save;
    private readonly TextBox _name = new();
    private readonly List<RadioButton> _triggerCards = [];
    private readonly StackPanel _triggerOptions = new() { Margin = new Thickness(0, Space.S4, 0, 0) };
    private readonly StackPanel _actions = new();
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    /// <param name="rule">null = nouvelle règle.</param>
    /// <param name="save">Appelé avec la règle modifiée (une copie : rien ne change tant qu'on n'enregistre pas).</param>
    public static void Open(Window? owner, AutomationRule? rule, Action<AutomationRule> save)
    {
        var editor = new RuleEditorWindow(rule, save, snapshot: false);
        if (owner is { IsVisible: true }) editor.Owner = owner;
        editor.Show();
        editor.Activate();
    }

    private RuleEditorWindow(AutomationRule? rule, Action<AutomationRule> save, bool snapshot)
        : base(Loc.T(rule == null ? "automations.editor.new" : "automations.editor.edit"), CardShape.EditorWidth, CardShape.EditorHeight, snapshot)
    {
        // Copie : annuler ne change rien.
        _rule = rule == null ? new AutomationRule { Enabled = true }
                             : JsonSerializer.Deserialize<AutomationRule>(JsonSerializer.Serialize(rule))!;
        _save = save;
        Body.Children.Add(BuildBody(rule == null));
        BuildFooter();
        ShowTrigger();
        ShowActions();
    }

    // ------------------------------------------------------------------ mise en page

    private FrameworkElement BuildBody(bool isNew)
    {
        var page = new StackPanel { Margin = new Thickness(Space.S14, Space.S10, Space.S14, Space.S4) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Badge(Icons.Automations));
        var heading = Heading(Loc.T(isNew ? "automations.editor.new" : "automations.editor.edit"));
        heading.Margin = new Thickness(Space.S6, 0, 0, 0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(heading);
        page.Children.Add(title);

        _name.Style = (Style)FindResource("HexBox");
        _name.Width = double.NaN;
        _name.MaxLength = 60;
        _name.FontFamily = FontStack.Ui;
        _name.Text = _rule.Name;
        _name.TextChanged += (_, _) => _rule.Name = _name.Text;
        page.Children.Add(Label(Loc.T("automations.editor.name")));
        page.Children.Add(_name);

        page.Children.Add(Section(Loc.T("automations.editor.when")));
        var cards = new UniformGrid { Columns = 4 };
        foreach (var kind in Enum.GetValues<TriggerKind>())
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("OptionCard"),
                GroupName = "Trigger",
                Content = new TextBlock { Text = AutomationText.TriggerShortName(kind), TextWrapping = TextWrapping.Wrap },
                ToolTip = AutomationText.TriggerName(kind),
                Tag = "",
                IsChecked = _rule.Trigger.Kind == kind,
                Margin = new Thickness(0, 0, Space.S2, Space.S2),
            };
            card.Checked += (_, _) =>
            {
                _rule.Trigger.Kind = kind;
                ShowTrigger();
            };
            _triggerCards.Add(card);
            cards.Children.Add(card);
        }
        page.Children.Add(cards);
        page.Children.Add(_triggerOptions);

        page.Children.Add(Section(Loc.T("automations.editor.then")));
        page.Children.Add(_actions);
        var add = Picker(() => "+  " + Loc.T("automations.editor.add_action"),
            () => Enum.GetValues<ActionKind>().Select(kind => (AutomationText.ActionName(kind), (Action)(() => AddAction(kind)))));
        add.Margin = new Thickness(0, Space.S4, 0, 0);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(add);

        int index = 0;
        foreach (UIElement child in page.Children) Reveal(child, index++);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private void BuildFooter()
    {
        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = SecondaryButton(Loc.T("automations.editor.cancel"));
        cancel.Margin = new Thickness(0, 0, Space.S4, 0);
        cancel.Click += (_, _) => CloseAnimated();
        var save = PrimaryButton(Loc.T("automations.editor.save"));
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 1);
        _status.Margin = new Thickness(0, 0, Space.S6, 0);
        Footer.Children.Add(_status);
        Footer.Children.Add(buttons);
    }

    // ------------------------------------------------------------------ « Quand… »

    private void ShowTrigger()
    {
        var trigger = _rule.Trigger;
        _triggerOptions.Children.Clear();
        switch (trigger.Kind)
        {
            case TriggerKind.AppStarted:
            case TriggerKind.AppClosed:
                var app = new TextBox { Style = (Style)FindResource("HexBox"), Width = CardShape.FieldWidth, MaxLength = 80, FontFamily = FontStack.Ui, Text = trigger.App ?? "" };
                app.TextChanged += (_, _) =>
                {
                    trigger.App = app.Text.Trim().Length > 0 ? app.Text.Trim() : null;
                    trigger.AppName = null;
                };
                var running = Picker(() => Loc.T("automations.editor.app.running"), () => RunningApps().Select(a => (a.Name, (Action)(() =>
                {
                    app.Text = a.Process;
                    trigger.AppName = a.Name;
                }))));
                _triggerOptions.Children.Add(Label(Loc.T("automations.editor.app")));
                _triggerOptions.Children.Add(Row(app, running));
                _triggerOptions.Children.Add(Hint(Loc.T("automations.editor.app.hint")));
                break;
            case TriggerKind.Time:
                var time = new TextBox { Style = (Style)FindResource("HexBox"), Text = trigger.Time ?? "" , MaxLength = 5 };
                time.TextChanged += (_, _) => trigger.Time = TimeSpan.TryParseExact(time.Text.Trim(), @"hh\:mm", null, out _) ? time.Text.Trim() : null;
                var days = new WrapPanel { Margin = new Thickness(Space.S6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                foreach (var day in AutomationText.WeekOrder)
                {
                    var toggle = new ToggleButton
                    {
                        Style = (Style)FindResource("TileToggle"),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Content = Loc.Culture.DateTimeFormat.AbbreviatedDayNames[(int)day].TrimEnd('.'),
                        IsChecked = trigger.Days.Contains(day),
                        Margin = new Thickness(0, 0, Space.S2, 0),
                    };
                    toggle.Click += (_, _) =>
                    {
                        trigger.Days.Remove(day);
                        if (toggle.IsChecked == true) trigger.Days.Add(day);
                    };
                    days.Children.Add(toggle);
                }
                _triggerOptions.Children.Add(Label(Loc.T("automations.editor.time")));
                _triggerOptions.Children.Add(Row(time, days));
                _triggerOptions.Children.Add(Hint(Loc.T("automations.editor.time.hint")));
                break;
            case TriggerKind.DeviceConnected:
                var device = Picker(() => Loc.T($"automations.device.{trigger.Device.ToString().ToLowerInvariant()}"),
                    () => Enum.GetValues<DeviceFilter>().Select(d => (Loc.T($"automations.device.{d.ToString().ToLowerInvariant()}"), (Action)(() => trigger.Device = d))));
                var name = new TextBox { Style = (Style)FindResource("HexBox"), Width = CardShape.FieldWidth, MaxLength = 40, FontFamily = FontStack.Ui, Text = trigger.DeviceName ?? "" };
                name.TextChanged += (_, _) => trigger.DeviceName = name.Text.Trim().Length > 0 ? name.Text.Trim() : null;
                _triggerOptions.Children.Add(Label(Loc.T("automations.editor.device")));
                _triggerOptions.Children.Add(Row(device, name));
                _triggerOptions.Children.Add(Hint(Loc.T("automations.editor.device.hint")));
                break;
            case TriggerKind.Idle:
                _triggerOptions.Children.Add(Label(Loc.T("automations.editor.idle")));
                _triggerOptions.Children.Add(Picker(() => AutomationText.Duration(trigger.Minutes),
                    () => IdleChoices.Select(m => (AutomationText.Duration(m), (Action)(() => trigger.Minutes = m)))));
                break;
            default:
                _triggerOptions.Children.Add(Hint(Loc.T($"automations.editor.hint.{AutomationText.Key(trigger.Kind)}")));
                break;
        }
    }

    // ------------------------------------------------------------------ « … alors »

    private void AddAction(ActionKind kind)
    {
        if (_rule.Actions.Count >= AutomationRule.MaxActions) return;
        var action = new AutomationAction { Kind = kind };
        if (kind == ActionKind.Timer) action.Minutes = 25;
        _rule.Actions.Add(action);
        ShowActions();
    }

    private void ShowActions()
    {
        _actions.Children.Clear();
        if (_rule.Actions.Count == 0) _actions.Children.Add(Hint(Loc.T("automations.editor.no_action")));
        foreach (var action in _rule.Actions.ToList()) _actions.Children.Add(ActionRow(action));
    }

    private SquircleBorder ActionRow(AutomationAction action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = AutomationText.ActionGlyph(action.Kind), FontFamily = FontStack.Icons, FontSize = IconSize.M, VerticalAlignment = VerticalAlignment.Center,
                                   Margin = new Thickness(0, 0, Space.S5, 0) };
        icon.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentBrushKey);
        var name = new TextBlock { Text = AutomationText.ActionName(action.Kind), Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center,
                                   TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        if (Choice(action) is { } choice)
        {
            choice.Margin = new Thickness(Space.S4, 0, Space.S4, 0);
            Grid.SetColumn(choice, 2);
            grid.Children.Add(choice);
        }
        var remove = new Button { Style = (Style)FindResource("RoundButton"), Width = Sizes.Button, Height = Sizes.Button, ToolTip = Loc.T("automations.editor.remove"),
                                  Content = new TextBlock { Text = Icons.Close, FontFamily = FontStack.Icons, FontSize = IconSize.S, Foreground = Paint.TextSecondary } };
        remove.Click += (_, _) =>
        {
            _rule.Actions.Remove(action);
            ShowActions();
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);
        return new SquircleBorder { Style = (Style)FindResource("SettingCard"), Child = grid, Margin = new Thickness(0, 0, 0, Space.S3) };
    }

    /// <summary>Le choix propre à l'action (profil, sortie audio, durée, fichier…) ; null s'il n'y en a pas.</summary>
    private FrameworkElement? Choice(AutomationAction action)
    {
        string Choose() => Loc.T("automations.editor.choose");
        switch (action.Kind)
        {
            case ActionKind.Profile:
                return Picker(() => action.Label ?? Choose(), () => ProfileService.All.Select(p => (p.Name, (Action)(() =>
                {
                    action.Value = p.Id;
                    action.Label = p.Name;
                }))));
            case ActionKind.AudioOutput:
                return Picker(() => action.Label ?? Choose(), () =>
                {
                    var outputs = AudioOutputService.List();
                    return outputs.Count == 0
                        ? [(Loc.T("automations.editor.no_output"), (Action)(() => { }))]
                        : outputs.Select(o => (o.Name, (Action)(() =>
                        {
                            action.Value = o.Id;
                            action.Label = o.Name;
                        })));
                });
            case ActionKind.LaunchApp:
                return Picker(() => action.Label ?? Choose(), () => PinnedApps().Select(p => (p.Name, (Action)(() =>
                {
                    action.Value = p.Value;
                    action.Label = p.Name;
                }))).Append((Loc.T("automations.editor.other_app"), () => PickFile(action, Loc.T("automations.editor.app_filter"), "*.exe;*.lnk;*.url"))));
            case ActionKind.OpenFolder:
                return BrowseButton(() => action.Value is { } folder ? Path.GetFileName(folder.TrimEnd('\\')) : Choose(), () =>
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = AutomationText.ActionName(action.Kind) };
                    if (dialog.ShowDialog(this) == true) action.Value = dialog.FolderName;
                });
            case ActionKind.Wallpaper:
                return BrowseButton(() => action.Value is { } image ? Path.GetFileName(image) : Choose(),
                    () => PickFile(action, Loc.T("automations.editor.image_filter"), "*.jpg;*.jpeg;*.png;*.bmp"));
            case ActionKind.Timer:
                return Picker(() => AutomationText.Duration(action.Minutes), () => TimerChoices.Select(m => (AutomationText.Duration(m), (Action)(() => action.Minutes = m))));
            case ActionKind.DoNotDisturbOn:
                return Picker(() => action.Minutes > 0 ? AutomationText.Duration(action.Minutes) : Loc.T("automations.editor.until_off"),
                    () => DoNotDisturbChoices.Select(m => (m > 0 ? AutomationText.Duration(m) : Loc.T("automations.editor.until_off"), (Action)(() => action.Minutes = m))));
            case ActionKind.Notice:
                var text = new TextBox { Style = (Style)FindResource("HexBox"), Width = CardShape.FieldWidth, MaxLength = 80, FontFamily = FontStack.Ui, Text = action.Value ?? "" };
                text.TextChanged += (_, _) => action.Value = text.Text.Trim().Length > 0 ? text.Text.Trim() : null;
                return text;
            default:
                return null;
        }
    }

    private void PickFile(AutomationAction action, string description, string patterns)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"{description}|{patterns}", Title = AutomationText.ActionName(action.Kind) };
        if (dialog.ShowDialog(this) != true) return;
        action.Value = dialog.FileName;
        action.Label = Path.GetFileNameWithoutExtension(dialog.FileName);
        ShowActions();
    }

    // ------------------------------------------------------------------ enregistrer

    private void Save()
    {
        _rule.Name = _name.Text.Trim();
        if (_rule.Name.Length == 0) _rule.Name = AutomationText.Describe(_rule.Trigger);
        if (AutomationText.NeedsChoice(_rule.Trigger))
        {
            _status.Text = Loc.T("automations.editor.missing_trigger");
            return;
        }
        if (_rule.Actions.Count == 0)
        {
            _status.Text = Loc.T("automations.editor.missing_action");
            return;
        }
        _rule.Normalize();
        _save(_rule);
        CloseAnimated();
    }

    // ------------------------------------------------------------------ petits éléments

    /// <summary>Bouton qui ouvre un menu de choix (menus sombres d'Opaline) ; son texte suit le choix.</summary>
    private Button Picker(Func<string> label, Func<IEnumerable<(string Text, Action Pick)>> choices)
    {
        var text = new TextBlock { Text = label(), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CardShape.PickerMaxWidth };
        var button = SecondaryButton("");
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { text, new TextBlock { Text = "  ▾", Foreground = Paint.TextSecondary } },
        };
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var (choice, pick) in choices())
            {
                var item = new MenuItem { Header = choice };
                item.Click += (_, _) =>
                {
                    pick();
                    text.Text = label();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private Button BrowseButton(Func<string> label, Action browse)
    {
        var button = SecondaryButton(label());
        button.Click += (_, _) =>
        {
            browse();
            button.Content = label();
        };
        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && row.Children.Count > 0 && element.Margin == default) element.Margin = new Thickness(Space.S4, 0, 0, 0);
            row.Children.Add(child);
        }
        return row;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = TypeRamp.Headline,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, Space.S10, 0, Space.S4),
    };

    private TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("CardTitle"),
        Margin = new Thickness(0, Space.S5, 0, Space.S3),
    };

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("CardDescription"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, Space.S3, 0, 0),
    };

    private static SquircleBorder Badge(string glyph)
    {
        var badge = new SquircleBorder
        {
            Width = WelcomeShape.TipIcon, Height = WelcomeShape.TipIcon, CornerRadius = Corners.Pill, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.L, Foreground = Paint.White,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        badge.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        return badge;
    }

    /// <summary>Applis ouvertes (avec une fenêtre) : nom affiché + nom du processus, sans doublon, 15 au plus.</summary>
    private static List<(string Name, string Process)> RunningApps()
    {
        var apps = new List<(string, string)>();
        foreach (var window in WindowTracker.Snapshot())
        {
            if (window.ExePath is not { } exe) continue;
            var process = Path.GetFileNameWithoutExtension(exe);
            if (apps.Any(a => string.Equals(a.Item2, process, StringComparison.OrdinalIgnoreCase))) continue;
            string name;
            try
            {
                name = FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } description ? description : process;
            }
            catch (FileNotFoundException)
            {
                name = process;
            }
            apps.Add((name, process));
            if (apps.Count >= 15) break;
        }
        return apps;
    }

    /// <summary>Applis épinglées au dock (profil actif), dossiers compris : nom + chemin ou AppUserModelID.</summary>
    private static IEnumerable<(string Name, string Value)> PinnedApps()
    {
        foreach (var pin in SettingsStore.Current.Dock.Pinned.SelectMany(p => p.Items ?? [p]))
            if ((pin.AppId ?? pin.Path) is { } value) yield return (pin.Name, value);
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : l'exemple « Mode jeu » ouvert dans l'éditeur.</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var example = AutomationService.Examples()[0];
        example.Actions.Add(new AutomationAction { Kind = ActionKind.Timer, Minutes = 25 });
        var window = new RuleEditorWindow(example, _ => { }, snapshot: true);
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "16-automatisation.png"));
        window.Close();
    }
}
