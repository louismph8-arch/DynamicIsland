namespace DynamicIsland;

/// <summary>Onglet « Notes » : ouverture depuis la palette (« notes »).</summary>
public partial class IslandWindow
{
    /// <summary>L'île s'ouvre sur les notes (onglet caché par l'utilisateur ou module arrêté : rien).</summary>
    public void OpenNotes()
    {
        if (TabStopped(Tab.Notes) || NotesTabButton.Visibility != System.Windows.Visibility.Visible) return;
        if (_tab != Tab.Notes) SelectTab(Tab.Notes, animate: _expanded);
        Expand();
    }
}
