using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Priorité 2 « confort » : actualisation de la météo et « Détecter ma ville » (page Widgets, correctif 18), son de fin et
/// volume du minuteur (page Île, correctif 19).
/// </summary>
public partial class SettingsWindow
{
    // ------------------------------------------------------------------ météo

    /// <summary>Minutes choisies par le curseur (sa position = rang dans <see cref="WidgetSettings.WeatherRefreshChoices"/>).</summary>
    private int WeatherRefreshMinutes =>
        WidgetSettings.WeatherRefreshChoices[Math.Clamp((int)Math.Round(WeatherRefreshSlider.Value), 0, WidgetSettings.WeatherRefreshChoices.Length - 1)];

    /// <summary>« Détecter ma ville » : rien n'est envoyé avant ce clic ; le résultat (ou l'erreur) s'affiche sous le champ.</summary>
    private async void DetectCity_Click(object sender, RoutedEventArgs e)
    {
        DetectCityButton.IsEnabled = false;
        CityStatusText.Text = Loc.T("city.detect.searching");
        CityStatusText.Foreground = Paint.TextSecondary;
        try
        {
            var outcome = await CityDetector.DetectAsync();
            if (outcome.Place is { } place)
            {
                SettingsStore.Update(s => CityDetector.Apply(s.Widgets, place));
                CityBox.Text = place.City;
            }
            CityStatusText.Text = outcome.Message;
            CityStatusText.Foreground = outcome.Place != null ? Paint.TextSecondary : Paint.Timer; // orange : à écrire à la main
        }
        catch (Exception ex)
        {
            Log.Error("Paramètres : détecter la ville", ex);
            CityStatusText.Text = Loc.T("city.detect.failed");
            CityStatusText.Foreground = Paint.Timer;
        }
        finally
        {
            DetectCityButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ son du minuteur

    private void ShowTimerSound()
    {
        var timer = SettingsStore.Current.Timer;
        TimerSoundText.Text = TimerSound.NameOf(timer.Sound, timer.CustomFile);
    }

    /// <summary>Menu sombre : son d'Opaline, alarmes et sonneries de Windows qui existent, fichier perso.</summary>
    private void TimerSound_Click(object sender, RoutedEventArgs e)
    {
        var timer = SettingsStore.Current.Timer;
        var menu = new ContextMenu { PlacementTarget = TimerSoundButton, Placement = PlacementMode.Bottom, Style = (Style)FindResource("ScrollingMenu") };
        menu.Resources[typeof(ScrollBar)] = FindResource(typeof(ScrollBar)); // le menu est hors de la fenêtre : sa barre fine et sombre
        menu.Items.Add(SoundItem(Loc.T("timer.sound.opaline"), TimerSound.OpalineId, timer));
        var windows = TimerSound.WindowsSounds();
        if (windows.Count > 0) menu.Items.Add(new Separator());
        foreach (var choice in windows) menu.Items.Add(SoundItem(choice.Name, choice.Id, timer));
        menu.Items.Add(new Separator());
        if (timer.CustomFile is { } file) menu.Items.Add(SoundItem(Path.GetFileName(file), TimerSound.FileId, timer));
        var browse = new MenuItem { Header = Loc.T("timer.sound.file") };
        browse.Click += (_, _) => PickTimerSoundFile();
        menu.Items.Add(browse);
        menu.IsOpen = true;
    }

    private MenuItem SoundItem(string name, string id, TimerSettings current)
    {
        var item = new MenuItem { Header = name, IsChecked = current.Sound == id };
        item.Click += (_, _) => SetTimerSound(id, SettingsStore.Current.Timer.CustomFile);
        return item;
    }

    private void PickTimerSoundFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("timer.sound.file_filter")}|*.wav;*.mp3",
            Title = Loc.T("settings.timer.sound.title"),
            InitialDirectory = SettingsStore.Current.Timer.CustomFile is { } file && Path.GetDirectoryName(file) is { } folder && Directory.Exists(folder)
                ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        if (dialog.ShowDialog(this) == true) SetTimerSound(TimerSound.FileId, dialog.FileName);
    }

    private void SetTimerSound(string id, string? file)
    {
        SettingsStore.Update(s =>
        {
            s.Timer.CustomFile = file;
            s.Timer.Sound = id;
        });
        ShowTimerSound();
        TimerSound.Test(); // on entend tout de suite ce qu'on a choisi
    }

    private void TimerTest_Click(object sender, RoutedEventArgs e) => TimerSound.Test();

    /// <summary>--snapshot : la page défilée jusqu'à <paramref name="target"/> (groupe hors du premier écran), puis remise en haut.</summary>
    private async Task SnapshotScrolledAsync(FrameworkElement target, string file)
    {
        UpdateLayout();
        double top = target.TranslatePoint(new Point(0, 0), Scroller).Y + Scroller.VerticalOffset;
        Scroller.ScrollToVerticalOffset(Math.Max(0, top - SettingsShape.SearchScrollMargin));
        UpdateLayout();
        await SnapshotRenderer.SaveAsync(this, file);
        Scroller.ScrollToTop();
    }
}
