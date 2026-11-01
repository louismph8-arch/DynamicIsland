using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.DesktopWidgets;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Plugins;

/// <summary>
/// Widgets des potes qui tournent (module « plugins ») : fenêtres du bureau (les mêmes que les widgets de la 5E : glisser,
/// taille, grille, estompées quand une appli est devant, cachées en jeu), données remises à jour seulement quand un widget
/// est affiché (toutes les 2 s s'il montre des mesures, 15 s s'il ne montre que l'heure) et source web lue au plus toutes
/// les 5 min, seulement si son site est autorisé. Toute erreur arrête le module pour la session (jamais l'app).
/// </summary>
public static class PluginService
{
    private static readonly LiveWidgetData Live = new();
    private static readonly Dictionary<string, (DesktopWidgetWindow Window, RenderedWidget Widget, InstalledWidget Source)> Desktop = [];
    private static readonly Dictionary<string, WebState> Web = [];
    private static readonly DispatcherTimer Timer = new() { Interval = PluginShape.SlowRefresh };
    private static bool _started;

    private sealed class WebState
    {
        public IReadOnlyDictionary<string, string>? Values;
        public DateTime Next;
        public bool Busy;
        public bool Logged;
    }

    /// <summary>De nouvelles valeurs de source web sont arrivées (l'onglet Widgets de l'île se remet à jour).</summary>
    public static event Action? DataChanged;

    /// <summary>Le module peut tourner : pas arrêté après une erreur, pas en mode sans échec.</summary>
    public static bool Available => !ModuleGuard.IsDisabled(ModuleGuard.Plugins) && !App.SafeMode;

    /// <summary>Données d'un widget : liste blanche + champs de SA source web (si elle est autorisée et déjà lue).</summary>
    public static IWidgetData Data(string id) =>
        new ScopedWidgetData(Live, Web.TryGetValue(id, out var web) ? web.Values : null, PluginStore.Find(id)?.Definition.Source);

    /// <summary>Données d'exemple (aperçus, rendus) : rien n'est mesuré, aucune requête.</summary>
    public static IWidgetData Sample(WidgetDefinition definition) =>
        new ScopedWidgetData(SampleWidgetData.Instance, SampleWidgetData.Web, definition.Source);

    public static void Start(MediaService media)
    {
        Live.Media = media;
        media.Changed += () => Guard(() => UpdateDesktop(onlyMusic: true));
        Timer.Tick += (_, _) => Guard(() => UpdateDesktop(onlyMusic: false));
        PluginStore.Changed += Apply;
        SettingsStore.Changed += Apply;
        GamingMode.Changed += Apply;
        _started = true;
        Apply();
    }

    public static void Stop()
    {
        _started = false;
        Timer.Stop();
        foreach (var id in Desktop.Keys.ToList()) CloseDesktop(id);
        Web.Clear();
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ModuleGuard.Fail(ModuleGuard.Plugins, ex);
        }
    }

    private static void Apply() => Guard(() =>
    {
        if (!_started) return;
        bool on = Available && !SettingsStore.IsReadOnly;
        var settings = SettingsStore.Current.Plugins;
        var installed = PluginStore.Installed;

        foreach (var id in Desktop.Keys.ToList())
        {
            var widget = installed.FirstOrDefault(w => w.Id == id);
            bool keep = on && widget != null && ReferenceEquals(widget, Desktop[id].Source) && settings.Items.Any(i => i.Id == id && i.OnDesktop);
            if (!keep) CloseDesktop(id);
        }
        int index = 0;
        foreach (var widget in installed)
        {
            var state = settings.Items.FirstOrDefault(i => i.Id == widget.Id);
            // Accord retiré (ou site changé) : les valeurs déjà lues sont oubliées tout de suite.
            if (!WebSource.IsAllowed(widget.Definition, state)) Web.Remove(widget.Id);
            if (!on || state is not { OnDesktop: true }) continue;
            var (left, top) = Position(widget, state, index++);
            if (!Desktop.TryGetValue(widget.Id, out var shown)) CreateDesktop(widget, state, left, top);
            else
            {
                shown.Window.Widget.ApplyLook();
                if (Math.Abs(shown.Window.Left - left) > 1 || Math.Abs(shown.Window.Top - top) > 1 || Math.Abs(shown.Window.Scale - state.Scale) > 0.01)
                    shown.Window.Place(left, top, state.Scale);
            }
        }

        bool gaming = GamingMode.IsActive;
        foreach (var (window, _, _) in Desktop.Values) window.Visibility = gaming ? Visibility.Hidden : Visibility.Visible;
        if (Desktop.Count > 0 && !gaming)
        {
            Timer.Interval = Desktop.Values.Any(d => NeedsFast(d.Source.Definition)) ? PluginShape.FastRefresh : PluginShape.SlowRefresh;
            Timer.Start();
            UpdateDesktop(onlyMusic: false);
        }
        else Timer.Stop();
    });

    private static void CreateDesktop(InstalledWidget widget, PluginState state, double left, double top)
    {
        var rendered = RenderedWidget.Render(widget.Package, Data(widget.Id));
        var window = new DesktopWidgetWindow(new PluginDesktopWidget(widget.Definition, rendered));
        window.Place(left, top, state.Scale);
        var id = widget.Id;
        window.Placed += placed => Guard(() =>
        {
            double l = Math.Round(placed.Left), t = Math.Round(placed.Top), scale = Math.Round(placed.Scale, 2);
            SettingsStore.Update(s =>
            {
                var item = s.Plugins.For(id);
                item.Left = l;
                item.Top = t;
                item.Scale = scale;
            });
        });
        window.ContextMenu = Menu(id);
        window.Show();
        Desktop[id] = (window, rendered, widget);
        DesktopWidgetsService.Attach(window);
    }

    private static void CloseDesktop(string id)
    {
        if (!Desktop.Remove(id, out var shown)) return;
        DesktopWidgetsService.Detach(shown.Window);
        shown.Window.Widget.Detach();
        shown.Window.Close();
    }

    private static void UpdateDesktop(bool onlyMusic)
    {
        foreach (var (id, (_, widget, source)) in Desktop)
        {
            if (onlyMusic && !source.Definition.UsedKeys().Any(k => k.StartsWith("music.", StringComparison.Ordinal))) continue;
            TouchWeb(source);
            widget.Update(Data(id));
        }
    }

    /// <summary>Widget affiché : sa source web est relue si elle est autorisée et que le délai est passé.</summary>
    public static void TouchWeb(InstalledWidget widget)
    {
        var definition = widget.Definition;
        if (definition.Source is not { } source || !Available) return;
        var state = SettingsStore.Current.Plugins.Items.FirstOrDefault(i => i.Id == widget.Id);
        if (!WebSource.IsAllowed(definition, state)) return;
        if (!Web.TryGetValue(widget.Id, out var web)) Web[widget.Id] = web = new WebState();
        if (web.Busy || DateTime.UtcNow < web.Next) return;
        web.Busy = true;
        web.Next = DateTime.UtcNow.AddMinutes(source.EveryMinutes);
        _ = FetchAsync(widget, state!, web);
    }

    private static async Task FetchAsync(InstalledWidget widget, PluginState state, WebState web)
    {
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (await WebSource.FetchAsync(widget.Definition, state, cancel.Token) is { } values)
            {
                web.Values = values;
                web.Logged = false;
            }
        }
        catch (Exception ex)
        {
            if (!web.Logged) Log.Error($"Widget des potes « {widget.Id} » : source web {widget.Definition.Source?.Host}", ex);
            web.Logged = true; // une seule ligne tant que ça ne remarche pas
        }
        finally
        {
            web.Busy = false;
        }
        Guard(() =>
        {
            if (Desktop.TryGetValue(widget.Id, out var shown)) shown.Widget.Update(Data(widget.Id));
            DataChanged?.Invoke();
        });
    }

    /// <summary>Mesures qui bougent (processeur, réseau, musique, secondes) : mise à jour toutes les 2 s, sinon 15 s.</summary>
    internal static bool NeedsFast(WidgetDefinition definition)
    {
        string[] fast = ["cpu", "ram", "gpu", "gpu.temp", "net.down", "net.up"];
        foreach (var element in definition.Elements())
        {
            foreach (var text in new[] { element.Text, element.Icon })
                if (text != null && WidgetTemplate.Placeholders(text).Any(p => fast.Contains(p.Key) || p.Key.StartsWith("music.", StringComparison.Ordinal)
                                                                          || (p.Key is "time" && p.Format?.Contains('s') == true)))
                    return true;
            if (element.Value != null && fast.Contains(element.Value)) return true;
        }
        return false;
    }

    /// <summary>Place enregistrée (si elle tombe sur un écran), sinon au milieu de l'écran principal, en cascade.</summary>
    private static (double Left, double Top) Position(InstalledWidget widget, PluginState state, int index)
    {
        var (_, work, scale) = ScreenInfo.Primary();
        if (state.Left is double left && state.Top is double top
            && System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Contains((int)((left + Desk.Grid) * scale), (int)((top + Desk.Grid) * scale))))
            return (left, top);
        double width = widget.Definition.Width * state.Scale, height = widget.Definition.Height * state.Scale;
        double offset = index * PluginShape.DesktopCascade;
        return (Math.Round((work.Left + work.Right) / 2 / scale - width / 2 + offset),
                Math.Round((work.Top + work.Bottom) / 2 / scale - height / 2 + offset));
    }

    private static ContextMenu Menu(string id)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Entry(Loc.T("plugins.menu.remove_desktop"), () => SettingsStore.Update(s => s.Plugins.For(id).OnDesktop = false)));
        menu.Items.Add(new Separator());
        var locked = new MenuItem { Header = Loc.T("settings.verrouiller_widgets"), IsCheckable = true };
        menu.Opened += (_, _) => locked.IsChecked = SettingsStore.Current.DesktopWidgets.Locked;
        locked.Click += (_, _) => SettingsStore.Update(s => s.DesktopWidgets.Locked = locked.IsChecked);
        menu.Items.Add(locked);
        menu.Items.Add(Entry(Loc.T("widget.menu.settings"), () => SettingsWindow.Open(SettingsPage.Widgets)));
        return menu;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var entry = new MenuItem { Header = header };
        entry.Click += (_, _) => Guard(action);
        return entry;
    }
}

/// <summary>Widget des potes dans une fenêtre du bureau : la carte de l'île (ou rien) + le rendu protégé.</summary>
public sealed class PluginDesktopWidget : DesktopWidget
{
    public PluginDesktopWidget(WidgetDefinition definition, RenderedWidget rendered) : base(new Size(definition.Width, definition.Height), definition.Card)
    {
        Children.Add(rendered.Root);
    }

    /// <summary>Jamais utilisé : la place d'un widget des potes est enregistrée par <see cref="PluginService"/>.</summary>
    public override DesktopWidgetKind Kind => DesktopWidgetKind.Clock;
}
