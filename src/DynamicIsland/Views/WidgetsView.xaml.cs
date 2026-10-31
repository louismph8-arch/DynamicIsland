using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
        ApplySettings();
        UpdateWeather();
    }

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
