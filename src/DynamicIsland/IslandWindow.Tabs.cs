using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland;

/// <summary>
/// Priorité 2, point 12 : taille de l'île (tout le contenu agrandi / réduit, la fenêtre suit), onglets qu'on cache et qu'on
/// réordonne (Paramètres → Île), rangée qui défile quand il y en a trop (l'heure reste), ou onglets en colonne à gauche.
/// </summary>
public partial class IslandWindow
{
    private bool _friendsWanted;
    private string _tabOrder = "";
    private bool? _verticalShown;
    private double _scaleShown = double.NaN;

    /// <summary>Onglet ↔ identifiant enregistré dans les réglages.</summary>
    private static readonly (Tab Tab, string Id)[] TabIds =
    [
        (Tab.Music, IslandTabItem.Music), (Tab.Calendar, IslandTabItem.Calendar), (Tab.Control, IslandTabItem.Control),
        (Tab.Widgets, IslandTabItem.Widgets), (Tab.Shelf, IslandTabItem.Shelf), (Tab.Voice, IslandTabItem.Voice),
        (Tab.Claude, IslandTabItem.Claude), (Tab.Clocks, IslandTabItem.Clocks), (Tab.Health, IslandTabItem.Health),
        (Tab.Friends, IslandTabItem.Friends), (Tab.Mixer, IslandTabItem.Mixer), (Tab.Clipboard, IslandTabItem.Clipboard), (Tab.Notes, IslandTabItem.Notes),
        (Tab.Translate, IslandTabItem.Translate),
    ];

    private static string IdOf(Tab tab) => TabIds.First(t => t.Tab == tab).Id;

    /// <summary>L'onglet a quelque chose à montrer (Claude activé, des villes, des amis…).</summary>
    private bool TabHasContent(Tab tab) => tab switch
    {
        Tab.Claude => SettingsStore.Current.Claude.Enabled,
        Tab.Clocks => SettingsStore.Current.Island.WorldClocks.Count > 0,
        Tab.Health => SettingsStore.Current.Health.Tab,
        Tab.Friends => _friendsWanted,
        _ => true,
    };

    /// <summary>
    /// Visibilité et ordre des onglets : choix de l'utilisateur ET contenu disponible ET module en marche. L'onglet ouvert caché →
    /// le premier qui reste (plus aucun : l'île reste une pilule).
    /// </summary>
    private void RefreshTabs()
    {
        var items = SettingsStore.Current.Island.Tabs;
        var buttons = TabButtons().ToDictionary(t => t.Tab, t => t.Button);
        ClaudeTabButton.Content = Services.ClaudeService.AssistantName; // « ChatGPT » avec la clé OpenAI
        string order = string.Join(",", items.Select(i => i.Id));
        if (order != _tabOrder)
        {
            _tabOrder = order;
            int index = 0;
            foreach (var item in items)
            {
                var button = buttons[TabIds.First(t => t.Id == item.Id).Tab];
                if (TabStrip.Children.IndexOf(button) != index)
                {
                    TabStrip.Children.Remove(button);
                    TabStrip.Children.Insert(index, button);
                }
                index++;
            }
        }
        bool grid = GridLayout;
        foreach (var (tab, id) in TabIds)
        {
            // Mode grille (O6) : l'onglet n'est plus qu'un outil ; il reste ouvrable (palette, dock…) même si sa tuile est retirée.
            bool shown = (grid || items.First(i => i.Id == id).Visible) && TabHasContent(tab) && !TabStopped(tab);
            buttons[tab].Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
        if (grid)
        {
            RefreshGrid(); // tuiles : disposition enregistrée, sans celles qui n'ont rien à montrer
            if (!_home && buttons[_tab].Visibility != Visibility.Visible) GoHome(animate: _expanded); // outil arrêté : retour à la grille
            return;
        }
        if (buttons[_tab].Visibility == Visibility.Visible) return;
        var next = items.Select(i => TabIds.First(t => t.Id == i.Id).Tab).FirstOrDefault(t => buttons[t].Visibility == Visibility.Visible, _tab);
        if (next != _tab) SelectTab(next, animate: _expanded);
        else if (_expanded) Collapse(); // plus aucun onglet : l'île reste une pilule (heure, musique, minuteur)
    }

    /// <summary>Onglets en rangée (en haut, défilants) ou en colonne (à gauche, icônes seules, nom de l'onglet en titre).</summary>
    private void ApplyTabLayout()
    {
        bool vertical = VerticalTabs;
        if (_verticalShown == vertical) return;
        _verticalShown = vertical;
        if (vertical)
        {
            HeaderRow.Children.Remove(TabScroller);
            ExpandedLayout.Children.Add(TabScroller);
            Grid.SetColumn(TabScroller, 0);
            Grid.SetRowSpan(TabScroller, 2);
            TabScroller.VerticalAlignment = VerticalAlignment.Top;
            TabScroller.Margin = new Thickness(0, 0, Space.S4, 0);
            TabScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            TabStrip.Orientation = Orientation.Vertical;
        }
        else
        {
            ExpandedLayout.Children.Remove(TabScroller);
            // Premier appel (constructeur) : la rangée y est déjà (XAML) ; la réinsérer levait une exception et l'île n'était pas créée.
            if (!HeaderRow.Children.Contains(TabScroller)) HeaderRow.Children.Insert(0, TabScroller);
            Grid.SetColumn(TabScroller, 0);
            Grid.SetRowSpan(TabScroller, 1);
            TabScroller.VerticalAlignment = VerticalAlignment.Center;
            TabScroller.Margin = new Thickness(0);
            TabScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            TabScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabStrip.Orientation = Orientation.Horizontal;
        }
        var style = (Style)FindResource(vertical ? "TabButtonIcon" : "TabButton");
        foreach (var (button, _) in TabButtons())
        {
            button.Style = style;
            button.ToolTip = vertical ? button.Content : null;
        }
        UpdateTabTitle();
        if (_expanded) AnimateTo(ExpandedSize(_tab), ExpandedRadius, Springs.Resize);
    }

    /// <summary>En colonne : le nom de l'onglet ouvert s'affiche en haut, à côté de l'heure.</summary>
    private void UpdateTabTitle()
    {
        bool vertical = VerticalTabs;
        TabTitle.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        var button = TabButtons().First(t => t.Tab == _tab).Button;
        if (vertical) TabTitle.Text = button.Content as string ?? "";
        // Trop d'onglets : celui qu'on ouvre défile jusqu'à être visible en entier (après la mise en page : son nom vient d'apparaître).
        Dispatcher.BeginInvoke(() => button.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        // Encore une fois quand le ressort a fini d'élargir l'île : le dernier onglet (« Presse-papiers ») restait coupé.
        var settle = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (button.IsChecked == true) button.BringIntoView();
        };
        settle.Start();
    }

    /// <summary>Taille de l'île : le contenu est mis à l'échelle et la fenêtre (transparente) suit, puis l'île revient à sa place.</summary>
    private void ApplyScale()
    {
        double scale = SettingsStore.Current.Island.Scale;
        if (Math.Abs(scale - _scaleShown) < 0.001) return;
        _scaleShown = scale;
        WindowRoot.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        Width = IslandShape.Window.Width * scale;
        Height = IslandShape.Window.Height * scale;
        if (IsLoaded && SnapshotFolder == null && !_moving) PositionOnMonitor(_monitor);
    }

    /// <summary>Molette sur la rangée d'onglets : elle défile de côté (un ScrollViewer ne le fait pas seul).</summary>
    private void TabScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TabScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            // En colonne : défilement normal ; au bout (O3), la colonne cède un peu puis revient.
            if ((e.Delta > 0 && TabScroller.VerticalOffset <= 0) || (e.Delta < 0 && TabScroller.VerticalOffset >= TabScroller.ScrollableHeight - 0.5))
                TabStripOverscroll(e.Delta, vertical: true);
            return;
        }
        double before = TabScroller.HorizontalOffset;
        TabScroller.ScrollToHorizontalOffset(before - e.Delta / 2.0);
        if ((e.Delta > 0 && before <= 0) || (e.Delta < 0 && before >= TabScroller.ScrollableWidth - 0.5))
            TabStripOverscroll(e.Delta, vertical: false); // au-delà du premier / dernier onglet : rebond élastique (O3)
        e.Handled = true;
    }
}
