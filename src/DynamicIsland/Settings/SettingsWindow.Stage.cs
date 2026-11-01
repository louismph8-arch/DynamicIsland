using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>Page Dock → « Stage Manager » (7G) : activer / couper, raccourci (aucun par défaut), exceptions par appli.</summary>
public partial class SettingsWindow
{
    private void StageModeToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = StageModeToggle.IsChecked == true;
        SettingsStore.Update(s => s.Stage.Enabled = on);
        CheckClickedToggle(sender, on); // module arrêté, mode sans échec… : l'interrupteur revient et le bandeau dit pourquoi
    }

    private void DefaultStageHotkey_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Stage.Hotkey = Hotkeys.DefaultStage);
}
