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
    private string _devicesShown = "";
    private bool _active, _reading;
    private const double BaseHeight = 170;
    /// <summary>Appareils relus toutes les 4 s, seulement onglet ouvert (rien au repos).</summary>
    private readonly System.Windows.Threading.DispatcherTimer _devicesTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>La rangée des appareils est apparue ou a disparu : l'île change de hauteur.</summary>
    public event Action? HeightChanged;

    public bool HasDevices => DevicesRow.Visibility == Visibility.Visible;

    /// <summary>Onglet Contrôles ouvert : liste des appareils lue tout de suite puis toutes les 4 s.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value)
            {
                _ = RefreshDevicesAsync();
                _devicesTimer.Start();
            }
            else
            {
                _devicesTimer.Stop();
            }
        }
    }

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
        _devicesTimer.Tick += (_, _) =>
        {
            _ = RefreshDevicesAsync();
            RefreshFocus(); // temps restant du « Ne pas déranger » (onglet ouvert seulement)
        };
        FocusService.Changed += RefreshFocus;
        Services.NightLight.NightLightService.Changed += RefreshNightLight;
        Stage.StageManager.Changed += RefreshStage;
        RefreshFocus();
        RefreshNightLight();
        RefreshStage();
    }

    // ------------------------------------------------------------------ Stage Manager (7G)

    /// <summary>Tuile « Stage Manager » : allumée si le mode tourne ; grisée en mode sans échec.</summary>
    internal void RefreshStage()
    {
        StageToggle.IsChecked = Stage.StageManager.IsOn;
        StageToggle.IsEnabled = !App.SafeMode;
        StageToggle.ToolTip = Loc.T(Stage.StageManager.IsOn ? "stage.tile.on" : "stage.tile.off");
    }

    private void StageToggle_Click(object sender, RoutedEventArgs e)
    {
        Stage.StageManager.Toggle();
        RefreshStage();
    }

    /// <summary>Clic droit : activer / couper et paramètres (exceptions, raccourci).</summary>
    private void StageToggle_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = StageToggle, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var toggle = new MenuItem { Header = Loc.T(Stage.StageManager.IsOn ? "palette.stage.off" : "palette.stage.on"), IsEnabled = !App.SafeMode };
        toggle.Click += (_, _) => Stage.StageManager.Toggle();
        menu.Items.Add(toggle);
        var settings = new MenuItem { Header = Loc.T("stage.menu.settings") };
        settings.Click += (_, _) => SettingsWindow.Open(SettingsPage.Dock);
        menu.Items.Add(settings);
        menu.IsOpen = true;
    }

    private async Task RefreshDevicesAsync()
    {
        if (_reading) return;
        _reading = true;
        try
        {
            ShowDevices(await DeviceListService.SnapshotAsync());
        }
        catch (Exception ex)
        {
            Log.Error("Appareils connectés", ex);
        }
        finally
        {
            _reading = false;
        }
    }

    /// <summary>Pastilles des appareils (icône, nom, batterie) ; rangée cachée s'il n'y en a aucun.</summary>
    internal void ShowDevices(IReadOnlyList<DeviceListService.Device> devices)
    {
        var shown = string.Join("|", devices.Select(d => $"{d.Kind}:{d.Name}:{d.Battery}"));
        if (shown == _devicesShown) return;
        _devicesShown = shown;
        DevicesPanel.Children.Clear();
        foreach (var device in devices)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = device.Kind switch
                {
                    DeviceListService.Kind.Headset => Icons.Headphones,
                    DeviceListService.Kind.Mouse => Icons.Mouse,
                    DeviceListService.Kind.Keyboard => Icons.Keyboard,
                    DeviceListService.Kind.Controller => Icons.Gamepad,
                    DeviceListService.Kind.Storage => Icons.Usb,
                    _ => Icons.Bluetooth,
                },
                FontFamily = FontStack.Icons,
                FontSize = IconSize.M,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, Space.Px, Space.S3, 0),
                Foreground = Paint.TextSecondary,
            });
            content.Children.Add(new TextBlock
            {
                Text = device.Name.Length > 16 ? device.Name[..15].TrimEnd() + "…" : device.Name,
                FontSize = TypeRamp.Footnote,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (device.Battery is { } battery)
            {
                content.Children.Add(new TextBlock
                {
                    Text = battery <= 20 ? Icons.BatteryLow : battery <= 60 ? Icons.BatteryMedium : Icons.BatteryFull,
                    FontFamily = FontStack.Icons,
                    FontSize = IconSize.M,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Space.S4, Space.Px, Space.S1, 0),
                    Foreground = battery <= 20 ? Paint.Danger : Paint.TextSecondary,
                });
                content.Children.Add(new TextBlock
                {
                    Text = Loc.F("format.percent", battery),
                    FontSize = TypeRamp.Footnote,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Paint.TextSecondary,
                });
            }
            var chip = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Height = Sizes.ProfileChip,
                Padding = new Thickness(Space.S6, 0, Space.S7, 0),
                Margin = new Thickness(0, 0, Space.S3, 0),
                Content = content,
                Background = Paint.Surface,
                ToolTip = device.Name,
            };
            if (device.Drive is { } drive) chip.Click += (_, _) => DeviceNotifier.OpenDrive(drive); // clé USB : l'ouvrir
            else chip.IsHitTestVisible = false;
            DevicesPanel.Children.Add(chip);
        }
        bool any = devices.Count > 0;
        if (any == HasDevices) return;
        DevicesRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Height = BaseHeight + (any ? IslandShape.DevicesRow : 0);
        HeightChanged?.Invoke();
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
        RefreshFocus();
        RefreshNightLight();
        RefreshStage();
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
        DarkToggle.ToolTip = Loc.T(dark ? "state.on" : "state.off");
    }

    private void DarkToggle_Click(object sender, RoutedEventArgs e)
    {
        bool dark = DarkToggle.IsChecked == true;
        ThemeService.SetDarkMode(dark);
        DarkToggle.ToolTip = Loc.T(dark ? "state.on" : "state.off");
    }

    // ------------------------------------------------------------------ Ne pas déranger et veilleuse (7F)

    /// <summary>Tuile « Ne pas déranger » : allumée + temps restant (« Encore 25 min », « Jusqu'à 8:00 »…).</summary>
    internal void RefreshFocus()
    {
        bool on = FocusService.DoNotDisturb;
        FocusToggle.IsChecked = on;
        FocusStateText.Text = on ? FocusService.Describe(DateTime.Now) : Loc.T("dnd.tile.choose");
    }

    /// <summary>Rendu --snapshot : les deux tuiles allumées, sans rien changer (ni réglages, ni écrans).</summary>
    internal void ShowFocusSample()
    {
        FocusToggle.IsChecked = true;
        FocusStateText.Text = Loc.F("dnd.state.minutes", 25);
        NightToggle.IsChecked = true;
        StageToggle.IsChecked = true;
    }

    /// <summary>Tuile « Veilleuse » : allumée + température (infobulle).</summary>
    internal void RefreshNightLight()
    {
        NightToggle.IsChecked = Services.NightLight.NightLightService.IsOn;
        NightToggle.ToolTip = Services.NightLight.NightLightService.StateText();
    }

    /// <summary>Éteint : on choisit une durée ; allumé : on l'arrête.</summary>
    private void FocusToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FocusService.DoNotDisturb) FocusService.Stop();
        else ShowFocusMenu(FocusToggle);
        RefreshFocus();
    }

    private void FocusToggle_RightClick(object sender, MouseButtonEventArgs e)
    {
        ShowFocusMenu(FocusToggle);
        e.Handled = true;
    }

    private static void ShowFocusMenu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var item in FocusMenu.Items()) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private void NightToggle_Click(object sender, RoutedEventArgs e)
    {
        Services.NightLight.NightLightService.Toggle();
        RefreshNightLight();
    }

    /// <summary>Clic droit : intensité (25 à 100 %) et paramètres de la veilleuse.</summary>
    private void NightToggle_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = NightToggle, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        int current = SettingsStore.Current.Focus.NightLight.Strength;
        menu.Items.Add(new MenuItem { Header = Services.NightLight.NightLightService.StateText(), IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        foreach (int strength in new[] { 25, 50, 75, 100 })
        {
            var item = new MenuItem { Header = Loc.F("nightlight.menu.strength", strength), IsCheckable = true, IsChecked = current == strength };
            int value = strength;
            item.Click += (_, _) => SettingsStore.Update(s =>
            {
                s.Focus.NightLight.Strength = value;
                s.Focus.NightLight.Enabled = true;
            });
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Loc.T("nightlight.menu.settings") };
        settings.Click += (_, _) => SettingsWindow.Open(SettingsPage.Appearance);
        menu.Items.Add(settings);
        menu.IsOpen = true;
    }

    private static string VolumeIcon(double percent, bool muted) =>
        muted || percent < 0.5 ? Icons.VolumeMute
        : percent < 34 ? Icons.VolumeLow
        : percent < 67 ? Icons.VolumeMedium
        : Icons.VolumeHigh;
}
