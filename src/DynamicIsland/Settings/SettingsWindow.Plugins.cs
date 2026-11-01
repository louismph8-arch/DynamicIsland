using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Plugins;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Settings;

/// <summary>
/// Paramètres → Widgets → « Widgets des potes » (7H) : installer un .opalinewidget (aperçu d'abord : nom, auteur, site web
/// demandé), la liste des widgets installés (bureau / île, accord du site, désinstaller) et « Créer un widget ».
/// Les aperçus utilisent des données d'exemple : rien n'est mesuré et aucune requête ne part.
/// </summary>
public partial class SettingsWindow
{
    private (WidgetPackage Package, byte[] Bytes)? _pendingWidget;
    private CheckBox? _pendingAllowWeb;

    /// <summary>Double-clic sur un .opalinewidget (ou fichier reçu par une 2e instance) : aperçu dans la page Widgets.</summary>
    public static void OpenWidgetImport(string path)
    {
        Open(SettingsPage.Widgets);
        _instance?.PreviewWidget(path);
    }

    private void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("plugins.install.dialog"),
            Filter = $"{Loc.F("plugins.file_type", AppInfo.Name)} (*{WidgetPackageReader.Extension})|*{WidgetPackageReader.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
        };
        if (dialog.ShowDialog(this) == true) PreviewWidget(dialog.FileName);
    }

    private void CreatePluginTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = PluginStore.CreateTemplateFolder();
            ShowPluginStatus(Loc.F("plugins.create.done", folder));
        }
        catch (Exception ex)
        {
            Log.Error("Widgets des potes : modèle", ex);
            ShowPluginStatus(Loc.F("plugins.create.failed", ex.Message));
        }
    }

    private void ShowPluginStatus(string? text)
    {
        PluginStatusText.Text = text ?? "";
        PluginStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ aperçu avant d'installer

    /// <summary>Lit et vérifie le fichier : refusé = message clair, rien n'est installé ; sinon aperçu + confirmation.</summary>
    private void PreviewWidget(string path)
    {
        _pendingWidget = null;
        PluginPreviewPanel.Children.Clear();
        ShowPluginStatus(null);
        try
        {
            var package = WidgetPackageReader.Read(path, out var bytes);
            _pendingWidget = (package, bytes);
            PluginPreviewPanel.Children.Add(PreviewCard(package));
        }
        catch (Exception ex) when (ex is WidgetPackageException or System.IO.IOException or UnauthorizedAccessException)
        {
            ShowPluginStatus(Loc.F("plugins.invalid", ex.Message));
        }
        Dispatcher.BeginInvoke(() => PluginsGroupTitle.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement PreviewCard(WidgetPackage package)
    {
        var definition = package.Definition;
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.Children.Add(Thumbnail(package, PluginShape.InstallPreview));
        var about = new StackPanel { Margin = new Thickness(Space.S8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        about.Children.Add(new TextBlock { Text = definition.Name, Style = Res("CardTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
        about.Children.Add(new TextBlock { Text = Byline(definition), Style = Res("CardDescription") });
        if (definition.Description.Length > 0) about.Children.Add(new TextBlock { Text = definition.Description, Style = Res("CardDescription") });
        about.Children.Add(new TextBlock
        {
            Text = Loc.F("plugins.preview.summary", definition.Elements().Count(), package.Images.Count),
            Style = Res("CardDescription"),
        });
        if (PluginStore.Find(definition.Id) is { } existing)
            about.Children.Add(new TextBlock { Text = Loc.F("plugins.preview.replace", existing.Definition.Version), Style = Res("CardDescription"), Foreground = Paint.Timer });
        Grid.SetColumn(about, 1);
        top.Children.Add(about);
        stack.Children.Add(top);

        _pendingAllowWeb = null;
        if (definition.Source is { } source)
        {
            var web = new SquircleBorder
            {
                Background = Paint.Card,
                CornerRadius = Corners.L,
                Padding = new Thickness(Space.S6, Space.S5, Space.S6, Space.S5),
                Margin = new Thickness(0, Space.S6, 0, 0),
            };
            var lines = new StackPanel();
            lines.Children.Add(new TextBlock { Text = Loc.F("plugins.web.asks", source.Host), Style = Res("CardTitle") });
            lines.Children.Add(new TextBlock { Text = Loc.F("plugins.web.explain", source.Url.GetLeftPart(UriPartial.Path), source.EveryMinutes), Style = Res("CardDescription") });
            // L'accord se donne en l'allumant, jamais d'office.
            var allow = Switch(Loc.F("plugins.web.allow", source.Host), false, _ => { });
            allow.Margin = new Thickness(0, Space.S5, 0, 0);
            _pendingAllowWeb = (CheckBox)((Panel)allow).Children[0];
            lines.Children.Add(allow);
            web.Child = lines;
            stack.Children.Add(web);
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = Loc.T("plugins.web.none"), Style = Res("CardDescription"), Margin = new Thickness(0, Space.S5, 0, 0) });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S6, 0, 0) };
        var install = new Button { Style = Res("SettingsButton"), Margin = new Thickness(0), Content = Loc.T("plugins.install.confirm") };
        install.SetResourceReference(BackgroundProperty, Services.AppearanceService.AccentGradientKey);
        install.Click += (_, _) => ConfirmWidget();
        buttons.Children.Add(install);
        var cancel = new Button { Style = Res("SettingsButton"), Margin = new Thickness(Space.S4, 0, 0, 0), Content = Loc.T("settings.annuler") };
        cancel.Click += (_, _) =>
        {
            _pendingWidget = null;
            PluginPreviewPanel.Children.Clear();
        };
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        return Card(stack);
    }

    private void ConfirmWidget()
    {
        if (_pendingWidget is not { } pending) return;
        try
        {
            PluginStore.Install(pending.Package, pending.Bytes, _pendingAllowWeb?.IsChecked == true);
            ShowPluginStatus(Loc.F("plugins.installed", pending.Package.Definition.Name));
            (Application.Current as App)?.Notify(Icons.Check, Loc.F("plugins.installed", pending.Package.Definition.Name));
        }
        catch (Exception ex)
        {
            Log.Error("Widgets des potes : installation", ex);
            ShowPluginStatus(Loc.F("plugins.install.failed", ex.Message));
        }
        _pendingWidget = null;
        PluginPreviewPanel.Children.Clear();
    }

    // ------------------------------------------------------------------ widgets installés

    private void ShowPlugins()
    {
        try
        {
            PluginList.Children.Clear();
            var installed = PluginStore.Installed;
            if (installed.Count == 0 && PluginStore.Broken.Count == 0)
            {
                PluginList.Children.Add(new TextBlock { Text = Loc.T("plugins.empty"), Style = Res("CardDescription"), Margin = new Thickness(Space.S2, 0, 0, Space.S4) });
                return;
            }
            foreach (var widget in installed) PluginList.Children.Add(InstalledCard(widget));
            foreach (var id in PluginStore.Broken)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = id, Style = Res("CardTitle") });
                text.Children.Add(new TextBlock { Text = Loc.T("plugins.broken"), Style = Res("CardDescription") });
                row.Children.Add(text);
                var remove = UninstallButton(id, id);
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                PluginList.Children.Add(Card(row));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Widgets des potes : liste", ex);
        }
    }

    private FrameworkElement InstalledCard(InstalledWidget widget)
    {
        var definition = widget.Definition;
        var state = SettingsStore.Current.Plugins.Items.FirstOrDefault(i => i.Id == widget.Id) ?? new PluginState { Id = widget.Id };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Thumbnail(widget.Package, PluginShape.SettingsPreview));

        var about = new StackPanel { Margin = new Thickness(Space.S7, 0, Space.S4, 0), VerticalAlignment = VerticalAlignment.Center };
        about.Children.Add(new TextBlock { Text = definition.Name, Style = Res("CardTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
        about.Children.Add(new TextBlock { Text = Byline(definition), Style = Res("CardDescription") });
        var places = new WrapPanel { Margin = new Thickness(0, Space.S4, 0, 0) };
        places.Children.Add(Switch(Loc.T("plugins.on_desktop"), state.OnDesktop, on => SettingsStore.Update(s => s.Plugins.For(widget.Id).OnDesktop = on)));
        places.Children.Add(Switch(Loc.T("plugins.in_island"), state.InIsland, on => SettingsStore.Update(s => s.Plugins.For(widget.Id).InIsland = on)));
        about.Children.Add(places);

        if (definition.Source is { } source)
        {
            bool allowed = WebSource.IsAllowed(definition, state);
            var web = new Grid { Margin = new Thickness(0, Space.S4, 0, 0) };
            web.ColumnDefinitions.Add(new ColumnDefinition());
            web.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            web.Children.Add(new TextBlock
            {
                Text = Loc.F(allowed ? "plugins.web.allowed" : "plugins.web.blocked", source.Host, source.EveryMinutes),
                Style = Res("CardDescription"),
                Foreground = allowed ? Paint.TextSecondary : Paint.Timer,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var toggle = new Button
            {
                Style = Res("SettingsButton"),
                Margin = new Thickness(Space.S4, 0, 0, 0),
                Content = Loc.T(allowed ? "plugins.web.revoke" : "plugins.web.grant"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Click += (_, _) =>
            {
                if (!allowed && MessageBox.Show(this, Loc.F("plugins.web.confirm", definition.Name, source.Host, source.EveryMinutes),
                        Loc.T("plugins.web.confirm.title"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                var host = allowed ? null : source.Host;
                SettingsStore.Update(s => s.Plugins.For(widget.Id).AllowedHost = host);
            };
            Grid.SetColumn(toggle, 1);
            web.Children.Add(toggle);
            about.Children.Add(web);
        }
        Grid.SetColumn(about, 1);
        grid.Children.Add(about);

        var remove = UninstallButton(widget.Id, definition.Name);
        remove.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);
        return Card(grid);
    }

    private Button UninstallButton(string id, string name)
    {
        var button = new Button { Style = Res("SettingsButton"), Content = Loc.T("plugins.uninstall"), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Loc.F("plugins.uninstall.confirm", name), Loc.T("plugins.uninstall"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                PluginStore.Uninstall(id);
            }
            catch (Exception ex)
            {
                Log.Error("Widgets des potes : désinstallation", ex);
                ShowPluginStatus(Loc.F("plugins.uninstall.failed", ex.Message));
            }
        };
        return button;
    }

    /// <summary>Interrupteur avec son libellé (« Sur le bureau », « Dans l'île »).</summary>
    private FrameworkElement Switch(string label, bool on, Action<bool> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, Space.S8, Space.S2) };
        var toggle = new CheckBox { Style = Res("SwitchToggle"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) => changed(toggle.IsChecked == true);
        panel.Children.Add(toggle);
        panel.Children.Add(new TextBlock { Text = label, FontSize = TypeRamp.Body, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, 0, 0) });
        return panel;
    }

    /// <summary>Aperçu du widget (données d'exemple) sur la carte de l'île, à la hauteur demandée.</summary>
    private static FrameworkElement Thumbnail(WidgetPackage package, double height)
    {
        var definition = package.Definition;
        var rendered = RenderedWidget.Render(package, PluginService.Sample(definition));
        var tile = new SquircleBorder
        {
            Width = definition.Width,
            Height = definition.Height,
            CornerRadius = Corners.Tile,
            Background = definition.Card ? Paint.Frozen(Swatch.WithAlpha(Services.AppearanceService.SurfaceBase, Desk.CardAlpha)) : Paint.Surface,
            Child = rendered.Root,
        };
        return new Viewbox
        {
            Height = height,
            MaxWidth = height * PluginShape.PreviewMaxRatio,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            Child = tile,
        };
    }

    private static string Byline(WidgetDefinition definition) =>
        definition.Author.Length > 0 ? Loc.F("plugins.byline", definition.Author, definition.Version) : Loc.F("plugins.version", definition.Version);

    private FrameworkElement Card(FrameworkElement content) => new SquircleBorder { Style = Res("SettingCard"), Child = content };

    private Style Res(string key) => (Style)FindResource(key);

    /// <summary>Rendu --snapshot : deux widgets d'exemple installés en mémoire + l'aperçu d'installation du compteur Bitcoin.</summary>
    internal void ShowPluginsSample()
    {
        PluginSnapshots.UseSamples();
        ShowPlugins();
        if (PluginStore.Installed.FirstOrDefault(w => w.Definition.Source != null) is { } web)
        {
            _pendingWidget = (web.Package, []);
            PluginPreviewPanel.Children.Clear();
            PluginPreviewPanel.Children.Add(PreviewCard(web.Package));
        }
    }
}
