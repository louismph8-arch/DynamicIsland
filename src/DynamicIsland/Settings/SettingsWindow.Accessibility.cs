using System.Windows;
using DynamicIsland.Animation;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>
/// Page Apparence, groupe Accessibilité (refonte O3) : « Réduire les animations » et « Réduire la transparence ». Par défaut chaque
/// option suit Windows (l'interrupteur montre ce qui s'applique, la ligne dessous dit d'où ça vient) ; toucher l'interrupteur
/// fixe le choix ici, « Comme Windows » revient au défaut. Windows n'est jamais modifié.
/// </summary>
public partial class SettingsWindow
{
    private bool _watchingWindowsMotion;

    private void ShowAccessibility()
    {
        if (!_watchingWindowsMotion)
        {
            _watchingWindowsMotion = true;
            MotionPrefs.WindowsChanged += ShowAccessibility;
            Closed += (_, _) => MotionPrefs.WindowsChanged -= ShowAccessibility;
        }
        var look = SettingsStore.Current.Appearance;
        bool loading = _loading;
        _loading = true;
        ReduceMotionToggle.IsChecked = MotionPrefs.ReduceMotion;
        ReduceTransparencyToggle.IsChecked = MotionPrefs.ReduceTransparency;
        _loading = loading;
        Describe(look.ReduceMotion, !MotionPrefs.WindowsAnimations, ReduceMotionSource, ReduceMotionFollow);
        Describe(look.ReduceTransparency, !MotionPrefs.WindowsTransparency, ReduceTransparencySource, ReduceTransparencyFollow);
    }

    private static void Describe(AccessibilityChoice choice, bool windowsReduces, System.Windows.Controls.TextBlock source, System.Windows.Controls.Button follow)
    {
        string windows = Loc.T(windowsReduces ? "settings.accessibility.on" : "settings.accessibility.off");
        source.Text = choice == AccessibilityChoice.Windows
            ? Loc.F("settings.accessibility.follows_windows", windows)
            : Loc.F("settings.accessibility.overridden", windows);
        follow.Visibility = choice == AccessibilityChoice.Windows ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Accessibility_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true;
        var choice = on ? AccessibilityChoice.On : AccessibilityChoice.Off;
        if (sender == ReduceMotionToggle) SettingsStore.Update(s => s.Appearance.ReduceMotion = choice);
        else SettingsStore.Update(s => s.Appearance.ReduceTransparency = choice);
        ShowAccessibility();
    }

    private void AccessibilityFollow_Click(object sender, RoutedEventArgs e)
    {
        if (sender == ReduceMotionFollow) SettingsStore.Update(s => s.Appearance.ReduceMotion = AccessibilityChoice.Windows);
        else SettingsStore.Update(s => s.Appearance.ReduceTransparency = AccessibilityChoice.Windows);
        ShowAccessibility();
    }
}
