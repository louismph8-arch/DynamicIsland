using System.IO;
using System.Windows.Interop;
using DynamicIsland.Dock;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du bug « double dock » : le dock d'un rendu / d'une mesure reste HORS écran même après un changement de réglage
/// (avant : PositionWindow le posait en bas de l'écran principal, devant le vrai dock), et le garde-fou des modes développeur
/// ne laisse aucune fenêtre visible sur un écran. Plus : la fenêtre des paramètres n'a pas les boutons de légende de Windows.
/// </summary>
internal static partial class SelfTest
{
    private static void DeveloperWindows(List<string> lines)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Opaline-dev", "selftest-dock");
        Directory.CreateDirectory(folder);
        // Fermer la dernière fenêtre arrêterait l'app (OnLastWindowClose) avant la fin de l'auto-test.
        var app = System.Windows.Application.Current;
        var mode = app.ShutdownMode;
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var dock = new DockWindow { SnapshotFolder = folder, GlassBenchOnly = true };
        try
        {
            dock.Show();
            bool before = dock.Left <= -20000;
            // Même chose que --glass-bench : un réglage change → ApplyAppearance → PositionWindow.
            bool glass = SettingsStore.Current.Appearance.LiquidGlass;
            SettingsStore.Update(s => s.Appearance.LiquidGlass = !glass);
            SettingsStore.Update(s => s.Appearance.LiquidGlass = glass);
            bool after = dock.Left <= -20000;
            Check(lines, before && after, $"dock d'un rendu : hors écran avant et après un changement de réglage (Left = {dock.Left:0})");
            Check(lines, !DeveloperWindowGuard.ShownOnScreen(dock), "dock d'un rendu : jamais visible sur un écran");
        }
        finally
        {
            dock.Close();
        }
        try
        {
            Check(lines, SettingsWindow.SelfTestCaption(), "paramètres : pas de boutons réduire / agrandir / fermer de Windows (WS_SYSMENU retiré)");
        }
        finally
        {
            app.ShutdownMode = mode;
        }
    }
}
