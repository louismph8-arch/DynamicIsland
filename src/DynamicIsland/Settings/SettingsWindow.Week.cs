using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Stats;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Page « Ma semaine » (7F) : une semaine (lundi → dimanche) à la fois — totaux comparés à la semaine d'avant, temps d'écran
/// par jour en barres (part « jeux » empilée), Pomodoros en pastilles, applis et jeux les plus utilisés. Tout est lu dans
/// l'historique LOCAL (<see cref="UsageStats"/>) ; le suivi est désactivé tant que l'utilisateur ne l'active pas.
/// </summary>
public partial class SettingsWindow
{
    private DateOnly _weekShown = UsageLog.WeekStart(DateOnly.FromDateTime(DateTime.Now));
    private bool _weekWatched, _weekSample;
    private TextBlock? _weekAppsTitle; // rendu --snapshot de la suite de la page

    private static DateOnly WeekToday => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Page ouverte : redessinée à chaque relevé (une fois par minute au plus) ; fermée : plus rien.</summary>
    private void WatchWeek(bool on)
    {
        if (on != _weekWatched)
        {
            _weekWatched = on;
            if (on) UsageStats.Changed += OnWeekChanged;
            else UsageStats.Changed -= OnWeekChanged;
        }
        if (on) LoadWeek();
    }

    private void OnWeekChanged()
    {
        if (WeekPage.Visibility == Visibility.Visible) LoadWeek();
    }

    private void LoadWeek()
    {
        var settings = SettingsStore.Current.Stats;
        bool tracking = _weekSample || settings.Enabled;
        bool editable = !SettingsStore.IsReadOnly && !_weekSample;
        WeekTrackToggle.IsChecked = tracking;
        WeekTrackToggle.IsEnabled = editable && !ModuleGuard.IsDisabled(ModuleGuard.Stats);
        WeekSummaryToggle.IsChecked = settings.WeeklySummary;
        WeekSummaryToggle.IsEnabled = editable && tracking;
        var log = UsageStats.Log;
        WeekClearButton.IsEnabled = editable && log.HasData;

        WeekContent.Children.Clear();
        _weekAppsTitle = null;
        if (!tracking && !log.HasData)
        {
            WeekContent.Children.Add(WeekOffCard());
            return;
        }
        var current = UsageLog.WeekStart(WeekToday);
        if (_weekShown > current) _weekShown = current;
        var week = log.Week(_weekShown);
        // Semaine en cours : comparée aux MÊMES jours de la semaine d'avant (lundi → aujourd'hui), pas à 7 jours entiers.
        int elapsed = _weekShown == current ? WeekToday.DayNumber - current.DayNumber + 1 : 7;
        var previous = log.Week(_weekShown.AddDays(-7), elapsed);

        WeekContent.Children.Add(WeekNavigation(week, current));
        WeekContent.Children.Add(WeekTotals(week, previous));
        WeekContent.Children.Add(WeekScreenChart(week));
        WeekContent.Children.Add(WeekPomodoros(week));
        _weekAppsTitle = WeekGroupTitle(Loc.T("settings.week.apps"));
        WeekContent.Children.Add(_weekAppsTitle);
        WeekContent.Children.Add(WeekTopList(week.TopApps, accent: true, Loc.T("settings.week.apps.empty")));
        WeekContent.Children.Add(WeekGroupTitle(Loc.T("settings.week.games")));
        WeekContent.Children.Add(WeekTopList(week.TopGames, accent: false, Loc.T("settings.week.games.empty")));
    }

    // ------------------------------------------------------------------ morceaux de la page

    /// <summary>Suivi coupé et rien d'enregistré : explication + bouton clair pour l'activer.</summary>
    private SquircleBorder WeekOffCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Loc.T("settings.week.off.title"), Style = (Style)FindResource("CardTitle") });
        panel.Children.Add(new TextBlock { Text = Loc.T("settings.week.off.desc"), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        var start = new Button { Style = (Style)FindResource("SettingsButton"), Content = Loc.T("settings.week.off.button"), HorizontalAlignment = HorizontalAlignment.Left,
                                 Margin = new Thickness(0, Space.S5, 0, 0), IsEnabled = !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Stats) };
        start.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        start.Click += (_, _) => WeekSetTracking(true);
        panel.Children.Add(start);
        return WeekCard(panel);
    }

    /// <summary>« ‹  27 oct. – 2 nov. 2026 · cette semaine  › ».</summary>
    private Grid WeekNavigation(WeekSummary week, DateOnly current)
    {
        var grid = new Grid { Margin = new Thickness(0, Space.S8, 0, Space.S2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var oldest = UsageLog.WeekStart(WeekToday.AddDays(-(UsageLog.KeepDays - 1)));
        var back = WeekNavArrow(Icons.ChevronLeft, Loc.T("settings.week.previous"), week.Start > oldest, -7);
        var next = WeekNavArrow(Icons.ChevronRight, Loc.T("settings.week.next"), week.Start < current, 7);

        var end = week.Start.AddDays(6);
        string range = $"{week.Start.ToString("d MMM", Loc.Culture)} – {end.ToString("d MMM yyyy", Loc.Culture)}";
        string label = week.Start == current ? Loc.T("settings.week.this") : week.Start == current.AddDays(-7) ? Loc.T("settings.week.last") : "";
        var title = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = TypeRamp.Title3,
                                    FontFamily = FontStack.Display, FontWeight = FontWeights.SemiBold, Foreground = Paint.TextPrimary };
        System.Windows.Documents.Typography.SetNumeralAlignment(title, FontNumeralAlignment.Tabular); // dates : chiffres à largeur fixe (O4)
        title.Inlines.Add(new System.Windows.Documents.Run(range));
        if (label.Length > 0)
            title.Inlines.Add(new System.Windows.Documents.Run("  ·  " + label) { Foreground = Paint.TextSecondary, FontWeight = FontWeights.Normal, FontSize = TypeRamp.Body });
        Grid.SetColumn(title, 1);
        Grid.SetColumn(next, 2);
        grid.Children.Add(back);
        grid.Children.Add(title);
        grid.Children.Add(next);
        return grid;
    }

    private Button WeekNavArrow(string glyph, string tip, bool enabled, int days)
    {
        var button = new Button { Style = (Style)FindResource("SettingsButton"), Content = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.L,
                                  Width = Sizes.Button, Padding = new Thickness(0), ToolTip = tip, IsEnabled = enabled };
        button.Click += (_, _) =>
        {
            _weekShown = _weekShown.AddDays(days);
            LoadWeek();
        };
        return button;
    }

    /// <summary>Trois tuiles : temps d'écran, temps de jeu, Pomodoros, chacune avec l'évolution depuis la semaine d'avant.</summary>
    private UniformGrid WeekTotals(WeekSummary week, WeekSummary previous)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-Space.S2, 0, -Space.S2, 0) };
        grid.Children.Add(WeekTile(Icons.Monitor, Loc.T("settings.week.screen"), UsageStats.Format(week.ScreenSeconds), UsageLog.Change(week.ScreenSeconds, previous.ScreenSeconds)));
        grid.Children.Add(WeekTile(Icons.Gamepad, Loc.T("settings.week.gaming"), UsageStats.Format(week.GameSeconds), UsageLog.Change(week.GameSeconds, previous.GameSeconds)));
        grid.Children.Add(WeekTile(Icons.Timer, Loc.T("settings.week.pomodoros"), week.Pomodoros.ToString(Loc.Culture), UsageLog.Change(week.Pomodoros, previous.Pomodoros)));
        return grid;
    }

    private SquircleBorder WeekTile(string glyph, string name, string value, double? change)
    {
        var panel = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.M, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Space.S3, 0) };
        icon.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentBrushKey);
        head.Children.Add(icon);
        head.Children.Add(new TextBlock { Text = name, Style = (Style)FindResource("CardDescription"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);
        panel.Children.Add(Numerals.Apply(new TextBlock { Text = value, Foreground = Paint.TextPrimary, Margin = new Thickness(0, Space.S2, 0, 0) },
                                          TypeRamp.LargeTitle)); // grands chiffres (O4)
        panel.Children.Add(new TextBlock { Text = WeekChangeText(change), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        var card = WeekCard(panel);
        card.Margin = new Thickness(Space.S2, Space.S2, Space.S2, Space.S2);
        return card;
    }

    /// <summary>« +12 % par rapport à la semaine d'avant », « −8 % … », « Rien à comparer ».</summary>
    internal static string WeekChangeText(double? change)
    {
        if (change is not { } value) return Loc.T("settings.week.change.none");
        long rounded = (long)Math.Round(value);
        string text = rounded > 0 ? "+" + rounded.ToString(Loc.Culture) : rounded < 0 ? "−" + (-rounded).ToString(Loc.Culture) : "0";
        return Loc.F("settings.week.change", text);
    }

    /// <summary>Temps d'écran par jour : barre fine, part « jeux » empilée en haut (2 DIP d'écart), bout arrondi, survol = détail du jour.</summary>
    private SquircleBorder WeekScreenChart(WeekSummary week)
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = Loc.T("settings.week.chart"), Style = (Style)FindResource("CardTitle") });
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        legend.Children.Add(WeekLegendItem(null, Loc.T("settings.week.legend.apps")));
        legend.Children.Add(WeekLegendItem(WeekShape.Games, Loc.T("settings.week.legend.games")));
        head.Children.Add(legend);
        panel.Children.Add(head);

        int maxSeconds = Math.Max(3600, week.Days.Max(d => d.ScreenSeconds));
        int scaleHours = (int)Math.Ceiling(maxSeconds / 3600.0);
        double scale = WeekShape.ChartHeight / (scaleHours * 3600.0);
        int busiest = week.Days.Max(d => d.ScreenSeconds);

        var chart = new Grid { Margin = new Thickness(0, Space.S6, 0, 0) };
        chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(WeekShape.ChartHeight + TypeRamp.Caption2 * 2) });
        chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // Repères discrets : ligne du haut (échelle) et ligne de base.
        var top = new Border { Height = Stroke.Hairline, Background = Paint.Divider, VerticalAlignment = VerticalAlignment.Bottom,
                               Margin = new Thickness(0, 0, 0, WeekShape.ChartHeight) };
        var baseline = new Border { Height = Stroke.Hairline, Background = Paint.Track, VerticalAlignment = VerticalAlignment.Bottom };
        var scaleLabel = new TextBlock { Text = Loc.F("week.duration.h", scaleHours), FontSize = TypeRamp.Caption2, Foreground = Paint.TextTertiary,
                                         HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                                         Margin = new Thickness(0, 0, 0, WeekShape.ChartHeight + Space.S1) };
        chart.Children.Add(top);
        chart.Children.Add(baseline);
        chart.Children.Add(scaleLabel);

        var columns = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, Space.S16, 0) };
        var columnsLabels = new UniformGrid { Columns = 7, Margin = new Thickness(0, Space.S3, Space.S16, 0) };
        foreach (var day in week.Days)
        {
            bool future = day.Date > WeekToday;
            int other = Math.Max(0, day.ScreenSeconds - day.GameSeconds);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Width = WeekShape.BarWidth };
            if (busiest > 0 && day.ScreenSeconds == busiest) // une seule valeur écrite : le jour le plus chargé
                stack.Children.Add(new TextBlock { Text = UsageStats.Format(day.ScreenSeconds), FontSize = TypeRamp.Caption2, Foreground = Paint.TextSecondary,
                                                   HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
                                                   Margin = new Thickness(-Space.S10, 0, -Space.S10, Space.S2), TextAlignment = TextAlignment.Center });
            if (day.GameSeconds > 0)
                stack.Children.Add(WeekSegment(WeekShape.Games, Math.Max(WeekShape.MinSegment, day.GameSeconds * scale), roundTop: true));
            if (day.GameSeconds > 0 && other > 0) stack.Children.Add(new Border { Height = WeekShape.SegmentGap });
            if (other > 0)
                stack.Children.Add(WeekSegment(null, Math.Max(WeekShape.MinSegment, other * scale), roundTop: day.GameSeconds == 0));
            var hit = new Border { Background = Brushes.Transparent, Child = stack, ToolTip = WeekDayTip(day) }; // toute la colonne réagit au survol
            columns.Children.Add(hit);

            bool today = day.Date == WeekToday;
            columnsLabels.Children.Add(new TextBlock
            {
                Text = Loc.Culture.DateTimeFormat.GetAbbreviatedDayName(day.Date.DayOfWeek),
                FontSize = TypeRamp.Caption, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = today ? Paint.TextPrimary : future ? Paint.TextFaint : Paint.TextSecondary,
            });
        }
        chart.Children.Add(columns);
        Grid.SetRow(columnsLabels, 1);
        chart.Children.Add(columnsLabels);
        panel.Children.Add(chart);
        return WeekCard(panel);
    }

    private static FrameworkElement WeekLegendItem(Brush? fill, string text)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Space.S6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var dot = new SquircleBorder { Width = WeekShape.LegendDot, Height = WeekShape.LegendDot, CornerRadius = Corners.Pill, VerticalAlignment = VerticalAlignment.Center,
                                       Margin = new Thickness(0, 0, Space.S3, 0) };
        if (fill != null) dot.Background = fill;
        else dot.SetResourceReference(BackgroundProperty, AppearanceService.AccentBrushKey);
        item.Children.Add(dot);
        item.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        return item;
    }

    /// <summary>Morceau de barre ; <paramref name="fill"/> null = couleur d'accent d'Opaline. Le bas reste carré (ancré à la base).</summary>
    private static SquircleBorder WeekSegment(Brush? fill, double height, bool roundTop)
    {
        var segment = new SquircleBorder { Height = height, CornerRadius = roundTop ? WeekShape.BarTop : Corners.None };
        if (fill != null) segment.Background = fill;
        else segment.SetResourceReference(BackgroundProperty, AppearanceService.AccentBrushKey);
        return segment;
    }

    /// <summary>« samedi 31 octobre · 3 h 10 d'écran, dont 1 h de jeu · 2 Pomodoros ».</summary>
    private static string WeekDayTip(DaySummary day)
    {
        string date = day.Date.ToString("dddd d MMMM", Loc.Culture);
        if (day.ScreenSeconds == 0 && day.Pomodoros == 0) return $"{date}\n{Loc.T("settings.week.tip.none")}";
        var parts = new List<string> { Loc.F("settings.week.tip.screen", UsageStats.Format(day.ScreenSeconds)) };
        if (day.GameSeconds > 0) parts.Add(Loc.F("settings.week.tip.games", UsageStats.Format(day.GameSeconds)));
        if (day.Pomodoros > 0) parts.Add(Loc.F("settings.week.tip.pomodoros", day.Pomodoros));
        return $"{date}\n{string.Join(" · ", parts)}";
    }

    /// <summary>Une pastille par Pomodoro terminé, jour par jour.</summary>
    private SquircleBorder WeekPomodoros(WeekSummary week)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Loc.T("settings.week.pomodoros.title"), Style = (Style)FindResource("CardTitle") });
        panel.Children.Add(new TextBlock { Text = Loc.T("settings.week.pomodoros.desc"), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        var days = new UniformGrid { Columns = 7, Margin = new Thickness(0, Space.S5, Space.S16, 0) };
        foreach (var day in week.Days)
        {
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var dots = new WrapPanel { MaxWidth = (WeekShape.PomodoroDot + Space.S2) * 3, HorizontalAlignment = HorizontalAlignment.Center,
                                       VerticalAlignment = VerticalAlignment.Bottom };
            int shown = Math.Min(day.Pomodoros, WeekShape.PomodoroDotsShown);
            for (int i = 0; i < shown; i++)
            {
                var dot = new SquircleBorder { Width = WeekShape.PomodoroDot, Height = WeekShape.PomodoroDot, CornerRadius = Corners.Pill, Margin = new Thickness(Space.S1) };
                dot.SetResourceReference(BackgroundProperty, AppearanceService.AccentBrushKey);
                dots.Children.Add(dot);
            }
            if (day.Pomodoros > shown)
                dots.Children.Add(new TextBlock { Text = "+" + (day.Pomodoros - shown).ToString(Loc.Culture), FontSize = TypeRamp.Caption2, Foreground = Paint.TextSecondary,
                                                  VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S1, 0, 0, 0) });
            if (day.Pomodoros == 0)
                dots.Children.Add(new SquircleBorder { Width = WeekShape.PomodoroDot, Height = WeekShape.PomodoroDot, CornerRadius = Corners.Pill, Margin = new Thickness(Space.S1),
                                                       BorderBrush = Paint.Track, BorderThickness = new Thickness(Stroke.Hairline) });
            column.Children.Add(new Grid { Height = (WeekShape.PomodoroDot + Space.S2) * 2, Children = { dots } }); // pastilles posées sur une même ligne
            column.Children.Add(new TextBlock { Text = Loc.Culture.DateTimeFormat.GetAbbreviatedDayName(day.Date.DayOfWeek), FontSize = TypeRamp.Caption,
                                                Foreground = day.Date == WeekToday ? Paint.TextPrimary : Paint.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center,
                                                Margin = new Thickness(0, Space.S2, 0, 0) });
            column.ToolTip = Loc.F("settings.week.tip.pomodoros", day.Pomodoros);
            days.Children.Add(column);
        }
        panel.Children.Add(days);
        return WeekCard(panel);
    }

    /// <summary>Applis (accent) ou jeux (sarcelle) les plus utilisés : nom, barre proportionnelle au premier, durée.</summary>
    private SquircleBorder WeekTopList(IReadOnlyList<KeyValuePair<string, int>> items, bool accent, string empty)
    {
        var panel = new StackPanel();
        if (items.Count == 0)
            panel.Children.Add(new TextBlock { Text = empty, Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        int first = items.Count > 0 ? Math.Max(1, items[0].Value) : 1;
        for (int i = 0; i < items.Count; i++)
        {
            var (name, seconds) = items[i];
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : Space.S4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WeekShape.TopNameWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WeekShape.TopValueWidth) });
            row.Children.Add(new TextBlock { Text = name, FontSize = TypeRamp.Body, Foreground = Paint.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis,
                                             VerticalAlignment = VerticalAlignment.Center, ToolTip = name, Margin = new Thickness(0, 0, Space.S5, 0) });
            var track = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = WeekShape.TopBar };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(seconds, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, first - seconds), GridUnitType.Star) });
            var bar = new SquircleBorder { CornerRadius = Corners.Pill };
            if (accent) bar.SetResourceReference(BackgroundProperty, AppearanceService.AccentBrushKey);
            else bar.Background = WeekShape.Games;
            track.Children.Add(bar);
            Grid.SetColumn(track, 1);
            var value = new TextBlock { Text = UsageStats.Format(seconds), FontSize = TypeRamp.Callout, Foreground = Paint.TextSecondary,
                                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 2);
            row.Children.Add(track);
            row.Children.Add(value);
            panel.Children.Add(row);
        }
        return WeekCard(panel);
    }

    private SquircleBorder WeekCard(UIElement child) => new() { Style = (Style)FindResource("SettingCard"), Child = child };

    private TextBlock WeekGroupTitle(string text) => new() { Style = (Style)FindResource("GroupTitle"), Text = text };

    // ------------------------------------------------------------------ réglages

    private void WeekTrack_Click(object sender, RoutedEventArgs e) => WeekSetTracking(WeekTrackToggle.IsChecked == true);

    private void WeekSetTracking(bool on)
    {
        SettingsStore.Update(s => s.Stats.Enabled = on);
        LoadWeek();
    }

    private void WeekSummary_Click(object sender, RoutedEventArgs e)
    {
        bool on = WeekSummaryToggle.IsChecked == true;
        SettingsStore.Update(s => s.Stats.WeeklySummary = on);
    }

    private void WeekClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("settings.week.clear.confirm"), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                            MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        UsageStats.Clear();
        LoadWeek();
    }

    /// <summary>--snapshot : deux semaines inventées (en mémoire, rien d'écrit), semaine en cours affichée.</summary>
    internal void ShowWeekSample()
    {
        UsageStats.UseSample(WeekToday);
        _weekSample = true;
        _weekShown = UsageLog.WeekStart(WeekToday);
        LoadWeek();
    }

    internal FrameworkElement? WeekAppsTitle => _weekAppsTitle;
}
