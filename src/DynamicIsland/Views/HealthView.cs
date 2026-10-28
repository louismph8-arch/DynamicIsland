using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Health;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Santé » de l'île (6E) : processeur, mémoire, GPU (et sa température NVIDIA), espace libre des disques, et les
/// 4 applis qui utilisent le plus le processeur ou la mémoire, avec un bouton pour les fermer (confirmation ; fermeture
/// forcée seulement après une deuxième confirmation). Mis à jour toutes les 2 s, seulement quand l'onglet est ouvert.
/// </summary>
public sealed class HealthView : UserControl
{
    private const int AppsShown = 3;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SystemStatsService _stats = new();
    private readonly AppUsageMeter _apps = new();
    private readonly UniformGrid _tiles = new() { Columns = 4 };
    private readonly WrapPanel _disks = new() { Margin = new Thickness(Space.S1, Space.S3, 0, 0) };
    private readonly StackPanel _list = new();
    private readonly TextBlock _byCpu, _byMemory;
    private bool _active, _sampling, _sortByMemory;

    public HealthView()
    {
        Width = IslandShape.HealthContentWidth;
        var panel = new StackPanel();
        panel.Children.Add(_tiles);
        panel.Children.Add(_disks);

        var header = new Grid { Margin = new Thickness(Space.S2, Space.S4, Space.S2, Space.S1) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = Loc.T("health.apps"), FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        var sort = new StackPanel { Orientation = Orientation.Horizontal };
        _byCpu = SortButton(Loc.T("health.sort.cpu"), false);
        _byMemory = SortButton(Loc.T("health.sort.memory"), true);
        sort.Children.Add(_byCpu);
        sort.Children.Add(_byMemory);
        Grid.SetColumn(sort, 1);
        header.Children.Add(sort);
        panel.Children.Add(header);
        panel.Children.Add(_list);
        Content = panel;
        _timer.Tick += (_, _) => _ = SampleAsync();
        ShowSort();
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value)
            {
                _stats.Reset();
                _ = SampleAsync();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    /// <summary>« Processeur » / « Mémoire » : le tri de la liste des applis.</summary>
    private TextBlock SortButton(string text, bool memory)
    {
        var button = new TextBlock
        {
            Text = text,
            FontSize = TypeRamp.Caption,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(Space.S4, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Paint.HitTarget,
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            _sortByMemory = memory;
            ShowSort();
            _ = SampleAsync();
            e.Handled = true;
        };
        return button;
    }

    private void ShowSort()
    {
        _byCpu.Opacity = _sortByMemory ? 0.5 : 1;
        _byMemory.Opacity = _sortByMemory ? 1 : 0.5;
    }

    private async Task SampleAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var stats = _stats.Sample();
            int? temperature = HealthService.Nvidia.Temperature();
            var apps = await Task.Run(_apps.Sample); // une à deux centaines de processus : hors du thread de l'interface
            var disks = HealthService.Disks().Select(d => (d.Name.TrimEnd('\\'), HealthService.FreePercent(d), d.AvailableFreeSpace)).ToList();
            Show(stats, temperature, disks, apps);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Santé : relevé", ex);
        }
        finally
        {
            _sampling = false;
        }
    }

    private void Show(SystemStats stats, int? gpuTemperature, List<(string Name, double FreePercent, long Free)> disks, List<AppUsage> apps)
    {
        _tiles.Children.Clear();
        _tiles.Children.Add(Tile(Icons.Cpu, Loc.T("health.cpu"), Percent(stats.Cpu)));
        _tiles.Children.Add(Tile(Icons.Memory, Loc.T("health.memory"), Percent(stats.Ram)));
        _tiles.Children.Add(Tile(Icons.Monitor, Loc.T("health.gpu"), Percent(stats.Gpu)));
        _tiles.Children.Add(Tile(Icons.Thermometer, Loc.T("health.gpu_temperature"), gpuTemperature is { } t ? $"{t} °C" : "—",
            gpuTemperature >= 85 ? Paint.Danger : null));

        _disks.Children.Clear();
        foreach (var (name, free, bytes) in disks.Take(4)) _disks.Children.Add(Disk(name, free, bytes));

        _list.Children.Clear();
        var top = (_sortByMemory ? apps.OrderByDescending(a => a.Memory) : apps.OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Memory)).Take(AppsShown);
        foreach (var app in top) _list.Children.Add(AppRow(app));
    }

    private static string Percent(double? value) => value is { } v ? $"{Math.Round(v)} %" : "—";

    private static SquircleBorder Tile(string glyph, string label, string value, Brush? valueBrush = null)
    {
        var stack = new StackPanel { Margin = new Thickness(Space.S4, Space.S3, Space.S2, Space.S3) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.S, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = label, FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, Margin = new Thickness(Space.S2, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock { Text = value, FontSize = TypeRamp.Title3, FontWeight = FontWeights.SemiBold, Foreground = valueBrush ?? Paint.TextPrimary, Margin = new Thickness(0, Space.S1, 0, 0) });
        return new SquircleBorder { Margin = new Thickness(Space.S1), CornerRadius = Corners.L, Background = Paint.Control, Child = stack };
    }

    private static FrameworkElement Disk(string name, double freePercent, long freeBytes)
    {
        bool low = freePercent < HealthService.LowDiskPercent;
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Space.S2, Space.S1, Space.S5, Space.S1) };
        stack.Children.Add(new TextBlock { Text = Icons.HardDrive, FontFamily = FontStack.Icons, FontSize = IconSize.S, Foreground = low ? Paint.Danger : Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.F("health.disk", name, Math.Round(freePercent), FormatBytes(freeBytes)),
            FontSize = TypeRamp.Caption,
            Foreground = low ? Paint.Danger : Paint.TextSecondary,
            Margin = new Thickness(Space.S2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return stack;
    }

    private FrameworkElement AppRow(AppUsage app)
    {
        var row = new Grid { Height = 26, Margin = new Thickness(Space.S2, 0, Space.S1, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = app.Name, FontSize = TypeRamp.Footnote, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var cpu = new TextBlock { Text = $"{app.Cpu:0.#} %", FontSize = TypeRamp.Footnote, Foreground = _sortByMemory ? Paint.TextSecondary : Paint.TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(cpu, 1);
        row.Children.Add(cpu);
        var memory = new TextBlock { Text = FormatBytes(app.Memory), FontSize = TypeRamp.Footnote, Foreground = _sortByMemory ? Paint.TextPrimary : Paint.TextSecondary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(memory, 2);
        row.Children.Add(memory);
        var close = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = Icons.Close,
            FontSize = IconSize.S,
            ToolTip = Loc.F("health.close", app.Name),
            Margin = new Thickness(Space.S3, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => CloseApp(app);
        Grid.SetColumn(close, 3);
        row.Children.Add(close);
        return row;
    }

    /// <summary>Fermer comme le × de ses fenêtres (elle peut proposer d'enregistrer) ; forcer seulement après une 2e confirmation.</summary>
    private void CloseApp(AppUsage app)
    {
        const MessageBoxOptions onTop = MessageBoxOptions.DefaultDesktopOnly; // l'île ne prend jamais le focus
        if (MessageBox.Show(Loc.F("health.close.confirm", app.Name), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel, onTop) != MessageBoxResult.OK) return;
        if (AppUsageMeter.Close(app.Process) > 0) return;
        if (MessageBox.Show(Loc.F("health.kill.confirm", app.Name), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel, onTop) == MessageBoxResult.OK)
            AppUsageMeter.Kill(app.Process);
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? Loc.F("size.gb", (bytes / (double)(1L << 30)).ToString("0.#", Loc.Culture))
        : Loc.F("size.mb", (bytes / (double)(1L << 20)).ToString("0", Loc.Culture));

    /// <summary>Rendu --snapshot : des valeurs d'exemple (rien n'est mesuré).</summary>
    internal void ShowSample()
    {
        _timer.Stop();
        Show(new SystemStats(23, 61, 47, null, null), 64,
            [("C:", 56, 530L << 30), ("D:", 8, 74L << 30)],
            [
                new AppUsage("chrome", "Google Chrome", 12.4, 2_400L << 20, 18),
                new AppUsage("Discord", "Discord", 3.1, 610L << 20, 6),
                new AppUsage("Spotify", "Spotify", 1.2, 380L << 20, 5),
                new AppUsage("steamwebhelper", "Steam", 0.6, 520L << 20, 9),
            ]);
    }
}
