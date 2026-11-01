using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuile vivante « Moniteur » (refonte O6) : anneaux CPU et RAM (+ GPU et débits réseau en L), valeur en chiffres fins au centre.
/// Les anneaux suivent la valeur avec un ressort et « respirent » doucement (épaisseur et lueur, période
/// <see cref="Opal.BreathPeriod"/>) ; rouges au-delà de <see cref="LiveTileShape.Alert"/>. Clic = outil Santé.
/// <list type="bullet">
/// <item>S : deux petits anneaux (CPU, RAM) et leur nom ;</item>
/// <item>W : icône + nom, puis deux anneaux avec leur nom à droite ;</item>
/// <item>L : icône + nom, trois anneaux (CPU, RAM, GPU), débits descendant / montant en bas.</item>
/// </list>
/// <para>Performance : mesures (<see cref="SystemStatsService"/>, comme l'onglet Santé) toutes les 2 s et image ≤ 25 / s SEULEMENT
/// tant que la tuile est affichée (<see cref="TileFace.IsLive"/>) ; sinon aucune minuterie et la requête du GPU est refermée.
/// « Réduire les animations », verre coupé ou mode Gaming : valeurs posées sans ressort ni respiration, aucune minuterie.</para>
/// </summary>
public sealed class MonitorTileFace : TileFace
{
    private readonly Gauge _cpu, _ram;
    private readonly Gauge? _gpu;
    private readonly TextBlock? _down, _up;
    private readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = LiveTileShape.Frame };
    private readonly Stopwatch _clock = new();
    private SystemStatsService? _stats;
    private DateTime _lastSample = DateTime.MinValue;
    private double _lastTick;
    private bool _cpuKnown;

    /// <summary>Un anneau et ses textes : valeur au centre, nom.</summary>
    private sealed class Gauge(TileRing ring, TextBlock value, TextBlock label, double phase)
    {
        public TileRing Ring { get; } = ring;
        public TextBlock Value { get; } = value;
        public TextBlock Label { get; } = label;
        public Spring Spring { get; } = new(0);
        /// <summary>Décalage de la respiration (fraction de période) : les anneaux ne respirent pas tous ensemble.</summary>
        public double Phase { get; } = phase;
        public double? Shown { get; set; }
    }

    public MonitorTileFace(TileDefinition definition, TileSize size) : base(definition, size, null)
    {
        _frame.Tick += (_, _) => Tick();
        var layout = new Grid();
        Child = layout;
        switch (size)
        {
            case TileSize.S:
            {
                (_cpu, _ram) = (NewGauge("tiles.monitor.cpu", LiveTileShape.RingS, LiveTileShape.RingStrokeS, TypeRamp.Caption, 0),
                    NewGauge("tiles.monitor.ram", LiveTileShape.RingS, LiveTileShape.RingStrokeS, TypeRamp.Caption, 0.33));
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rings = new UniformGrid2(2) { Margin = new Thickness(0, Space.S1, 0, 0) };
                rings.Add(Stacked(_cpu));
                rings.Add(Stacked(_ram));
                layout.Children.Add(rings.Panel);
                var title = Title();
                Grid.SetRow(title, 2);
                layout.Children.Add(title);
                break;
            }
            case TileSize.W:
            {
                (_cpu, _ram) = (NewGauge("tiles.monitor.cpu", LiveTileShape.RingW, LiveTileShape.RingStrokeW, TypeRamp.NumberTile, 0),
                    NewGauge("tiles.monitor.ram", LiveTileShape.RingW, LiveTileShape.RingStrokeW, TypeRamp.NumberTile, 0.33));
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                layout.Children.Add(Header());
                var rings = new UniformGrid2(2) { VerticalAlignment = VerticalAlignment.Bottom };
                rings.Add(Beside(_cpu));
                rings.Add(Beside(_ram));
                Grid.SetRow(rings.Panel, 1);
                layout.Children.Add(rings.Panel);
                break;
            }
            default:
            {
                (_cpu, _ram) = (NewGauge("tiles.monitor.cpu", LiveTileShape.RingL, LiveTileShape.RingStrokeL, TypeRamp.NumberTile, 0),
                    NewGauge("tiles.monitor.ram", LiveTileShape.RingL, LiveTileShape.RingStrokeL, TypeRamp.NumberTile, 0.33));
                _gpu = NewGauge("tiles.monitor.gpu", LiveTileShape.RingL, LiveTileShape.RingStrokeL, TypeRamp.NumberTile, 0.66);
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                layout.Children.Add(Header());
                var rings = new UniformGrid2(3) { Margin = new Thickness(0, Space.S5, 0, 0) };
                rings.Add(Stacked(_cpu));
                rings.Add(Stacked(_ram));
                rings.Add(Stacked(_gpu));
                Grid.SetRow(rings.Panel, 1);
                layout.Children.Add(rings.Panel);
                var network = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                network.Children.Add(new Border { Height = Stroke.Hairline, Margin = new Thickness(0, 0, 0, Space.S3) }.WithInk(Border.BackgroundProperty, InkRole.Divider));
                (_down, var downRow) = RateRow(Icons.ArrowDown, "tiles.monitor.down");
                (_up, var upRow) = RateRow(Icons.ArrowUp, "tiles.monitor.up");
                upRow.Margin = new Thickness(0, Space.S1, 0, 0);
                network.Children.Add(downRow);
                network.Children.Add(upRow);
                Grid.SetRow(network, 2);
                layout.Children.Add(network);
                break;
            }
        }
        Show(null);
    }

    // ------------------------------------------------------------------ construction

    private static Gauge NewGauge(string labelKey, double diameter, double stroke, double valueSize, double phase)
    {
        var value = Numerals.Apply(new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, valueSize);
        value.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);
        var label = new TextBlock { Text = Loc.T(labelKey), FontSize = TypeRamp.Caption2, FontWeight = FontWeights.SemiBold };
        label.WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
        return new Gauge(new TileRing(diameter, stroke), value, label, phase);
    }

    /// <summary>Anneau avec la valeur au centre.</summary>
    private static Grid RingWithValue(Gauge gauge)
    {
        var cell = new Grid { Width = gauge.Ring.Width, Height = gauge.Ring.Height };
        cell.Children.Add(gauge.Ring);
        cell.Children.Add(gauge.Value);
        return cell;
    }

    /// <summary>Anneau, puis son nom centré dessous (S, L).</summary>
    private static StackPanel Stacked(Gauge gauge)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(RingWithValue(gauge));
        gauge.Label.HorizontalAlignment = HorizontalAlignment.Center;
        gauge.Label.Margin = new Thickness(0, Space.S2, 0, 0);
        stack.Children.Add(gauge.Label);
        return stack;
    }

    /// <summary>Anneau, puis son nom à droite (W).</summary>
    private static StackPanel Beside(Gauge gauge)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(RingWithValue(gauge));
        gauge.Label.VerticalAlignment = VerticalAlignment.Center;
        gauge.Label.Margin = new Thickness(Space.S4, 0, 0, 0);
        stack.Children.Add(gauge.Label);
        return stack;
    }

    private TextBlock Title() => new TextBlock
    {
        Text = Definition.Name,
        FontSize = TypeRamp.Caption,
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
    }.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);

    /// <summary>En-tête W / L : l'icône (allumée tant que la tuile mesure) et le nom.</summary>
    private StackPanel Header()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new IconChip
        {
            Glyph = Definition.Glyph, GlyphSize = IconSize.L, Width = Sizes.IconChip, Height = Sizes.IconChip, IsActive = true,
            Tint = Interaction.ActiveTintLight, VerticalAlignment = VerticalAlignment.Center,
        });
        var title = Title();
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(Space.S3, 0, 0, 0);
        header.Children.Add(title);
        return header;
    }

    /// <summary>Ligne de débit (L) : flèche, nom, valeur à droite en chiffres.</summary>
    private static (TextBlock Value, Grid Row) RateRow(string glyph, string labelKey)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.S, VerticalAlignment = VerticalAlignment.Center }
            .WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
        var label = new TextBlock { Text = Loc.T(labelKey), FontSize = TypeRamp.Caption2, Margin = new Thickness(Space.S2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
        var value = Numerals.Apply(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TypeRamp.Caption);
        value.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(value, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(value);
        return (value, row);
    }

    // ------------------------------------------------------------------ vie de la tuile

    /// <summary>Mesures et respiration en cours (--self-test : jamais pour une tuile cachée).</summary>
    internal bool IsRunning => _frame.IsEnabled || _stats != null;

    /// <summary>Animée : ressort et respiration (sinon valeurs posées, aucune image).</summary>
    private static bool Animated => MotionPrefs.Rich && OpalTheme.EffectsOn;

    protected override void OnLiveChanged(bool live)
    {
        if (live)
        {
            if (!TileSamples.Enabled)
            {
                _stats = new SystemStatsService();
                _cpuKnown = false;
            }
            Sample();
            UpdateFrame();
        }
        else
        {
            _frame.Stop();
            _clock.Reset();
            _stats?.Dispose(); // requête PDH du GPU refermée : rien ne reste ouvert pour une tuile cachée
            _stats = null;
            foreach (var gauge in Gauges) gauge.Ring.Breath = 0;
        }
    }

    /// <summary>Appelé chaque seconde par la grille (page visible, île ouverte) : une mesure toutes les 2 s.</summary>
    public override void Refresh()
    {
        if (!IsLive) return;
        var due = _cpuKnown ? LiveTileShape.SampleEvery : LiveTileShape.SampleEvery / 2; // 1re valeur du processeur plus vite
        if (DateTime.UtcNow - _lastSample >= due) Sample();
        UpdateFrame(); // « Réduire les animations » / mode Gaming changés pendant que la tuile est affichée
    }

    private void Sample()
    {
        _lastSample = DateTime.UtcNow;
        SystemStats stats;
        try
        {
            stats = TileSamples.Enabled ? TileSamples.Stats : _stats?.Sample() ?? TileSamples.Stats;
        }
        catch (Exception ex)
        {
            Log.Error("Tuile Moniteur : mesure", ex);
            return;
        }
        if (stats.Cpu != null) _cpuKnown = true;
        Show(stats);
    }

    /// <summary>Affiche des mesures (null = rien encore) : textes tout de suite, anneaux par le ressort (ou posés).</summary>
    internal void Show(SystemStats? stats)
    {
        Set(_cpu, stats?.Cpu);
        Set(_ram, stats?.Ram);
        if (_gpu != null) Set(_gpu, stats?.Gpu);
        if (_down != null) _down.Text = stats?.Download == null ? Loc.T("tiles.monitor.none") : SystemStatsService.FormatRate(stats.Download);
        if (_up != null) _up.Text = stats?.Upload == null ? Loc.T("tiles.monitor.none") : SystemStatsService.FormatRate(stats.Upload);
        if (!Animated) SettleMotion();
    }

    private void Set(Gauge gauge, double? percent)
    {
        if (percent is null && gauge.Shown != null) return; // mesure manquée : on garde la dernière
        gauge.Shown = percent;
        double fill = Math.Clamp((percent ?? 0) / 100, 0, 1);
        gauge.Value.Inlines.Clear();
        if (percent is { } value)
        {
            gauge.Value.Inlines.Add(new Run(Math.Round(value).ToString("0", Loc.Culture)));
            if (TileSize != TileSize.S) gauge.Value.Inlines.Add(new Run(Loc.T("tiles.monitor.percent")) { FontSize = gauge.Value.FontSize * 0.6 });
        }
        else gauge.Value.Inlines.Add(new Run(Loc.T("tiles.monitor.none")));
        gauge.Ring.Alert = fill >= LiveTileShape.Alert;
        gauge.Spring.SetTarget(fill, LiveTileShape.Gauge);
        System.Windows.Automation.AutomationProperties.SetName(gauge.Ring, $"{gauge.Label.Text} {gauge.Value.Text}");
    }

    private IEnumerable<Gauge> Gauges => _gpu == null ? [_cpu, _ram] : [_cpu, _ram, _gpu];

    /// <summary>Minuterie des images : seulement tuile affichée ET animations permises.</summary>
    private void UpdateFrame()
    {
        bool run = IsLive && Animated;
        if (run == _frame.IsEnabled) return;
        if (run)
        {
            _clock.Restart();
            _lastTick = 0;
            _frame.Start();
        }
        else
        {
            _frame.Stop();
            SettleMotion();
        }
    }

    private void Tick()
    {
        if (!IsLive || !Animated)
        {
            UpdateFrame();
            return;
        }
        try
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - _lastTick, 0.1) * AppearanceService.Speed;
            _lastTick = now;
            double period = Opal.BreathPeriod.TotalSeconds;
            foreach (var gauge in Gauges)
            {
                gauge.Spring.Step(dt);
                gauge.Ring.Fill = gauge.Spring.Value;
                gauge.Ring.Breath = Math.Sin(2 * Math.PI * (now / period + gauge.Phase));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Tuile Moniteur : image", ex);
            _frame.Stop();
        }
    }

    public override void SettleMotion()
    {
        foreach (var gauge in Gauges)
        {
            gauge.Spring.Snap();
            gauge.Ring.Fill = gauge.Spring.Value;
            gauge.Ring.Breath = 0;
        }
    }

    /// <summary>--self-test : valeur affichée de l'anneau CPU / RAM (0 → 1).</summary>
    internal (double Cpu, double Ram, bool RamAlert) Rings => (_cpu.Ring.Fill, _ram.Ring.Fill, _ram.Ring.Alert);

    /// <summary>Petite grille de N colonnes égales (sans UniformGrid : chaque cellule centrée).</summary>
    private sealed class UniformGrid2
    {
        public Grid Panel { get; } = new();
        public Thickness Margin { set => Panel.Margin = value; }
        public VerticalAlignment VerticalAlignment { set => Panel.VerticalAlignment = value; }

        public UniformGrid2(int columns)
        {
            for (int i = 0; i < columns; i++) Panel.ColumnDefinitions.Add(new ColumnDefinition());
        }

        public void Add(FrameworkElement element)
        {
            Grid.SetColumn(element, Panel.Children.Count);
            Panel.Children.Add(element);
        }
    }
}

/// <summary>Couleurs du thème posées en ressource dynamique (elles suivent le sombre / l'opale blanche sans rien recréer).</summary>
internal static class TileInk
{
    public static T WithInk<T>(this T element, DependencyProperty property, InkRole role) where T : FrameworkElement
    {
        element.SetResourceReference(property, Paint.Key(role));
        return element;
    }
}
