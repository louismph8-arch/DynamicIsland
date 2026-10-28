using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

public partial class ControlCenterView : UserControl
{
    private VolumeService? _volume;
    private BrightnessService? _brightness;
    private string _profilesShown = "";

    public ControlCenterView()
    {
        InitializeComponent();
        VolumeSlider.UserChanged += value =>
        {
            _volume?.SetLevel(value / 100);
            VolumeSlider.Icon = VolumeIcon(value, muted: false);
        };
        BrightnessSlider.UserChanged += value => _brightness?.Set((int)Math.Round(value));
        SettingsStore.Changed += RefreshProfiles;
        RefreshProfiles();
    }

    /// <summary>Pastilles des profils (celle du profil actif est remplie de sa couleur) : clic = changer de profil.</summary>
    private void RefreshProfiles()
    {
        var active = ProfileService.Active;
        var shown = string.Join("|", ProfileService.All.Select(p => $"{p.Id}:{p.Name}:{p.Icon}:{p.AccentStart}:{p.AccentEnd}"))
                    + $"#{active?.Id}:{AppearanceService.AccentStart}:{AppearanceService.AccentEnd}";
        if (shown == _profilesShown) return; // rien n'a changé (la plupart des réglages ne concernent pas les profils)
        _profilesShown = shown;

        ProfilesPanel.Children.Clear();
        foreach (var profile in ProfileService.All)
        {
            bool isActive = profile == active;
            var (start, end) = ProfileService.AccentOf(profile);
            var gradient = Paint.Gradient(start, end, 0);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = Icons.Glyph(profile.Icon),
                FontFamily = FontStack.Icons,
                FontSize = IconSize.M,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, Space.Px, Space.S3, 0),
                Foreground = isActive ? Paint.White : gradient,
            });
            content.Children.Add(new TextBlock { Text = profile.Name, FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            var chip = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Height = Sizes.ProfileChip,
                Padding = new Thickness(Space.S6, 0, Space.S7, 0),
                Margin = new Thickness(0, 0, Space.S3, 0),
                Content = content,
                Background = isActive ? gradient : Paint.Surface,
                ToolTip = isActive ? Loc.T("profiles.is_active") : Loc.F("controls.switch_profile", profile.Name),
            };
            var id = profile.Id;
            chip.Click += (_, _) => ProfileService.Switch(id);
            ProfilesPanel.Children.Add(chip);
        }
    }

    private void Profiles_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = (ScrollViewer)sender;
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    public void Attach(VolumeService volume, BrightnessService brightness)
    {
        _volume = volume;
        _brightness = brightness;
        brightness.Changed += () => Dispatcher.InvokeAsync(UpdateBrightness);
    }

    /// <summary>Relit l'état réel du système (appelé à l'ouverture de l'onglet).</summary>
    public void Refresh()
    {
        UpdateVolume();
        UpdateBrightness();
        _brightness?.RequestRead();
        UpdateDarkMode();
    }

    private void UpdateVolume()
    {
        if (_volume != null && _volume.TryRead(out double level, out bool muted))
        {
            VolumeSlider.IsEnabled = true;
            VolumeSlider.Label = Loc.T(muted ? "controls.volume.muted" : "controls.volume");
            if (!VolumeSlider.IsDragging) VolumeSlider.Value = level * 100;
            VolumeSlider.Icon = VolumeIcon(level * 100, muted);
        }
        else
        {
            VolumeSlider.IsEnabled = false;
            VolumeSlider.Label = Loc.T("controls.volume.none");
            VolumeSlider.Value = 0;
            VolumeSlider.Icon = VolumeIcon(0, muted: true);
        }
    }

    private void UpdateBrightness()
    {
        var brightness = _brightness;
        if (brightness == null || !brightness.IsReady)
        {
            BrightnessSlider.IsEnabled = false;
            BrightnessSlider.Label = Loc.T("controls.brightness.searching");
        }
        else if (brightness.IsSupported)
        {
            BrightnessSlider.IsEnabled = true;
            BrightnessSlider.Label = Loc.T("controls.brightness");
            if (!BrightnessSlider.IsDragging) BrightnessSlider.Value = brightness.Current;
        }
        else
        {
            // L'écran ne répond pas en DDC/CI (souvent à activer dans son menu OSD).
            BrightnessSlider.IsEnabled = false;
            BrightnessSlider.Label = Loc.T("controls.brightness.unavailable");
            BrightnessSlider.Value = 0;
        }
    }

    private void UpdateDarkMode()
    {
        bool dark = ThemeService.IsDarkMode();
        DarkToggle.IsChecked = dark;
        DarkStateText.Text = Loc.T(dark ? "state.on" : "state.off");
    }

    private void DarkToggle_Click(object sender, RoutedEventArgs e)
    {
        bool dark = DarkToggle.IsChecked == true;
        ThemeService.SetDarkMode(dark);
        DarkStateText.Text = Loc.T(dark ? "state.on" : "state.off");
    }

    private static string VolumeIcon(double percent, bool muted) =>
        muted || percent < 0.5 ? Icons.VolumeMute
        : percent < 34 ? Icons.VolumeLow
        : percent < 67 ? Icons.VolumeMedium
        : Icons.VolumeHigh;
}
