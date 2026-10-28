using DynamicIsland.Settings;

namespace DynamicIsland.Dock;

/// <summary>
/// Bouton rapide « dock Opaline ↔ barre des tâches Windows » (demandé après la 6D-1b) : le même réglage que Paramètres →
/// Général → « Dock » ; coupé, seul le dock s'en va et la barre des tâches normale de Windows revient (l'île et le reste
/// continuent). Dans le menu du dock, le menu de l'icône Opaline, la palette et la télécommande.
/// </summary>
public static class TaskbarSwitch
{
    public static bool DockOn => SettingsStore.Current.Dock.Enabled;

    /// <summary>Texte du bouton selon l'état actuel.</summary>
    public static string Label => Loc.T(DockOn ? "dock.switch.to_windows" : "dock.switch.to_dock");

    public static void Toggle()
    {
        Log.Info(DockOn ? "Barre des tâches Windows remise (dock coupé)" : "Dock Opaline remis");
        SettingsStore.Update(s => s.Dock.Enabled = !s.Dock.Enabled);
    }
}
