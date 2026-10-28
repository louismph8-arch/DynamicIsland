using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>
/// Paramètres → Île → Claude (5J, 6D-3) : activation, moteur (Claude Code ou clé API), état de Claude Code ou de la clé,
/// modèle de chaque moteur.
/// </summary>
public partial class SettingsWindow
{
    private const string ConsoleKeysUrl = "https://console.anthropic.com/settings/keys";
    private bool _claudeModelsBuilt;

    /// <summary>Moteurs, modèles et état, vérifiés à l'ouverture de la page Île.</summary>
    private void ShowClaude()
    {
        ClaudeToggle.IsChecked = SettingsStore.Current.Claude.Enabled;
        if (!_claudeModelsBuilt)
        {
            _claudeModelsBuilt = true;
            foreach (var (engine, title, description) in new[]
                     {
                         (ClaudeEngine.ClaudeCode, Loc.T("settings.claude.engine.code"), Loc.T("settings.claude.engine.code.desc")),
                         (ClaudeEngine.ApiKey, Loc.T("settings.claude.engine.api"), Loc.T("settings.claude.engine.api.desc")),
                     })
            {
                var option = new RadioButton
                {
                    Style = (Style)FindResource("OptionCard"),
                    GroupName = "ClaudeEngine",
                    Content = title,
                    Tag = description,
                    IsChecked = SettingsStore.Current.Claude.Engine == engine,
                };
                option.Checked += (_, _) =>
                {
                    if (_loading || SettingsStore.Current.Claude.Engine == engine) return;
                    SettingsStore.Update(s => s.Claude.Engine = engine);
                    ShowClaudeEngine();
                };
                ClaudeEngineList.Children.Add(option);
            }
            AddModelOptions(ClaudeModelList, "ClaudeModel", () => SettingsStore.Current.Claude.Model, id => SettingsStore.Update(s => s.Claude.Model = id));
            AddModelOptions(ClaudeApiModelList, "ClaudeApiModel", () => SettingsStore.Current.Claude.ApiModel, id => SettingsStore.Update(s => s.Claude.ApiModel = id));
        }
        ShowClaudeEngine();
    }

    private void AddModelOptions(Panel list, string group, Func<string> current, Action<string> choose)
    {
        foreach (var (id, name, description) in ClaudeService.Models)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("OptionCard"),
                GroupName = group,
                Content = name,
                Tag = Loc.T(description),
                IsChecked = current() == id,
            };
            option.Checked += (_, _) =>
            {
                if (!_loading && current() != id) choose(id);
            };
            list.Children.Add(option);
        }
    }

    /// <summary>Claude Code : sa carte et ses modèles ; clé API : la carte de la clé et ses modèles.</summary>
    private void ShowClaudeEngine()
    {
        bool api = SettingsStore.Current.Claude.Engine == ClaudeEngine.ApiKey;
        ClaudeCodeCard.Visibility = ClaudeModelList.Visibility = api ? Visibility.Collapsed : Visibility.Visible;
        ClaudeApiCard.Visibility = ClaudeApiModelList.Visibility = api ? Visibility.Visible : Visibility.Collapsed;
        if (api) ShowClaudeKey();
        else _ = CheckClaudeAsync();
    }

    private void ShowClaudeKey()
    {
        var masked = ClaudeApiKey.Masked();
        ClaudeKeyStatusText.Text = masked != null ? Loc.F("settings.claude.api.saved", masked) : Loc.T("settings.claude.api.none");
        ClaudeKeyCheckButton.Visibility = ClaudeKeyDeleteButton.Visibility = masked != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CheckClaudeAsync()
    {
        ClaudeStatusText.Text = Loc.T("claude.checking");
        ClaudeLoginButton.Visibility = Visibility.Collapsed;
        var status = await ClaudeService.CheckAsync();
        ClaudeStatusText.Text = status.Installed && !status.LoggedIn
            ? status.Description + " " + Loc.T("claude.login.help")
            : status.Description;
        ClaudeLoginButton.Visibility = status.Installed && !status.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClaudeLogin_Click(object sender, RoutedEventArgs e) => ClaudeService.OpenLogin();

    private void ClaudeUpdate_Click(object sender, RoutedEventArgs e) => ClaudeService.OpenUpdate();

    private void ClaudeCheck_Click(object sender, RoutedEventArgs e) => _ = CheckClaudeAsync();

    // ------------------------------------------------------------------ clé API (6D-3)

    private void ClaudeKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SaveClaudeKeyAsync();
    }

    private void ClaudeKeySave_Click(object sender, RoutedEventArgs e) => _ = SaveClaudeKeyAsync();

    /// <summary>Vérifiée auprès de l'API (gratuit) avant d'être gardée, chiffrée par Windows. Le champ est vidé tout de suite.</summary>
    private async Task SaveClaudeKeyAsync()
    {
        var key = ClaudeKeyBox.Password.Trim();
        ClaudeKeyBox.Clear();
        if (!ClaudeApiKey.LooksValid(key))
        {
            ShowClaudeKeyMessage(Loc.T("settings.claude.api.invalid"));
            return;
        }
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.checking"));
        var problem = await ClaudeService.CheckApiKeyAsync(key);
        if (problem != null)
        {
            ShowClaudeKeyMessage(problem);
            return;
        }
        ClaudeApiKey.Save(key);
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.ok"));
        ShowClaudeKey();
    }

    private async void ClaudeKeyCheck_Click(object sender, RoutedEventArgs e)
    {
        if (ClaudeApiKey.Load() is not { } key) return;
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.checking"));
        ShowClaudeKeyMessage(await ClaudeService.CheckApiKeyAsync(key) ?? Loc.T("settings.claude.api.ok"));
    }

    private void ClaudeKeyDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("settings.claude.api.delete.confirm"), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        ClaudeApiKey.Delete();
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.deleted"));
        ShowClaudeKey();
    }

    private void ClaudeKeyCreate_Click(object sender, RoutedEventArgs e) => Start(ConsoleKeysUrl, "");

    private void ShowClaudeKeyMessage(string text)
    {
        ClaudeKeyMessageText.Text = text;
        ClaudeKeyMessageText.Visibility = Visibility.Visible;
    }
}
