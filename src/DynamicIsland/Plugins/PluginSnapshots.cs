using System.IO;
using DynamicIsland.DesktopWidgets;
using DynamicIsland.Settings;

namespace DynamicIsland.Plugins;

/// <summary>
/// Mode --snapshot : les deux widgets d'exemple (embarqués dans l'app) installés EN MÉMOIRE seulement — le dossier Widgets
/// n'est ni lu ni écrit, les réglages sont en lecture seule — et rendus avec des données d'exemple (aucune requête web).
/// </summary>
internal static class PluginSnapshots
{
    private static bool _used;

    public static void UseSamples()
    {
        if (_used) return;
        _used = true;
        PluginStore.UseSample(PluginStore.Examples());
        foreach (var widget in PluginStore.Installed)
        {
            var state = SettingsStore.Current.Plugins.For(widget.Id); // réglages en mémoire (jamais écrits en --snapshot)
            state.InIsland = true;
            state.OnDesktop = widget.Definition.Source == null;
        }
    }

    /// <summary>Chaque exemple dans une vraie fenêtre de widget du bureau, hors écran : 10-widget-pote-&lt;id&gt;.png.</summary>
    public static async Task RenderAsync(string folder)
    {
        UseSamples();
        foreach (var widget in PluginStore.Installed)
        {
            var rendered = RenderedWidget.Render(widget.Package, PluginService.Sample(widget.Definition));
            var window = new DesktopWidgetWindow(new PluginDesktopWidget(widget.Definition, rendered)) { Left = -30000, Top = 0 };
            window.Place(-30000, 0, 1);
            window.Show();
            await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, $"10-widget-pote-{widget.Id}.png"));
            window.Widget.Detach();
            window.Close();
        }
    }
}
