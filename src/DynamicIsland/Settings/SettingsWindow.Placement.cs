using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Dock;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>Paramètres : place de l'île et du dock (5I). Le plus simple reste de les faire glisser ; ici on voit où ils sont.</summary>
public partial class SettingsWindow
{
    private bool _placementLoading;
    /// <summary>Choix d'écran du dock affichés (refaits seulement si les écrans changent).</summary>
    private readonly List<(RadioButton Option, IntPtr Monitor)> _dockScreenOptions = [];
    private string? _dockScreensShown;

    /// <summary>Remplit les cartes « Position » (aussi quand on déplace l'île ou le dock, fenêtre ouverte).</summary>
    private void ShowPlacement()
    {
        _placementLoading = true;
        var settings = SettingsStore.Current;
        var monitors = ScreenInfo.All();

        var place = settings.Island.Placement;
        IslandPlaceText.Text = Loc.F("placement.island.help", Describe(place, monitors));
        ResetIslandPlaceButton.IsEnabled = !place.IsDefault;
        IslandLockToggle.IsChecked = settings.Island.LockPosition;

        // Un choix d'écran par écran branché (rien s'il n'y en a qu'un).
        string shown = string.Join("|", monitors.Select(m => $"{m.Handle}:{m.Label}"));
        if (shown != _dockScreensShown)
        {
            _dockScreensShown = shown;
            _dockScreenOptions.Clear();
            DockScreenList.Children.Clear();
            foreach (var monitor in monitors.Count > 1 ? monitors : new List<ScreenInfo.Monitor>())
            {
                var option = new RadioButton
                {
                    Style = (Style)FindResource("OptionCard"),
                    GroupName = "DockScreen",
                    Content = monitor.Label,
                    Tag = Loc.T(monitor.IsPrimary ? "placement.screen.primary" : "placement.screen.other"),
                };
                var name = ScreenInfo.SettingName(monitor);
                option.Checked += (_, _) =>
                {
                    if (_placementLoading || SettingsStore.Current.Dock.Screen == name) return;
                    SettingsStore.Update(s =>
                    {
                        s.Dock.Screen = name;
                        s.Dock.Offset = 0;
                    });
                };
                _dockScreenOptions.Add((option, monitor.Handle));
                DockScreenList.Children.Add(option);
            }
        }
        var dockScreen = ScreenInfo.Find(settings.Dock.Screen).Handle;
        foreach (var (option, monitor) in _dockScreenOptions) option.IsChecked = monitor == dockScreen;
        (settings.Dock.Edge == DockEdge.Top ? DockTopEdge : DockBottomEdge).IsChecked = true;
        DockLockToggle.IsChecked = settings.Dock.LockPosition;
        _placementLoading = false;
    }

    /// <summary>« en haut au centre — Écran 2 (principal) ».</summary>
    private static string Describe(IslandPlacement place, List<ScreenInfo.Monitor> monitors)
    {
        string vertical = Loc.T(place.Vertical == VerticalAnchor.Bottom ? "placement.bottom" : "placement.top");
        string horizontal = place.Horizontal switch
        {
            HorizontalAnchor.Left => Loc.T("placement.left"),
            HorizontalAnchor.Right => Loc.T("placement.right"),
            _ when place.X == 0 => Loc.T("placement.center"),
            _ => Loc.T(place.X < 0 ? "placement.left_of_center" : "placement.right_of_center"),
        };
        var screen = ScreenInfo.Find(place.Screen);
        string where = monitors.Count > 1 ? $" — {screen.Label}" : "";
        return Loc.F("placement.where", vertical, horizontal) + where;
    }

    private void ResetIslandPlace_Click(object sender, RoutedEventArgs e) => IslandWindow.ResetPlacement();

    private void Welcome_Click(object sender, RoutedEventArgs e) => Welcome.WelcomeWindow.Open();

    private void IslandLock_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Island.LockPosition = IslandLockToggle.IsChecked == true);

    private void DockLock_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Dock.LockPosition = DockLockToggle.IsChecked == true);

    private void DockEdge_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _placementLoading) return;
        var edge = DockTopEdge.IsChecked == true ? DockEdge.Top : DockEdge.Bottom;
        if (SettingsStore.Current.Dock.Edge != edge) SettingsStore.Update(s => s.Dock.Edge = edge);
    }
}
