using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Paramètres → Île (6C) : annonces d'appareils et de téléchargements, horloges du monde.</summary>
public partial class SettingsWindow
{
    private void ShowIslandExtras()
    {
        var island = SettingsStore.Current.Island;
        DeviceNoticesToggle.IsChecked = island.DeviceNotices;
        DownloadNoticesToggle.IsChecked = island.DownloadNotices;
        HealthTabToggle.IsChecked = SettingsStore.Current.Health.Tab;
        HealthDiskToggle.IsChecked = SettingsStore.Current.Health.DiskAlert;
        ShowClocks();
    }

    /// <summary>Santé du PC (6E) : onglet de l'île et alerte d'espace disque.</summary>
    private void Health_Click(object sender, RoutedEventArgs e)
    {
        bool tab = HealthTabToggle.IsChecked == true, disk = HealthDiskToggle.IsChecked == true;
        SettingsStore.Update(s =>
        {
            s.Health.Tab = tab;
            s.Health.DiskAlert = disk;
        });
    }

    private void Notices_Click(object sender, RoutedEventArgs e)
    {
        bool devices = DeviceNoticesToggle.IsChecked == true, downloads = DownloadNoticesToggle.IsChecked == true;
        SettingsStore.Update(s =>
        {
            s.Island.DeviceNotices = devices;
            s.Island.DownloadNotices = downloads;
        });
    }

    private void ShowClocks()
    {
        var clocks = SettingsStore.Current.Island.WorldClocks;
        ClockList.Children.Clear();
        foreach (var clock in clocks)
        {
            var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var time = WorldClockService.Now(clock) is { } now ? now.Time.ToString("t", Loc.Culture) : "—";
            row.Children.Add(new TextBlock { Text = clock.Name, FontSize = TypeRamp.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var hour = new TextBlock { Text = time, Style = (Style)FindResource("CardDescription"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S5, 0, Space.S4, 0) };
            Grid.SetColumn(hour, 1);
            row.Children.Add(hour);
            var remove = new Button
            {
                Style = (Style)FindResource("SmallIconButton"),
                Content = Icons.Close,
                FontSize = IconSize.S,
                ToolTip = Loc.T("settings.clocks.remove"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var target = clock;
            remove.Click += (_, _) =>
            {
                SettingsStore.Update(s => s.Island.WorldClocks.RemoveAll(c => c.Name == target.Name && c.TimeZone == target.TimeZone));
                ShowClocks();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            ClockList.Children.Add(row);
        }
        bool full = clocks.Count >= WorldClockService.MaxClocks;
        ClockCityBox.IsEnabled = AddClockButton.IsEnabled = !full;
        if (full) ShowClockStatus(Loc.F("settings.clocks.full", WorldClockService.MaxClocks));
    }

    private void ClockCityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = AddClockAsync();
    }

    private void AddClock_Click(object sender, RoutedEventArgs e) => _ = AddClockAsync();

    /// <summary>Cherche la ville (géocodage Open-Meteo : nom + fuseau horaire), puis l'ajoute.</summary>
    private async Task AddClockAsync()
    {
        var city = ClockCityBox.Text.Trim();
        if (city.Length == 0) return;
        AddClockButton.IsEnabled = false;
        ShowClockStatus(Loc.F("sun.searching", city));
        try
        {
            var place = await WorldClockService.FindAsync(city);
            if (place == null)
            {
                ShowClockStatus(Loc.F("weather.city_not_found", city));
                return;
            }
            if (SettingsStore.Current.Island.WorldClocks.Any(c => c.Name == place.Name))
            {
                ShowClockStatus(Loc.F("settings.clocks.already", place.Name));
                return;
            }
            SettingsStore.Update(s =>
            {
                if (s.Island.WorldClocks.Count < WorldClockService.MaxClocks)
                    s.Island.WorldClocks.Add(new WorldClock { Name = place.Name, TimeZone = place.TimeZone });
            });
            ClockCityBox.Text = "";
            ClockStatusText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("Horloges du monde : recherche de ville", ex);
            ShowClockStatus(Loc.T("weather.unavailable"));
        }
        finally
        {
            AddClockButton.IsEnabled = true;
            ShowClocks();
        }
    }

    private void ShowClockStatus(string text)
    {
        ClockStatusText.Text = text;
        ClockStatusText.Visibility = Visibility.Visible;
    }
}
