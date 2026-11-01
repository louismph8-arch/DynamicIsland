using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Mélangeur » de l'île (correctifs urgents, point 22) : volume général en haut, puis une ligne par appli qui fait du
/// son (icône, nom, curseur, bouton muet), comme le mélangeur de Windows. Relu toutes les 2 s, seulement quand l'onglet est
/// ouvert ; une ligne qu'on est en train de régler n'est pas réécrite. Ouvert aussi par la case « Mélangeur » du dock.
/// </summary>
public sealed class MixerView : UserControl
{
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

    private sealed class Row
    {
        public required string Key;
        public required FrameworkElement Root;
        public required TextBlock Name;
        public required LineSlider Slider;
        public required Button Mute;
        public required TextBlock Percent;
        public bool Muted;
        public double Level;
    }

    private readonly DispatcherTimer _timer = new() { Interval = Refresh };
    private readonly AudioMixerService _mixer = new();
    private readonly VolumeService _master = new();
    private readonly Row _masterRow;
    private readonly StackPanel _list = new();
    private readonly TextBlock _empty;
    private readonly Dictionary<string, Row> _rows = [];
    private bool _active, _sample;

    public MixerView()
    {
        Width = IslandShape.ContentWidth;
        var panel = new StackPanel();

        _masterRow = CreateRow("#master", Loc.T("mixer.master"), Icon(null, Icons.Mixer, master: true));
        _masterRow.Slider.UserChanged += value =>
        {
            if (_sample) return;
            _master.SetLevel(value);
            Show(_masterRow, value, muted: false);
        };
        _masterRow.Mute.Click += (_, _) =>
        {
            if (_sample) return;
            _master.ToggleMute();
            ReadMaster();
        };
        panel.Children.Add(new SquircleBorder
        {
            CornerRadius = Corners.L,
            Background = Paint.Control,
            Padding = new Thickness(Space.S4, Space.S1, Space.S3, Space.S1),
            Child = _masterRow.Root,
        });

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("mixer.apps"),
            FontSize = TypeRamp.Footnote,
            FontWeight = FontWeights.SemiBold,
            Foreground = Paint.TextSecondary,
            Margin = new Thickness(Space.S4, Space.S4, 0, Space.S1),
        });
        _empty = new TextBlock
        {
            Text = Loc.T("mixer.empty"),
            FontSize = TypeRamp.Footnote,
            Foreground = Paint.TextTertiary,
            Margin = new Thickness(Space.S4, Space.S3, Space.S4, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var scroller = new ScrollViewer
        {
            Height = IslandShape.MixerListHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Children = { _list, _empty } },
            Padding = new Thickness(Space.S4, 0, Space.S3, 0),
        };
        if (TryFindResource("ThinScrollBar") is Style thin) scroller.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), thin);
        panel.Children.Add(scroller);
        Content = panel;
        _timer.Tick += (_, _) => ReadAll();
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value && !_sample)
            {
                ReadAll();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _mixer.Release(); // les sessions ne sont gardées que tant que l'onglet est ouvert
            }
        }
    }

    // ------------------------------------------------------------------ lecture

    private void ReadAll()
    {
        ReadMaster();
        ShowApps(_mixer.Read());
    }

    private void ReadMaster()
    {
        if (_masterRow.Slider.IsDragging) return;
        bool found = _master.TryRead(out double level, out bool muted);
        _masterRow.Slider.IsEnabled = found;
        if (found) Show(_masterRow, level, muted);
    }

    private void ShowApps(IReadOnlyList<AudioMixerService.Entry> entries)
    {
        var keys = entries.Select(e => e.Key).ToHashSet();
        foreach (var gone in _rows.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _list.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }
        int index = 0;
        foreach (var entry in entries)
        {
            if (!_rows.TryGetValue(entry.Key, out var row))
            {
                row = CreateRow(entry.Key, entry.Name, Icon(entry.ExePath, entry.SystemSounds ? Icons.Bell : null, master: false));
                row.Root.Height = IslandShape.MixerRow;
                var key = entry.Key;
                row.Slider.UserChanged += value =>
                {
                    if (!_sample) _mixer.SetLevel(key, value);
                    Show(_rows[key], value, muted: false);
                };
                row.Mute.Click += (_, _) =>
                {
                    var r = _rows[key];
                    if (!_sample) _mixer.SetMute(key, !r.Muted);
                    Show(r, r.Level, !r.Muted);
                };
                _rows[entry.Key] = row;
            }
            if (_list.Children.IndexOf(row.Root) != index)
            {
                _list.Children.Remove(row.Root);
                _list.Children.Insert(index, row.Root);
            }
            if (!row.Slider.IsDragging) Show(row, entry.Level, entry.Muted);
            index++;
        }
        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Show(Row row, double level, bool muted)
    {
        row.Level = level;
        row.Muted = muted;
        row.Slider.Value = muted ? 0 : level;
        row.Mute.Content = muted || level < 0.005 ? Icons.VolumeMute
            : level < 0.34 ? Icons.VolumeLow
            : level < 0.67 ? Icons.VolumeMedium
            : Icons.VolumeHigh;
        row.Mute.ToolTip = Loc.T(muted ? "mixer.unmute" : "mixer.mute");
        row.Percent.Text = muted ? Loc.T("volume.muted") : Loc.F("format.percent", Math.Round(level * 100));
    }

    // ------------------------------------------------------------------ lignes

    private Row CreateRow(string key, string name, FrameworkElement icon)
    {
        var grid = new Grid { Background = Paint.HitTarget, Height = IslandShape.MixerRow };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandShape.MixerName) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var label = new TextBlock
        {
            Text = name,
            ToolTip = name,
            FontSize = TypeRamp.Footnote,
            FontWeight = key == "#master" ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Paint.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.S4, 0, Space.S3, 0),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var slider = new LineSlider { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 2);
        grid.Children.Add(slider);
        var percent = new TextBlock
        {
            MinWidth = Sizes.SoftButton,
            TextAlignment = TextAlignment.Right,
            FontSize = TypeRamp.Caption2,
            Foreground = Paint.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.S3, 0, 0, 0),
        };
        Grid.SetColumn(percent, 3);
        grid.Children.Add(percent);
        var mute = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = Icons.VolumeHigh,
            Margin = new Thickness(Space.S2, 0, 0, 0),
        };
        Grid.SetColumn(mute, 4);
        grid.Children.Add(mute);
        return new Row { Key = key, Root = grid, Name = label, Slider = slider, Mute = mute, Percent = percent };
    }

    /// <summary>Icône de l'exe ; sinon un glyphe (volume général, sons système) ou l'initiale du nom sur une petite tuile.</summary>
    private static FrameworkElement Icon(string? exePath, string? glyph, bool master)
    {
        if (exePath != null && IconService.Get(exePath) is { } image)
            return new Image { Source = image, Width = IslandShape.MixerIcon, Height = IslandShape.MixerIcon };
        var tile = new SquircleBorder
        {
            Width = IslandShape.MixerIcon,
            Height = IslandShape.MixerIcon,
            CornerRadius = Corners.S,
            Background = master ? null : Paint.Selected,
        };
        tile.Child = new TextBlock
        {
            Text = glyph ?? Icons.Mixer,
            FontFamily = FontStack.Icons,
            FontSize = master ? IconSize.L : IconSize.S,
            Foreground = master ? Paint.TextPrimary : Paint.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return tile;
    }

    /// <summary>Rendu --snapshot : des applis d'exemple, rien n'est lu ni réglé.</summary>
    internal void ShowSample()
    {
        _sample = true;
        _timer.Stop();
        _masterRow.Slider.IsEnabled = true;
        Show(_masterRow, 0.62, muted: false);
        ShowApps(
        [
            new("spotify", "Spotify", null, false, 0.8, false, 0.4f),
            new("chrome", "Google Chrome", null, false, 0.55, false, 0.2f),
            new("discord", "Discord", null, false, 1, false, 0.1f),
            new("steam", "Steam", null, false, 0.35, true, 0),
            new(AudioMixerService.SystemKey, Loc.T("mixer.system_sounds"), null, true, 0.5, false, 0),
        ]);
    }
}
