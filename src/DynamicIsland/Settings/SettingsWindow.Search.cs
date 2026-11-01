using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Correctif 10 : notre propre barre de titre (WindowChrome : déplacer, réduire, fermer ; le Mica reste). Correctif 11 : recherche
/// de n'importe quel réglage — chaque carte (titre, description, groupe, page) est lue dans les pages elles-mêmes, donc une
/// nouvelle carte est trouvable sans rien ajouter ; un résultat ouvre sa page, l'amène à l'écran et la fait clignoter.
/// </summary>
public partial class SettingsWindow
{
    /// <summary>Une carte de réglage trouvable.</summary>
    private sealed record SearchEntry(RadioButton Nav, FrameworkElement Card, string Title, string Description, string Group, string Haystack);

    private List<SearchEntry> _searchHits = [];
    private int _searchSelected;

    private (RadioButton Nav, FrameworkElement Page)[] PageEntries =>
    [
        (NavGeneral, GeneralPage), (NavAppearance, AppearancePage), (NavWallpaper, WallpaperPage), (NavIsland, IslandPage), (NavDock, DockPage),
        (NavWidgets, WidgetsPage), (NavProfiles, ProfilesPage), (NavSharing, SharingPage), (NavConnected, ConnectedPage), (NavFriends, FriendsPage), (NavAutomations, AutomationsPage),
        (NavWeek, WeekPage), (NavAbout, AboutPage),
    ];

    private void InitializeChrome()
    {
        StateChanged += (_, _) => RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0); // agrandie : rien sous les bords de l'écran
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        };
        SearchPopup.Closed += (_, _) => _searchHits = [];
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ recherche

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var query = Simplify(SearchBox.Text);
        if (query.Length < 2)
        {
            SearchPopup.IsOpen = false;
            return;
        }
        _searchHits = Search(query);
        _searchSelected = 0;
        ShowSearchResults();
        SearchPopup.IsOpen = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _searchHits.Count > 0:
                _searchSelected = (_searchSelected + 1) % _searchHits.Count;
                ShowSearchResults();
                break;
            case Key.Up when _searchHits.Count > 0:
                _searchSelected = (_searchSelected + _searchHits.Count - 1) % _searchHits.Count;
                ShowSearchResults();
                break;
            case Key.Enter when _searchHits.Count > 0:
                GoToSetting(_searchHits[_searchSelected]);
                break;
            case Key.Escape:
                SearchBox.Clear();
                SearchPopup.IsOpen = false;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Toutes les cartes dont le texte contient chaque mot tapé ; le titre compte plus que la description.</summary>
    private List<SearchEntry> Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return BuildIndex()
            .Where(entry => words.All(entry.Haystack.Contains))
            .Select(entry => (Entry: entry, Score: Score(entry, query, words)))
            .OrderByDescending(hit => hit.Score)
            .Take(SettingsShape.SearchResults)
            .Select(hit => hit.Entry)
            .ToList();
    }

    private static int Score(SearchEntry entry, string query, string[] words)
    {
        var title = Simplify(entry.Title);
        return (title.StartsWith(query, StringComparison.Ordinal) ? 100 : 0)
               + words.Count(w => title.Split(' ').Any(part => part.StartsWith(w, StringComparison.Ordinal))) * 20
               + words.Count(title.Contains) * 10;
    }

    /// <summary>Cartes de toutes les pages (arbre logique : les pages cachées comptent aussi), dans l'ordre.</summary>
    private List<SearchEntry> BuildIndex()
    {
        var cardStyle = (Style)FindResource("SettingCard");
        var titleStyle = (Style)FindResource("CardTitle");
        var descriptionStyle = (Style)FindResource("CardDescription");
        var groupStyle = (Style)FindResource("GroupTitle");
        var optionStyle = (Style)FindResource("OptionCard");
        var entries = new List<SearchEntry>();
        foreach (var (nav, page) in PageEntries)
        {
            string pageName = nav.Content as string ?? "";
            string group = "";
            void Walk(DependencyObject node)
            {
                if (node is TextBlock { Text.Length: > 0 } heading && ReferenceEquals(heading.Style, groupStyle)) group = heading.Text;
                if (node is RadioButton { Content: string option, Visibility: not Visibility.Collapsed } choice && ReferenceEquals(choice.Style, optionStyle))
                {
                    string detail = choice.Tag as string ?? "";
                    entries.Add(new SearchEntry(nav, choice, $"{group} : {option}".TrimStart(' ', ':'), detail, group, Simplify($"{option} {detail} {group} {pageName}")));
                    return;
                }
                if (node is FrameworkElement { Visibility: not Visibility.Collapsed } card && ReferenceEquals(card.Style, cardStyle))
                {
                    var texts = Descendants(card).OfType<TextBlock>().ToList();
                    string title = texts.FirstOrDefault(t => ReferenceEquals(t.Style, titleStyle) && t.Text.Length > 0)?.Text ?? "";
                    string description = texts.FirstOrDefault(t => ReferenceEquals(t.Style, descriptionStyle) && t.Text.Length > 0)?.Text ?? "";
                    if (title.Length > 0)
                        entries.Add(new SearchEntry(nav, card, title, description, group, Simplify($"{title} {description} {group} {pageName}")));
                    return; // une carte = un résultat
                }
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
            }
            Walk(page);
        }
        return entries;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    /// <summary>Minuscules sans accents ni ponctuation (« Fond d'écran » = « fond d ecran »).</summary>
    internal static string Simplify(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private void ShowSearchResults()
    {
        SearchResults.Children.Clear();
        if (_searchHits.Count == 0)
        {
            SearchResults.Children.Add(new TextBlock
            {
                Text = Loc.T("settings.search.none"),
                Foreground = Paint.TextSecondary,
                FontSize = TypeRamp.Body,
                Margin = new Thickness(Space.S5, Space.S4, Space.S5, Space.S4),
            });
            return;
        }
        for (int i = 0; i < _searchHits.Count; i++)
        {
            var hit = _searchHits[i];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = hit.Title, FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock
            {
                Text = hit.Group.Length > 0 ? $"{hit.Nav.Content} · {hit.Group}" : hit.Nav.Content as string,
                FontSize = TypeRamp.Caption,
                Foreground = Paint.TextSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var button = new Button { Style = (Style)FindResource("SearchResult"), Content = content, IsDefault = i == _searchSelected };
            button.Click += (_, _) => GoToSetting(hit);
            SearchResults.Children.Add(button);
        }
    }

    /// <summary>Ouvre la page du réglage, l'amène en haut et le fait clignoter à la couleur d'accent.</summary>
    private void GoToSetting(SearchEntry entry)
    {
        SearchPopup.IsOpen = false;
        entry.Nav.IsChecked = true;
        Dispatcher.BeginInvoke(() =>
        {
            Scroller.UpdateLayout();
            var top = entry.Card.TranslatePoint(new Point(0, 0), PagesHost).Y;
            Scroller.ScrollToVerticalOffset(Math.Max(0, top - SettingsShape.SearchScrollMargin));
            Highlight(entry.Card);
        }, DispatcherPriority.Loaded);
    }

    private static void Highlight(FrameworkElement card)
    {
        if (card is not Border border)
        {
            // Carte à choix (Fixe / Intelligent / Peek…) : elle clignote par son opacité.
            var blink = new DoubleAnimationUsingKeyFrames { Duration = Motion.Highlight };
            foreach (var (value, at) in new[] { (0.35, 0.15), (1.0, 0.4), (0.35, 0.55), (1.0, 0.8) })
                blink.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromPercent(at)));
            blink.Completed += (_, _) => card.BeginAnimation(OpacityProperty, null);
            card.BeginAnimation(OpacityProperty, blink);
            return;
        }
        var original = border.ReadLocalValue(Border.BackgroundProperty);
        var accent = AppearanceService.AccentStart;
        var brush = new SolidColorBrush(Color.FromArgb(0, accent.R, accent.G, accent.B));
        border.Background = brush;
        var pulse = new ColorAnimationUsingKeyFrames { Duration = Motion.Highlight };
        foreach (var (alpha, at) in new[] { (0x66, 0.15), (0x14, 0.4), (0x66, 0.55), (0x14, 0.8), (0x00, 1.0) })
            pulse.KeyFrames.Add(new EasingColorKeyFrame(Color.FromArgb((byte)alpha, accent.R, accent.G, accent.B), KeyTime.FromPercent(at)));
        pulse.Completed += (_, _) =>
        {
            if (original == DependencyProperty.UnsetValue) border.ClearValue(Border.BackgroundProperty);
            else border.SetValue(Border.BackgroundProperty, original);
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
    }
}
