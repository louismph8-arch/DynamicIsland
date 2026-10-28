using System.IO;
using System.Windows;
using System.Windows.Input;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Settings;

/// <summary>Page « Partage » : export / import de thèmes (.opaline, anciens .bloomtheme) et mises à jour GitHub.</summary>
public partial class SettingsWindow
{
    /// <summary>Thème lu, en attente de confirmation.</summary>
    private ThemeFile? _pendingTheme;

    /// <summary>Double-clic sur un thème (ou « Importer… ») : aperçu puis confirmation.</summary>
    public static void OpenImport(string path)
    {
        Open(SettingsPage.Sharing);
        _instance?.PreviewImport(path);
    }

    private void LoadSharing()
    {
        RepositoryBox.Text = SettingsStore.Current.Updates.Repository;
        ShowUpdateState();
    }

    // ------------------------------------------------------------------ export

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("sharing.export.dialog"),
            Filter = ThemeShareService.SaveFilter,
            FileName = (ProfileService.Active?.Name ?? Loc.T("sharing.export.default_name")) + ThemeShareService.Extension,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ThemeShareService.Export(dialog.FileName, ExportAllProfiles.IsChecked == true, ExportWallpapers.IsChecked == true);
            long size = new FileInfo(dialog.FileName).Length;
            ExportResultText.Text = Loc.F("sharing.export.done", Path.GetFileName(dialog.FileName), Math.Max(1, size / 1024));
            Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
        }
        catch (Exception ex)
        {
            Log.Error("Thème : export", ex);
            ExportResultText.Text = Loc.F("sharing.export.failed", ex.Message);
        }
    }

    // ------------------------------------------------------------------ import

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("sharing.import.dialog"),
            Filter = ThemeShareService.OpenFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
        };
        if (dialog.ShowDialog(this) == true) PreviewImport(dialog.FileName);
    }

    private void PreviewImport(string path)
    {
        ImportResultText.Text = "";
        try
        {
            _pendingTheme = ThemeShareService.Read(path);
            ImportPreviewText.Text = ThemeShareService.Describe(_pendingTheme);
            ImportPreview.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _pendingTheme = null;
            ImportPreview.Visibility = Visibility.Collapsed;
            ImportResultText.Text = Loc.F("sharing.import.unreadable", ex.Message);
        }
    }

    private async void ConfirmImport_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingTheme is not { } theme) return;
        ConfirmImportButton.IsEnabled = false;
        try
        {
            var result = await ThemeShareService.ImportAsync(theme);
            ImportPreview.Visibility = Visibility.Collapsed;
            var text = Loc.F("sharing.import.done", string.Join(", ", result.Profiles.Select(p => Loc.F("format.quoted", p))), result.Apps, result.Activated);
            if (result.Missing.Count > 0) text += " " + Loc.F("sharing.import.missing", string.Join(", ", result.Missing));
            ImportResultText.Text = text;
            (Application.Current as App)?.Notify(Icons.Check, Loc.F("sharing.import.notice", theme.Name));
        }
        catch (Exception ex)
        {
            Log.Error("Thème : import", ex);
            ImportResultText.Text = Loc.F("sharing.import.failed", ex.Message);
        }
        finally
        {
            _pendingTheme = null;
            ConfirmImportButton.IsEnabled = true;
        }
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e)
    {
        _pendingTheme = null;
        ImportPreview.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ mises à jour

    private void ShowUpdateState()
    {
        var update = UpdateService.Available;
        CurrentVersionText.Text = Loc.F("updates.installed", UpdateService.CurrentText);
        UpdateStatusText.Text = UpdateService.Status;
        CheckUpdateButton.IsEnabled = !UpdateService.Busy;
        UpdateActions.Visibility = update != null ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = update?.AssetUrl != null ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Content = update != null ? Loc.F("updates.install_version", update.Tag) : Loc.T("updates.install");
        InstallUpdateButton.IsEnabled = !UpdateService.Busy;
        SkipUpdateButton.Visibility = update != null && update.Tag != SettingsStore.Current.Updates.SkippedVersion
            ? Visibility.Visible : Visibility.Collapsed;
        ReleaseNotesText.Text = update?.Notes ?? "";
        ReleaseNotesText.Visibility = string.IsNullOrWhiteSpace(update?.Notes) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await UpdateService.CheckAsync();

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Available is not { } update) return;
        var answer = MessageBox.Show(this,
            Loc.F("updates.confirm", AppInfo.Name, update.Tag),
            Loc.T("updates.confirm.title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        try
        {
            await UpdateService.InstallAsync();
        }
        catch (Exception)
        {
            // Le message d'erreur est déjà dans l'état affiché (UpdateService.Status).
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Available?.PageUrl is { Length: > 0 } page && page.StartsWith("https://github.com/", StringComparison.Ordinal))
            Start(page, "");
    }

    private void SkipUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Available is { } update) SettingsStore.Update(s => s.Updates.SkippedVersion = update.Tag);
        ShowUpdateState();
    }

    private void RepositoryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyRepository();
    }

    private void RepositoryBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRepository();

    private void ApplyRepository()
    {
        var repository = RepositoryBox.Text.Trim();
        if (repository == SettingsStore.Current.Updates.Repository) return;
        SettingsStore.Update(s => s.Updates.Repository = repository);
        _ = UpdateService.CheckAsync();
    }
}
