using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Health;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>Un disque interne : nom (« C: »), place libre.</summary>
public readonly record struct DiskSpace(string Name, long Free, long Total)
{
    public double FreePercent => Total > 0 ? 100.0 * Free / Total : 100;
    public bool Low => FreePercent < HealthService.LowDiskPercent;
}

/// <summary>
/// Tuile Espace disque : disques internes en barres (place utilisée), en rouge sous 10 % libres (comme l'alerte de la santé du
/// PC). Relue au plus une fois par minute, seulement tuile en vue, hors du fil de l'interface. Clic = Paramètres de Windows →
/// Stockage (rien d'autre n'est touché).
/// </summary>
public sealed class DiskFace : EverydayFace
{
    internal static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(1);

    private readonly IconChip _chip = TilePieces.Chip(Icons.HardDrive);
    private readonly StackPanel _rows = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _value;
    private readonly TextBlock _info = TilePieces.Info();
    private IReadOnlyList<DiskSpace>? _disks;
    private DateTime _readAt = DateTime.MinValue;
    private bool _reading;
    private string _shown = "";

    /// <summary>Disques donnés (rendus --snapshot, auto-test) : rien n'est lu.</summary>
    internal IReadOnlyList<DiskSpace>? Sample { get; set; }

    /// <summary>Nombre de lectures faites (auto-test : rien n'est lu tuile cachée).</summary>
    internal int Reads { get; private set; }

    public DiskFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        _value = TilePieces.Number(TypeRamp.Title1);
        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto, Auto);
                Root.Children.Add(_chip);
                _value.VerticalAlignment = VerticalAlignment.Bottom;
                Root.Children.Add(Place(_value, 1));
                _rows.Margin = new Thickness(0, Space.S1, 0, Space.S2);
                Root.Children.Add(Place(_rows, 2));
                Root.Children.Add(Place(_info, 3));
                break;
            default:
            {
                Rows(Auto, Auto, Star);
                var header = new Grid();
                header.Children.Add(_chip);
                var name = TilePieces.Name(definition.Name);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.Margin = new Thickness(Sizes.IconChip + Space.S3, 0, 0, 0);
                header.Children.Add(name);
                Root.Children.Add(header);
                if (size == TileSize.L)
                {
                    // En L : le disque le plus rempli en grand (« 21 % libres sur D: »), puis tous les disques.
                    var hero = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Space.S5, 0, 0) };
                    Numerals.Apply(_value, TypeRamp.Display);
                    hero.Children.Add(_value);
                    _info.VerticalAlignment = VerticalAlignment.Bottom;
                    _info.Margin = new Thickness(Space.S3, 0, 0, Space.S3);
                    hero.Children.Add(_info);
                    Root.Children.Add(Place(hero, 1));
                }
                Root.Children.Add(Place(_rows, 2));
                break;
            }
        }
    }

    /// <summary>Disques montrés : S = le plus rempli, W = 2, L = 5 (calcul pur, --self-test).</summary>
    internal static List<DiskSpace> Pick(IEnumerable<DiskSpace> disks, TileSize size)
    {
        var list = disks.ToList();
        if (size == TileSize.S) return list.OrderBy(d => d.FreePercent).Take(1).ToList();
        return list.Take(size == TileSize.W ? 2 : 5).ToList();
    }

    protected override bool OnTileClick()
    {
        if (!App.DeveloperMode) Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
        return true;
    }

    protected override void Update()
    {
        if (Sample == null && !_reading && DateTime.UtcNow - _readAt >= ReadEvery)
        {
            _reading = true;
            _readAt = DateTime.UtcNow;
            Reads++;
            Task.Run(Read).ContinueWith(task =>
            {
                _reading = false;
                if (task.Exception != null) Log.Error("Tuile Espace disque : lecture", task.Exception.GetBaseException());
                else
                {
                    _disks = task.Result;
                    Show();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        Show();
    }

    private static List<DiskSpace> Read() =>
        HealthService.Disks().Select(d => new DiskSpace(d.Name.TrimEnd('\\'), d.AvailableFreeSpace, d.TotalSize)).ToList();

    private void Show()
    {
        var all = Sample ?? _disks; // disques d'exemple (rendus) d'abord : une lecture en cours ne les remplace pas
        var disks = all == null ? [] : Pick(all, TileSize);
        string key = string.Join("|", disks.Select(d => $"{d.Name}:{Math.Round(d.FreePercent)}:{d.Free >> 28}"));
        if (all == null) key = "?";
        if (key == _shown) return;
        _shown = key;
        _rows.Children.Clear();
        _chip.IsActive = false;
        if (all == null)
        {
            _value.Text = "";
            _info.Text = Loc.T("tiles.disk.reading");
            return;
        }
        if (disks.Count == 0)
        {
            _value.Text = "";
            _info.Text = Loc.T("tiles.disk.none");
            if (!Small)
            {
                var none = TilePieces.Info();
                none.Text = Loc.T("tiles.disk.none");
                _rows.Children.Add(none);
            }
            return;
        }
        if (Small)
        {
            var disk = disks[0];
            _value.Text = Loc.F("tiles.disk.percent", Math.Round(disk.FreePercent));
            _value.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(disk.Low ? InkRole.DangerText : InkRole.TextPrimary));
            _rows.Children.Add(Bar(disk));
            _info.Text = Loc.F("tiles.disk.small", disk.Name, Size(disk.Free));
            return;
        }
        if (Large && all.OrderBy(d => d.FreePercent).First() is var fullest)
        {
            _value.Text = Loc.F("tiles.disk.percent", Math.Round(fullest.FreePercent));
            // Rouge « texte » : foncé en opale blanche (le rouge vif tombait à 1,7:1 sur une tuile claire)
            _value.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(fullest.Low ? InkRole.DangerText : InkRole.TextPrimary));
            _info.Text = Loc.F("tiles.disk.free_on", fullest.Name);
        }
        for (int i = 0; i < disks.Count; i++)
        {
            var row = Row(disks[i]);
            if (i > 0) row.Margin = new Thickness(0, EverydayTileShape.DiskRowGap, 0, 0);
            _rows.Children.Add(row);
        }
    }

    private static FrameworkElement Row(DiskSpace disk)
    {
        var stack = new StackPanel();
        var line = new Grid { Margin = new Thickness(0, 0, 0, Space.S1) };
        var name = TilePieces.Text(TypeRamp.Caption, InkRole.TextPrimary, FontWeights.SemiBold);
        name.Text = disk.Name;
        var free = Numerals.Apply(new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis }, TypeRamp.Caption2);
        free.Text = Loc.F("tiles.disk.free", Math.Round(disk.FreePercent), Size(disk.Free));
        free.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(disk.Low ? InkRole.DangerText : InkRole.TextSecondary));
        line.Children.Add(name);
        line.Children.Add(free);
        stack.Children.Add(line);
        stack.Children.Add(Bar(disk));
        return stack;
    }

    /// <summary>Barre de la place UTILISÉE (accent, rouge si presque plein).</summary>
    private static FrameworkElement Bar(DiskSpace disk)
    {
        var bar = TilePieces.Bar(out var fill);
        double used = Math.Clamp(1 - disk.FreePercent / 100, 0, 1);
        if (disk.Low) fill.Background = Paint.Danger;
        else fill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * used;
        return bar;
    }

    /// <summary>« 412 Go » / « 1,8 To » / « 820 Mo » (calcul pur).</summary>
    internal static string Size(long bytes)
    {
        const double gb = 1L << 30;
        if (bytes >= 1L << 40) return Loc.F("size.tb", (bytes / (gb * 1024)).ToString("0.#", Loc.Culture));
        if (bytes >= 1L << 30) return Loc.F("size.gb", (bytes / gb).ToString(bytes >= 100 * (1L << 30) ? "0" : "0.#", Loc.Culture));
        return Loc.F("size.mb", (bytes / (double)(1L << 20)).ToString("0", Loc.Culture));
    }
}
