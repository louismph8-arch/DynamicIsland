using System.Windows;
using System.Windows.Input;
using DynamicIsland.Services;
using DynamicIsland.Services.NightLight;

namespace DynamicIsland.Settings;

/// <summary>
/// Paramètres → Apparence → « Concentration » (7F) : veilleuse (interrupteur, intensité, programmation, jeux, ce qui l'empêche de
/// marcher : HDR, limite de Windows, veilleuse de Windows) et « Ne pas déranger ».
/// </summary>
public partial class SettingsWindow
{
    private void ShowFocus()
    {
        var night = SettingsStore.Current.Focus.NightLight;
        bool loading = _loading;
        _loading = true;
        NightLightToggle.IsChecked = night.Enabled;
        NightGamesToggle.IsChecked = night.PauseInGames;
        NightStrengthSlider.Value = Math.Max(NightStrengthSlider.Minimum, night.Strength);
        NightScheduleAlways.IsChecked = night.Schedule == NightLightSchedule.Always;
        NightScheduleSun.IsChecked = night.Schedule == NightLightSchedule.Sun;
        NightScheduleHours.IsChecked = night.Schedule == NightLightSchedule.Hours;
        if (!NightFromBox.IsKeyboardFocused) NightFromBox.Text = night.From;
        if (!NightToBox.IsKeyboardFocused) NightToBox.Text = night.To;
        _loading = loading;

        NightHoursCard.Visibility = night.Schedule == NightLightSchedule.Hours ? Visibility.Visible : Visibility.Collapsed;
        NightStrengthText.Text = StrengthText(night.Strength);
        NightScheduleSun.Tag = CityLocation.SunToday() is { Polar: SunCalculator.Polar.None } sun
            ? Loc.F("settings.nightlight.schedule.sun.desc", CityLocation.Hour(sun.Sunset), CityLocation.Hour(sun.Sunrise), SettingsStore.Current.Widgets.City)
            : Loc.T("settings.nightlight.schedule.sun.unknown");
        NightLightStatus.Text = NightLightStatusText(night);

        bool dnd = FocusService.DoNotDisturb;
        FocusDndText.Text = dnd ? FocusService.Describe(DateTime.Now) : Loc.T("settings.focus.dnd.desc");
        FocusDndButton.Content = Loc.T(dnd ? "dnd.stop" : "settings.focus.dnd.choose");
    }

    private static string StrengthText(int strength) =>
        Loc.F("settings.nightlight.strength.value", strength, Math.Round(GammaRamp.Kelvin(strength / 100.0) / 100) * 100);

    /// <summary>État + ce qui gêne : écrans HDR, intensité réduite par Windows, écran qui refuse, veilleuse de Windows aussi active.</summary>
    private static string NightLightStatusText(NightLightSettings night)
    {
        var parts = new List<string> { Loc.F("settings.nightlight.state", NightLightService.StateText()) };
        if (App.SafeMode) parts.Add(Loc.T("settings.nightlight.safe_mode"));
        var report = NightLightService.LastReport;
        var hdr = report.HdrScreens.Count > 0 ? report.HdrScreens : HdrNamesNow();
        if (hdr.Count > 0) parts.Add(Loc.F("settings.nightlight.hdr", string.Join(", ", hdr)));
        if (report.LimitedTo is { } limit && night.Enabled) parts.Add(Loc.F("settings.nightlight.limited", limit));
        if (report.Failed && night.Enabled) parts.Add(Loc.T("settings.nightlight.failed"));
        if (DisplayFacts.WindowsNightLightOn() == true) parts.Add(Loc.T("settings.nightlight.windows_on"));
        return string.Join(Environment.NewLine, parts);
    }

    private static List<string> HdrNamesNow()
    {
        var hdr = DisplayFacts.HdrScreens();
        return hdr.Count == 0 ? [] : ScreenInfo.All().Where(m => hdr.Contains(m.Device)).Select(m => m.Name.Length > 0 ? m.Name : m.Device).ToList();
    }

    private void NightLight_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsStore.Update(s =>
        {
            s.Focus.NightLight.Enabled = NightLightToggle.IsChecked == true;
            s.Focus.NightLight.PauseInGames = NightGamesToggle.IsChecked == true;
        });
        ShowFocus();
    }

    private void NightStrength_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int strength = (int)Math.Round(e.NewValue);
        NightStrengthText.Text = StrengthText(strength);
        if (_loading) return;
        SettingsStore.Update(s => s.Focus.NightLight.Strength = strength);
    }

    private void NightSchedule_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var schedule = ReferenceEquals(sender, NightScheduleSun) ? NightLightSchedule.Sun
            : ReferenceEquals(sender, NightScheduleHours) ? NightLightSchedule.Hours
            : NightLightSchedule.Always;
        SettingsStore.Update(s => s.Focus.NightLight.Schedule = schedule);
        ShowFocus();
    }

    private void NightHours_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveNightHours();
    }

    private void NightHours_LostFocus(object sender, RoutedEventArgs e) => SaveNightHours();

    /// <summary>« 21:00 », « 9:30 » ou « 21h » acceptés ; autre chose = l'heure d'avant revient.</summary>
    private void SaveNightHours()
    {
        if (_loading) return;
        string? from = ParseHour(NightFromBox.Text), to = ParseHour(NightToBox.Text);
        SettingsStore.Update(s =>
        {
            if (from != null) s.Focus.NightLight.From = from;
            if (to != null) s.Focus.NightLight.To = to;
        });
        var night = SettingsStore.Current.Focus.NightLight;
        NightFromBox.Text = night.From;
        NightToBox.Text = night.To;
    }

    internal static string? ParseHour(string text)
    {
        var plain = text.Trim().Replace('h', ':').Replace('H', ':');
        if (plain.EndsWith(':')) plain += "00";
        return TimeOnly.TryParse(plain, System.Globalization.CultureInfo.InvariantCulture, out var time) ? time.ToString("HH:mm") : null;
    }

    private void FocusDnd_Click(object sender, RoutedEventArgs e)
    {
        if (FocusService.DoNotDisturb)
        {
            FocusService.Stop();
            return;
        }
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = FocusDndButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var item in Views.FocusMenu.Items()) menu.Items.Add(item);
        menu.IsOpen = true;
    }
}
