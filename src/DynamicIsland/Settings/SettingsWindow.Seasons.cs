using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services.Seasons;

namespace DynamicIsland.Settings;

/// <summary>Paramètres → Apparence → thèmes saisonniers (6F) : interrupteur et aperçu d'une minute.</summary>
public partial class SettingsWindow
{
    private void Seasons_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsStore.Update(s => s.Appearance.Seasons = SeasonsToggle.IsChecked == true);
    }

    private void SeasonPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<Season>(name, out var season)) SeasonService.Preview(season);
    }
}
