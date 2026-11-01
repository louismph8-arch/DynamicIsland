using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Widgets » : météo (Open-Meteo), CPU, RAM, GPU et réseau. Les mesures ne tournent qu'une fois
/// par seconde et seulement quand l'onglet est affiché (<see cref="IsActive"/>).
/// </summary>
public partial class WidgetsView : UserControl
{

    private readonly SystemStatsService _stats = new();
    private readonly WeatherService _weather = WeatherService.Shared;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _isActive;

    public WidgetsView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Sample();
        _weather.Changed += () => Dispatcher.InvokeAsync(UpdateWeather);
        SettingsStore.Changed += ApplySettings;
        // Widgets des potes (7H) : installés / retirés, module arrêté, nouvelles valeurs de leur source web.
        Plugins.PluginStore.Changed += BuildPlugins;
        Plugins.PluginService.DataChanged += () => { if (_isActive) UpdatePlugins(); };
        ModuleGuard.ModuleDisabled += id => { if (id == ModuleGuard.Plugins) BuildPlugins(); };
        ApplySettings();
        UpdateWeather();
    }

    /// <summary>Hauteur de l'île : une rangée de plus s'il y a des widgets des potes à montrer.</summary>
    public bool HasPlugins => PluginScroller.Visibility == Visibility.Visible;

    /// <summary>La rangée des widgets des potes vient d'apparaître ou de disparaître (l'île change de taille).</summary>
    public event Action? HeightChanged;

    /// <summary>Rendus --snapshot : données d'exemple (rien n'est mesuré ni demandé au réseau).</summary>
    public bool UseSampleData
    {
        get => _useSampleData;
        set
        {
            _useSampleData = value;
            _pluginsShown = ""; // redessinés avec les bonnes données
            BuildPlugins();
        }
    }

    private bool _useSampleData;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (value)
            {
                _stats.Reset();
                Sample();
                _timer.Start();
                RefreshWeather();
                UpdatePlugins();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    private void ApplySettings()
    {
        var widgets = SettingsStore.Current.Widgets;
        WeatherTile.Visibility = Show(widgets.Weather);
        CpuTile.Visibility = Show(widgets.Cpu);
        RamTile.Visibility = Show(widgets.Ram);
        GpuTile.Visibility = Show(widgets.Gpu);
        NetworkTile.Visibility = Show(widgets.Network);
        int gauges = (widgets.Cpu ? 1 : 0) + (widgets.Ram ? 1 : 0) + (widgets.Gpu ? 1 : 0);
        bool anyStat = gauges > 0 || widgets.Network;
        StatTiles.Visibility = Show(anyStat);
        // Réseau affiché : une rangée de jauges au-dessus de la tuile réseau (pleine largeur, pour ses deux libellés) ;
        // sinon les jauges sur deux colonnes, comme avant.
        GaugeTiles.Rows = widgets.Network ? 1 : 0;
        GaugeTiles.Columns = widgets.Network ? Math.Max(gauges, 1) : 2;
        GaugeRow.Height = gauges > 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        NetworkRow.Height = widgets.Network ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        // Trois jauges à côté de la météo : tuiles étroites, la valeur passe sous le libellé.
        bool narrow = widgets.Network && gauges == 3 && widgets.Weather;
        foreach (var value in new[] { CpuValue, RamValue, GpuValue })
        {
            value.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            value.VerticalAlignment = narrow ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            value.Margin = narrow ? new Thickness(0, 0, 0, Sizes.Bar + Space.S2) : new Thickness(0);
        }
        WeatherTile.Width = anyStat ? 140 : double.NaN; // seule, la météo prend toute la largeur
        bool any = widgets.Weather || anyStat;
        ContentGrid.Visibility = Show(any);
        EmptyPanel.Visibility = Show(!any);
        if (_isActive) RefreshWeather();
        BuildPlugins();
    }

    private void RefreshWeather()
    {
        var widgets = SettingsStore.Current.Widgets;
        if (widgets.Weather) _weather.RefreshIfNeeded(widgets.City);
    }

    private void UpdateWeather()
    {
        if (_weather.Current is { } report)
        {
            WeatherCity.Text = report.City;
            WeatherSymbol.Text = report.Symbol;
            WeatherTemp.Text = $"{Degrees(report.Temperature)}°";
            WeatherDescription.Text = report.Description;
            WeatherRange.Text = $"↓ {Degrees(report.Min)}°  ↑ {Degrees(report.Max)}°";
        }
        else
        {
            WeatherCity.Text = SettingsStore.Current.Widgets.City;
            WeatherSymbol.Text = "";
            WeatherTemp.Text = "—";
            WeatherDescription.Text = _weather.Error ?? Loc.T("text.loading");
            WeatherRange.Text = "";
        }
    }

    private void Sample()
    {
        try
        {
            var stats = _stats.Sample();
            SetPercent(CpuValue, CpuFill, stats.Cpu);
            SetPercent(RamValue, RamFill, stats.Ram);
            SetPercent(GpuValue, GpuFill, stats.Gpu);
            DownloadValue.Text = SystemStatsService.FormatRate(stats.Download);
            UploadValue.Text = SystemStatsService.FormatRate(stats.Upload);
        }
        catch (Exception ex)
        {
            Log.Error("Widgets : mesure", ex);
        }
        UpdatePlugins();
    }

    // ------------------------------------------------------------------ widgets des potes (7H)

    private readonly List<(Plugins.InstalledWidget Widget, Plugins.RenderedWidget Rendered)> _plugins = [];
    private string _pluginsShown = "";

    /// <summary>Une tuile par widget « dans l'île » (rendu protégé : un widget cassé = case « indisponible »).</summary>
    private void BuildPlugins()
    {
        try
        {
            var states = SettingsStore.Current.Plugins.Items;
            var shown = Plugins.PluginService.Available
                ? Plugins.PluginStore.Installed.Where(w => states.Any(s => s.Id == w.Id && s.InIsland)).ToList()
                : [];
            var signature = string.Join("|", shown.Select(w => $"{w.Id}:{w.Definition.Version}:{w.GetHashCode()}"));
            if (signature == _pluginsShown) return;
            _pluginsShown = signature;
            _plugins.Clear();
            PluginStrip.Children.Clear();
            foreach (var widget in shown)
            {
                var data = UseSampleData ? Plugins.PluginService.Sample(widget.Definition) : Plugins.PluginService.Data(widget.Id);
                var rendered = Plugins.RenderedWidget.Render(widget.Package, data);
                _plugins.Add((widget, rendered));
                var tile = new SquircleBorder
                {
                    Width = widget.Definition.Width,
                    Height = widget.Definition.Height,
                    CornerRadius = Corners.Tile,
                    Background = widget.Definition.Card ? Paint.Surface : Brushes.Transparent,
                    Child = rendered.Root,
                };
                PluginStrip.Children.Add(new Viewbox
                {
                    Height = PluginShape.IslandTile,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, PluginStrip.Children.Count < shown.Count - 1 ? Space.S3 : 0, 0),
                    Child = tile,
                    ToolTip = widget.Definition.Author.Length > 0 ? Loc.F("plugins.byline", widget.Definition.Author, widget.Definition.Version) : widget.Definition.Name,
                });
            }
            bool any = shown.Count > 0;
            if (any == HasPlugins) return;
            PluginScroller.Visibility = Show(any);
            Height = IslandShape.WidgetsContentHeight + (any ? PluginShape.IslandRow : 0);
            HeightChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ModuleGuard.Fail(ModuleGuard.Plugins, ex); // le module s'arrête, l'onglet Widgets continue
        }
    }

    private void UpdatePlugins()
    {
        if (UseSampleData) return;
        foreach (var (widget, rendered) in _plugins)
        {
            Plugins.PluginService.TouchWeb(widget);
            rendered.Update(Plugins.PluginService.Data(widget.Id));
        }
    }

    private void PluginScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        PluginScroller.ScrollToHorizontalOffset(PluginScroller.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private static void SetPercent(TextBlock value, Border fill, double? percent)
    {
        value.Text = percent is { } p ? Loc.F("format.percent", Math.Round(p)) : "…";
        ((ScaleTransform)fill.RenderTransform).ScaleX = (percent ?? 0) / 100;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Arrondi à l'entier : un int n'a pas de « -0 » (contrairement au double).</summary>
    private static int Degrees(double value) => (int)Math.Round(value);
}
