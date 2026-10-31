using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Automations;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Automations;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Page « Automatisations » (7D) : règles « Quand… alors… » (activer, modifier, essayer, supprimer) et « Ne pas déranger ».</summary>
public partial class SettingsWindow
{
    private List<AutomationRule>? _sampleRules; // rendu --snapshot seulement

    private void LoadAutomations()
    {
        var rules = _sampleRules ?? SettingsStore.Current.Automations.Rules;
        AutomationRulesList.Children.Clear();
        if (rules.Count == 0)
            AutomationRulesList.Children.Add(new TextBlock { Text = Loc.T("settings.automations.empty"), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        foreach (var rule in rules) AutomationRulesList.Children.Add(RuleCard(rule));
        NewRuleButton.IsEnabled = !SettingsStore.IsReadOnly && rules.Count < AutomationSettings.MaxRules;
        ShowDoNotDisturb();
    }

    private SquircleBorder RuleCard(AutomationRule rule)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SquircleBorder
        {
            Width = Sizes.Button, Height = Sizes.Button, CornerRadius = Corners.S, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, Space.S6, 0),
            Background = Paint.Control,
            Child = new TextBlock { Text = AutomationText.TriggerGlyph(rule.Trigger.Kind), FontFamily = FontStack.Icons, FontSize = IconSize.M,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ((TextBlock)icon.Child).SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentBrushKey);

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = rule.Name.Length > 0 ? rule.Name : Loc.T("automations.untitled"), Style = (Style)FindResource("CardTitle") });
        var summary = Loc.F("settings.automations.summary", AutomationText.Describe(rule.Trigger),
            rule.Actions.Count == 0 ? Loc.T("settings.automations.nothing") : string.Join(" · ", rule.Actions.Select(AutomationText.Describe)));
        texts.Children.Add(new TextBlock { Text = summary, Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        if (AutomationText.NeedsChoice(rule.Trigger) || rule.Actions.Any(AutomationText.NeedsChoice))
            texts.Children.Add(new TextBlock { Text = Loc.T("settings.automations.incomplete"), Style = (Style)FindResource("CardDescription"), Foreground = Paint.Timer, // orange : « à compléter »
                                               TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Space.S2, 0, 0) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S4, 0, 0) };
        var edit = new Button { Style = (Style)FindResource("SettingsButton"), Content = Loc.T("settings.automations.edit"), Margin = new Thickness(0, 0, Space.S3, 0) };
        edit.Click += (_, _) => RuleEditorWindow.Open(this, rule, SaveRule);
        var test = new Button { Style = (Style)FindResource("SettingsButton"), Content = Loc.T("settings.automations.try"), Margin = new Thickness(0, 0, Space.S3, 0),
                                ToolTip = Loc.T("settings.automations.try.tip") };
        test.Click += (_, _) => AutomationService.Run(rule);
        var delete = new Button { Style = (Style)FindResource("SettingsButton"), Content = Loc.T("settings.automations.delete"), Margin = new Thickness(0) };
        delete.Click += (_, _) => DeleteRule(rule);
        buttons.Children.Add(edit);
        buttons.Children.Add(test);
        buttons.Children.Add(delete);
        texts.Children.Add(buttons);
        foreach (var button in new[] { edit, test, delete }) button.IsEnabled = !SettingsStore.IsReadOnly && _sampleRules == null;

        var toggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Top,
                                    Margin = new Thickness(Space.S6, Space.S1, 0, 0), IsEnabled = !SettingsStore.IsReadOnly && _sampleRules == null };
        toggle.Click += (_, _) =>
        {
            bool on = toggle.IsChecked == true;
            SettingsStore.Update(s =>
            {
                if (s.Automations.Rules.FirstOrDefault(r => r.Id == rule.Id) is { } stored) stored.Enabled = on;
            });
        };

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(icon);
        grid.Children.Add(texts);
        grid.Children.Add(toggle);
        return new SquircleBorder { Style = (Style)FindResource("SettingCard"), Child = grid };
    }

    private void NewRule_Click(object sender, RoutedEventArgs e) => RuleEditorWindow.Open(this, null, SaveRule);

    /// <summary>Règle enregistrée par l'éditeur : remplacée (même Id) ou ajoutée, puis la liste est redessinée.</summary>
    private void SaveRule(AutomationRule rule)
    {
        SettingsStore.Update(s =>
        {
            var rules = s.Automations.Rules;
            int index = rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0) rules[index] = rule;
            else if (rules.Count < AutomationSettings.MaxRules) rules.Add(rule);
        });
        LoadAutomations();
    }

    private void DeleteRule(AutomationRule rule)
    {
        if (MessageBox.Show(this, Loc.F("settings.automations.delete.confirm", rule.Name), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                            MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        SettingsStore.Update(s => s.Automations.Rules.RemoveAll(r => r.Id == rule.Id));
        LoadAutomations();
    }

    // ------------------------------------------------------------------ Ne pas déranger

    private void ShowDoNotDisturb()
    {
        var until = FocusService.Until;
        DoNotDisturbText.Text = !FocusService.DoNotDisturb ? Loc.T("settings.dnd.off")
            : until == DateTime.MaxValue ? Loc.T("settings.dnd.on")
            : Loc.F("settings.dnd.until", until!.Value.ToString("t", Loc.Culture));
        DoNotDisturbButton.Content = Loc.T(FocusService.DoNotDisturb ? "settings.dnd.stop" : "settings.dnd.start");
        DoNotDisturbButton.IsEnabled = !SettingsStore.IsReadOnly;
    }

    private void DoNotDisturb_Click(object sender, RoutedEventArgs e)
    {
        if (FocusService.DoNotDisturb) FocusService.Stop();
        else FocusService.TurnOn(TimeSpan.FromHours(1));
        ShowDoNotDisturb();
    }

    /// <summary>--snapshot : les deux exemples + une règle « à heure précise », sans rien écrire.</summary>
    internal void ShowAutomationsSample()
    {
        var bedtime = new AutomationRule
        {
            Name = Loc.T("settings.automations.sample.night"),
            Enabled = true,
            Trigger = new AutomationTrigger { Kind = TriggerKind.Time, Time = "23:00", Days = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday] },
            Actions = [new AutomationAction { Kind = ActionKind.DoNotDisturbOn, Minutes = 0 }, new AutomationAction { Kind = ActionKind.MuteMic }],
        };
        _sampleRules = [.. AutomationService.Examples(), bedtime];
        LoadAutomations();
    }
}
