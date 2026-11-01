using System.Windows.Interop;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using DynamicIsland.Vault;

namespace DynamicIsland;

/// <summary>Onglet « Presse-papiers » : ouverture depuis la palette et proposition « Garder dans le coffre ».</summary>
public partial class IslandWindow
{
    /// <summary>Palette « presse-papiers » / « mot de passe … » : l'île s'ouvre sur l'historique ou sur le coffre.</summary>
    public void OpenClipboard(bool vault)
    {
        if (TabStopped(Tab.Clipboard)) return;
        ClipboardTab.ShowVault(vault);
        ShowTool(Tab.Clipboard); // O6 : outil ouvert même si la grille est affichée
        Expand();
    }

    /// <summary>Fenêtre de l'île : Windows Hello s'y rattache (le coffre se déverrouille depuis l'île).</summary>
    internal IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Un gestionnaire de mots de passe vient de copier : rien dans l'historique, mais une annonce discrète propose de le
    /// garder dans le coffre (le mot de passe reste en mémoire 60 s au plus, puis il est oublié).
    /// </summary>
    internal bool OfferSecret() => !ClipboardHistory.Store.HasSecret || ShowNotice(Icons.Key, Loc.T("clipboard.offer"), () =>
    {
        if (ClipboardHistory.Store.TakeSecret() is { } secret) VaultEditorWindow.Open(null, secret);
    }, TimeSpan.FromSeconds(8), Loc.T("clipboard.offer.notice"));
}
