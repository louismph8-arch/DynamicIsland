using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>
/// Paramètres → Île → Assistant IA (5J, 6D-3, priorité 3 n° 27) : activation, moteur (Claude Code, clé API Anthropic ou clé
/// API OpenAI), état de Claude Code ou de la clé du fournisseur choisi, modèle de chaque moteur.
/// </summary>
public partial class SettingsWindow
{
    private const string AnthropicKeysUrl = "https://console.anthropic.com/settings/keys";
    private const string OpenAiKeysUrl = "https://platform.openai.com/api-keys";
    private bool _claudeModelsBuilt;

    private static ClaudeEngine Engine => SettingsStore.Current.Claude.Engine;
    private static bool OpenAiEngine => Engine == ClaudeEngine.OpenAi;

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
                         (ClaudeEngine.OpenAi, Loc.T("settings.claude.engine.openai"), Loc.T("settings.claude.engine.openai.desc")),
                         (ClaudeEngine.Codex, Loc.T("settings.claude.engine.codex"), Loc.T("settings.claude.engine.codex.desc")),
                     })
            {
                var option = new RadioButton
                {
                    Style = (Style)FindResource("OptionCard"),
                    GroupName = "ClaudeEngine",
                    Content = title,
                    Tag = description,
                    IsChecked = Engine == engine,
                };
                option.Checked += (_, _) =>
                {
                    if (_loading || Engine == engine) return;
                    SettingsStore.Update(s => s.Claude.Engine = engine);
                    ClaudeKeyMessageText.Visibility = Visibility.Collapsed;
                    ShowClaudeEngine();
                };
                ClaudeEngineList.Children.Add(option);
            }
            AddModelOptions(ClaudeModelList, "ClaudeModel", ClaudeService.Models, () => SettingsStore.Current.Claude.Model, id => SettingsStore.Update(s => s.Claude.Model = id));
            AddModelOptions(ClaudeApiModelList, "ClaudeApiModel", ClaudeService.Models, () => SettingsStore.Current.Claude.ApiModel, id => SettingsStore.Update(s => s.Claude.ApiModel = id));
            AddModelOptions(OpenAiModelList, "OpenAiModel", ClaudeService.OpenAiModels, () => SettingsStore.Current.Claude.OpenAiModel, id => SettingsStore.Update(s => s.Claude.OpenAiModel = id));
        }
        ShowClaudeEngine();
    }

    private void AddModelOptions(Panel list, string group, (string Id, string Name, string Description)[] models, Func<string> current, Action<string> choose)
    {
        foreach (var (id, name, description) in models)
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

    /// <summary>Claude Code : sa carte et ses modèles ; clé API : la carte de la clé du fournisseur et ses modèles.</summary>
    private void ShowClaudeEngine()
    {
        var engine = Engine;
        static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        ClaudeCodeCard.Visibility = ClaudeModelList.Visibility = Shown(engine == ClaudeEngine.ClaudeCode);
        ClaudeApiCard.Visibility = Shown(engine is ClaudeEngine.ApiKey or ClaudeEngine.OpenAi);
        ClaudeApiModelList.Visibility = Shown(engine == ClaudeEngine.ApiKey);
        OpenAiModelList.Visibility = Shown(engine == ClaudeEngine.OpenAi);
        CodexCard.Visibility = CodexModelList.Visibility = Shown(engine == ClaudeEngine.Codex);
        switch (engine)
        {
            case ClaudeEngine.ClaudeCode: _ = CheckClaudeAsync(); break;
            case ClaudeEngine.Codex: ShowCodexModels(); _ = CheckCodexAsync(); break;
            default: ShowClaudeKey(); break;
        }
    }

    // ------------------------------------------------------------------ ChatGPT par Codex (abonnement)

    /// <summary>Modèles que Codex propose pour ce compte (relus à chaque affichage : la liste change avec Codex).</summary>
    private void ShowCodexModels()
    {
        CodexModelList.Children.Clear();
        var models = ClaudeService.CodexModels();
        var current = SettingsStore.Current.Claude.CodexModel;
        if (current.Length > 0 && models.All(m => m.Id != current)) models.Add((current, current, Loc.T("settings.codex.model.kept")));
        foreach (var (id, name, description) in models)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("OptionCard"),
                GroupName = "CodexModel",
                Content = name,
                Tag = description,
                IsChecked = current == id,
            };
            option.Checked += (_, _) =>
            {
                if (!_loading && SettingsStore.Current.Claude.CodexModel != id) SettingsStore.Update(s => s.Claude.CodexModel = id);
            };
            CodexModelList.Children.Add(option);
        }
    }

    private async Task CheckCodexAsync()
    {
        CodexStatusText.Text = Loc.T("codex.view.checking");
        CodexLoginButton.Visibility = CodexInstallButton.Visibility = Visibility.Collapsed;
        var status = await ClaudeService.CodexCheckAsync();
        CodexStatusText.Text = !status.Installed ? status.Description + " " + Loc.T("codex.install.help")
            : !status.LoggedIn ? status.Description + " " + Loc.T("codex.login.help")
            : status.Description;
        CodexInstallButton.Visibility = status.Installed ? Visibility.Collapsed : Visibility.Visible;
        CodexUpdateButton.Visibility = status.Installed ? Visibility.Visible : Visibility.Collapsed;
        CodexLoginButton.Visibility = status.Installed && !status.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CodexInstall_Click(object sender, RoutedEventArgs e) => ClaudeService.OpenCodexInstall();

    private void CodexLogin_Click(object sender, RoutedEventArgs e) => ClaudeService.OpenCodexLogin();

    private void CodexUpdate_Click(object sender, RoutedEventArgs e) => ClaudeService.OpenCodexUpdate();

    private void CodexCheck_Click(object sender, RoutedEventArgs e)
    {
        ShowCodexModels();
        _ = CheckCodexAsync();
    }

    private static ApiKeyStore CurrentKeys => OpenAiEngine ? ApiKeys.OpenAi : ApiKeys.Anthropic;

    private void ShowClaudeKey()
    {
        bool openAi = OpenAiEngine;
        ClaudeKeyTitle.Text = Loc.T(openAi ? "settings.openai.api" : "settings.claude.api");
        var masked = CurrentKeys.Masked();
        ClaudeKeyStatusText.Text = masked != null
            ? Loc.F(openAi ? "settings.openai.api.saved" : "settings.claude.api.saved", masked)
            : Loc.T(openAi ? "settings.openai.api.none" : "settings.claude.api.none");
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

    // ------------------------------------------------------------------ clé API (6D-3 : Anthropic ; n° 27 : OpenAI)

    private void ClaudeKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SaveClaudeKeyAsync();
    }

    private void ClaudeKeySave_Click(object sender, RoutedEventArgs e) => _ = SaveClaudeKeyAsync();

    private static Task<string?> CheckKeyAsync(bool openAi, string key) =>
        openAi ? ClaudeService.CheckOpenAiKeyAsync(key) : ClaudeService.CheckApiKeyAsync(key);

    /// <summary>Vérifiée auprès du fournisseur (gratuit) avant d'être gardée, chiffrée par Windows. Le champ est vidé tout de suite.</summary>
    private async Task SaveClaudeKeyAsync()
    {
        bool openAi = OpenAiEngine;
        var keys = CurrentKeys;
        var key = ClaudeKeyBox.Password.Trim();
        ClaudeKeyBox.Clear();
        if (!keys.LooksValid(key))
        {
            ShowClaudeKeyMessage(Loc.T(openAi ? "settings.openai.api.invalid" : "settings.claude.api.invalid"));
            return;
        }
        ShowClaudeKeyMessage(Loc.T(openAi ? "settings.openai.api.checking" : "settings.claude.api.checking"));
        var problem = await CheckKeyAsync(openAi, key);
        if (problem != null)
        {
            ShowClaudeKeyMessage(problem);
            return;
        }
        keys.Save(key);
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.ok"));
        if (OpenAiEngine == openAi) ShowClaudeKey();
    }

    private async void ClaudeKeyCheck_Click(object sender, RoutedEventArgs e)
    {
        bool openAi = OpenAiEngine;
        if (CurrentKeys.Load() is not { } key) return;
        ShowClaudeKeyMessage(Loc.T(openAi ? "settings.openai.api.checking" : "settings.claude.api.checking"));
        ShowClaudeKeyMessage(await CheckKeyAsync(openAi, key) ?? Loc.T("settings.claude.api.ok"));
    }

    private void ClaudeKeyDelete_Click(object sender, RoutedEventArgs e)
    {
        bool openAi = OpenAiEngine;
        if (MessageBox.Show(this, Loc.T(openAi ? "settings.openai.api.delete.confirm" : "settings.claude.api.delete.confirm"), AppInfo.Name,
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        CurrentKeys.Delete();
        ShowClaudeKeyMessage(Loc.T("settings.claude.api.deleted"));
        ShowClaudeKey();
    }

    private void ClaudeKeyCreate_Click(object sender, RoutedEventArgs e) => Start(OpenAiEngine ? OpenAiKeysUrl : AnthropicKeysUrl, "");

    private void ShowClaudeKeyMessage(string text)
    {
        ClaudeKeyMessageText.Text = text;
        ClaudeKeyMessageText.Visibility = Visibility.Visible;
    }
}
