using System.Windows;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland;

/// <summary>
/// Onglet « Traduire » : ouverture depuis la palette (« tr … »), le raccourci « Traduire ce que j'ai copié » ou le bouton de
/// l'onglet. Comme pour Claude, l'île reste ouverte pendant la traduction puis quelques secondes pour la lire ; refermée
/// entre-temps → annonce « Traduction prête ».
/// </summary>
public partial class IslandWindow
{
    private bool _translateAsked;

    /// <summary>Onglet affiché et module en marche.</summary>
    private bool TranslateAvailable => !TabStopped(Tab.Translate) && TranslateTabButton.Visibility == Visibility.Visible;

    /// <summary>L'île s'ouvre sur l'onglet Traduire, sans rien traduire.</summary>
    public void OpenTranslate()
    {
        if (!TranslateAvailable) return;
        if (_tab != Tab.Translate) SelectTab(Tab.Translate, animate: _expanded);
        Expand();
    }

    /// <summary>Traduit ce texte (palette) : l'île s'ouvre sur l'onglet et reste ouverte pendant la réponse.</summary>
    public void TranslateText(string text, string? target = null)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.TranslateTab)) return;
        if (!TranslateAvailable)
        {
            // Onglet caché par l'utilisateur : rien n'est demandé (on ne pourrait pas lire la traduction).
            ShowNotice(Icons.Translate, Loc.T("translate.tab_hidden"), () => Settings.SettingsWindow.Open(Settings.SettingsPage.Island), TimeSpan.FromSeconds(4));
            return;
        }
        _translateAsked = true;
        _claudePinnedUntil = DateTime.MaxValue; // même mécanisme que la réponse de Claude
        OpenTranslate();
        TranslateService.Translate(text, target);
    }

    /// <summary>
    /// « Traduire ce que j'ai copié » (bouton de l'onglet ou raccourci) : lit le texte du presse-papiers — jamais une copie
    /// marquée sensible par un gestionnaire de mots de passe — puis le traduit.
    /// </summary>
    public void TranslateClipboard()
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.TranslateTab)) return;
        string? text = null;
        string? problem = null;
        try
        {
            if (SecureClipboard.CurrentIsSensitive()) problem = Loc.T("translate.error.sensitive");
            else if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            problem = Loc.T("translate.error.clipboard_busy");
        }
        if (problem == null && string.IsNullOrWhiteSpace(text)) problem = Loc.T("translate.error.clipboard_empty");
        if (problem != null)
        {
            if (_expanded && _tab == Tab.Translate) TranslateTab.ShowStatus(problem);
            else ShowNotice(Icons.Translate, problem, OpenTranslate, TimeSpan.FromSeconds(3));
            return;
        }
        TranslateText(text!);
    }

    /// <summary>« tr https://… » dans la palette : la page s'ouvre dans Google Traduction (ou un message clair).</summary>
    public void TranslatePage(string address)
    {
        var problem = TranslateService.OpenPage(address);
        ShowNotice(Icons.Globe, problem ?? Loc.T("translate.page.opened"), duration: TimeSpan.FromSeconds(problem == null ? 2.5 : 4));
    }

    /// <summary>Traduction terminée : encore quelques secondes ouverte pour la lire, ou une annonce si l'île s'est refermée.</summary>
    private void OnTranslateChanged()
    {
        if (!_translateAsked || TranslateService.IsBusy || TranslateService.Current is not { Done: true }) return;
        _translateAsked = false;
        if (_expanded && _tab == Tab.Translate)
        {
            if (_claudePinnedUntil == DateTime.MaxValue) _claudePinnedUntil = DateTime.UtcNow + ClaudeReadTime;
            if (!IsCursorOverIsland()) _collapseDelay.Start();
        }
        else
        {
            if (_claudePinnedUntil == DateTime.MaxValue) _claudePinnedUntil = DateTime.MinValue;
            ShowNotice(Icons.Translate, Loc.T("translate.ready"), OpenTranslate);
        }
    }
}
