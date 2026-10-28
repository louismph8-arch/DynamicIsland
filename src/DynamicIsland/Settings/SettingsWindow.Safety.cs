using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Paramètres, solidité (6A) : langue (Général), rapport de bug, sauvegardes des réglages, mode sans échec et modules
/// arrêtés après une erreur (À propos).
/// </summary>
public partial class SettingsWindow
{
    private bool _languageLoading;

    // ------------------------------------------------------------------ langue

    private void ShowLanguage()
    {
        _languageLoading = true;
        (SettingsStore.Current.Language switch
        {
            Loc.French => LanguageFrench,
            Loc.English => LanguageEnglish,
            _ => LanguageAuto,
        }).IsChecked = true;
        _languageLoading = false;
        LanguageRestartCard.Visibility = Loc.Resolve(SettingsStore.Current.Language) != Loc.Language ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Langue choisie : enregistrée, appliquée au redémarrage (proposé tout de suite).</summary>
    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || _languageLoading) return;
        var language = LanguageFrench.IsChecked == true ? Loc.French : LanguageEnglish.IsChecked == true ? Loc.English : "auto";
        if (SettingsStore.Current.Language != language) SettingsStore.Update(s => s.Language = language);
        ShowLanguage();
    }

    private void RestartApp_Click(object sender, RoutedEventArgs e) => App.Restart();

    // ------------------------------------------------------------------ À propos : solidité

    private void ShowSafety()
    {
        SafeModeBanner.Visibility = App.SafeMode ? Visibility.Visible : Visibility.Collapsed;
        SafeModeButton.IsEnabled = !App.SafeMode;
        StoppedModulesCard.Visibility = ModuleGuard.Disabled.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StoppedModulesText.Text = Loc.F("settings.modules.stopped.description", string.Join(", ", ModuleGuard.Disabled.Select(ModuleGuard.Name)));
        ShowBackups();
    }

    private void BugReport_Click(object sender, RoutedEventArgs e)
    {
        BugReportText.Visibility = Visibility.Visible;
        try
        {
            var path = BugReport.Create();
            BugReportText.Text = Loc.F("settings.report.done", Path.GetFileName(path));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Rapport de bug", ex);
            BugReportText.Text = Loc.F("settings.report.failed", ex.Message);
        }
    }

    /// <summary>Les 10 dernières copies des réglages, chacune avec « Revenir à cette version ».</summary>
    private void ShowBackups()
    {
        var entries = SettingsBackup.List();
        BackupsDescription.Text = App.SafeMode ? Loc.T("settings.backups.safe") : Loc.F("settings.backups.description", SettingsBackup.Kept);
        BackupList.Children.Clear();
        if (entries.Count == 0)
        {
            BackupList.Children.Add(new TextBlock { Text = Loc.T("settings.backups.none"), Style = (Style)FindResource("CardDescription") });
            return;
        }
        foreach (var entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = entry.When.ToString("f", Loc.Culture), FontSize = TypeRamp.Body });
            texts.Children.Add(new TextBlock
            {
                Text = $"{Ago(entry.When)} · {SettingsBackup.Describe(entry) ?? Loc.T("settings.backups.unreadable")}",
                Style = (Style)FindResource("CardDescription"),
            });
            var restore = new Button
            {
                Style = (Style)FindResource("SettingsButton"),
                Content = Loc.T("settings.backups.restore"),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !App.SafeMode,
            };
            restore.Click += (_, _) => RestoreBackup(entry);
            Grid.SetColumn(restore, 1);
            row.Children.Add(texts);
            row.Children.Add(restore);
            BackupList.Children.Add(row);
        }
    }

    private static string Ago(DateTime when)
    {
        var age = DateTime.Now - when;
        return age.TotalMinutes < 1 ? Loc.T("time.just_now")
            : age.TotalHours < 1 ? Loc.F("time.minutes_ago", (int)age.TotalMinutes)
            : age.TotalDays < 1 ? Loc.F("time.hours_ago", (int)age.TotalHours)
            : Loc.F("time.days_ago", (int)age.TotalDays);
    }

    /// <summary>Après confirmation : l'état actuel est sauvé, la copie remise, puis l'app redémarre.</summary>
    private void RestoreBackup(SettingsBackup.Entry entry)
    {
        var answer = MessageBox.Show(this, Loc.F("settings.backups.confirm", entry.When.ToString("f", Loc.Culture)),
            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        try
        {
            SettingsBackup.Restore(entry);
            App.Restart();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Réglages : retour à une version précédente", ex);
            MessageBox.Show(this, Loc.F("settings.backups.failed", ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SafeModeRestart_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("settings.safe.confirm"), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) App.Restart(safeMode: true);
    }
}
