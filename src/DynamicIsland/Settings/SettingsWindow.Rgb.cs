using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services.Rgb;

namespace DynamicIsland.Settings;

/// <summary>Page « Connecté » → éclairage RGB (6D-2) : interrupteur, état de G HUB et d'OpenRGB, options.</summary>
public partial class SettingsWindow
{
    private void ShowRgb()
    {
        var rgb = SettingsStore.Current.Rgb;
        bool loading = _loading;
        _loading = true;
        RgbToggle.IsChecked = rgb.Enabled;
        RgbMusicToggle.IsChecked = rgb.FollowMusic;
        RgbGamesToggle.IsChecked = rgb.PauseInGames;
        _loading = loading;
        RgbOptionsCard.Visibility = rgb.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!rgb.Enabled) return;

        RgbSwatch.Fill = RgbService.Current is { } color ? new SolidColorBrush(color) : Brushes.Transparent;
        RgbSwatch.Visibility = RgbService.Current.HasValue ? Visibility.Visible : Visibility.Collapsed;
        RgbLogitechText.Text = RgbService.Paused
            ? Loc.T("settings.rgb.paused")
            : Loc.T(RgbService.LogitechStatus switch
            {
                RgbService.LogitechState.Connected => "settings.rgb.logitech.on",
                RgbService.LogitechState.NotResponding => "settings.rgb.logitech.not_responding",
                RgbService.LogitechState.NotInstalled => "settings.rgb.logitech.missing",
                _ => "settings.rgb.logitech.waiting",
            });
        RgbOpenRgbText.Text = RgbService.OpenRgbDevices >= 0
            ? Loc.F("settings.rgb.openrgb.on", RgbService.OpenRgbDevices)
            : Loc.T("settings.rgb.openrgb.off");
    }

    private void Rgb_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsStore.Update(s =>
        {
            s.Rgb.Enabled = RgbToggle.IsChecked == true;
            s.Rgb.FollowMusic = RgbMusicToggle.IsChecked == true;
            s.Rgb.PauseInGames = RgbGamesToggle.IsChecked == true;
        });
        ShowRgb();
    }

    private void RgbRetry_Click(object sender, RoutedEventArgs e) => RgbService.Retry();
}
