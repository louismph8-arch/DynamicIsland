using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Views;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile Minuteur : temps restant dans un anneau (même couleur que l'activité de la pilule : orange, rouge en concentration,
/// vert en pause). L'anneau est le bouton marche / pause ; en W et L, préréglages 5 / 10 / 25 min et Pomodoro ; le reste de la
/// tuile ouvre l'outil Calendrier et minuteur. Lit le <see cref="TimerService"/> de l'île (rien d'autre ne tourne).
/// </summary>
public sealed class TimerFace : EverydayFace
{
    /// <summary>Préréglages (minutes) ; 0 = Pomodoro.</summary>
    internal static readonly int[] Presets = [5, 10, 25, 0];

    private readonly IconChip _chip = TilePieces.Chip(Icons.Timer);
    private readonly TileRing _ring;
    private readonly TextBlock _toggle;
    private readonly TextBlock _time;
    private readonly TextBlock _state = TilePieces.Info();
    private readonly List<(int Minutes, SquircleBorder Button)> _presets = [];
    private string _shown = "";

    public TimerFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        double ringSize = size switch { TileSize.S => EverydayTileShape.RingSmall, TileSize.W => EverydayTileShape.RingWide, _ => EverydayTileShape.RingLarge };
        double stroke = size switch { TileSize.S => EverydayTileShape.RingStrokeSmall, TileSize.W => EverydayTileShape.RingStroke, _ => EverydayTileShape.RingStrokeLarge };
        _ring = new TileRing(ringSize, stroke);
        _toggle = TilePieces.Glyph(Icons.Play, size == TileSize.S ? IconSize.S : IconSize.L, InkRole.TextPrimary);
        _time = TilePieces.Number(size switch { TileSize.S => TypeRamp.Title1, TileSize.W => TypeRamp.Title1, _ => TypeRamp.NumberClock });
        var ringButton = TilePieces.Button(_ring, Loc.T("tiles.timer.toggle"), Toggle, ringSize, ringSize);
        ringButton.Padding = new Thickness(0);
        ringButton.Width = ringSize;
        ringButton.CornerRadius = new CornerRadius(ringSize / 2);
        ringButton.Background = Paint.HitTarget; // le cercle entier est cliquable, sans fond visible
        ringButton.MouseEnter += (_, _) => ringButton.Background = Paint.HitTarget;
        ringButton.MouseLeave += (_, _) => ringButton.Background = Paint.HitTarget;

        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto);
                _ring.Center = _toggle;
                ringButton.HorizontalAlignment = HorizontalAlignment.Right;
                ringButton.VerticalAlignment = VerticalAlignment.Top;
                Root.Children.Add(_chip);
                Root.Children.Add(ringButton);
                _time.VerticalAlignment = VerticalAlignment.Bottom;
                Root.Children.Add(Place(_time, 1));
                Root.Children.Add(Place(TilePieces.Name(definition.Name), 2));
                break;
            case TileSize.W:
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _ring.Center = _toggle;
                ringButton.VerticalAlignment = VerticalAlignment.Center;
                Root.Children.Add(ringButton);
                var right = new Grid { Margin = new Thickness(Space.S5, 0, 0, 0) };
                foreach (var height in new[] { Auto, Auto, Star, Auto }) right.RowDefinitions.Add(new RowDefinition { Height = height });
                _time.Margin = new Thickness(0, -Space.S1, 0, 0);
                right.Children.Add(_time);
                right.Children.Add(Place(_state, 1));
                right.Children.Add(Place(PresetRow(compact: true), 3));
                Root.Children.Add(Place(right, 0, 1));
                break;
            }
            default:
            {
                Rows(Auto, Star, Auto, Auto);
                Root.Children.Add(_chip);
                _state.HorizontalAlignment = HorizontalAlignment.Right;
                _state.VerticalAlignment = VerticalAlignment.Center;
                _state.Margin = new Thickness(Sizes.IconChip + Space.S3, 0, 0, 0);
                Root.Children.Add(_state);
                var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                _time.HorizontalAlignment = HorizontalAlignment.Center;
                _toggle.Margin = new Thickness(0, Space.Px, 0, 0);
                center.Children.Add(_time);
                center.Children.Add(_toggle);
                _ring.Center = center;
                ringButton.HorizontalAlignment = HorizontalAlignment.Center;
                ringButton.VerticalAlignment = VerticalAlignment.Center;
                Root.Children.Add(Place(ringButton, 1));
                Root.Children.Add(Place(PresetRow(compact: false), 2));
                var name = TilePieces.Name(definition.Name);
                name.Margin = new Thickness(0, Space.S3, 0, 0);
                Root.Children.Add(Place(name, 3));
                break;
            }
        }
    }

    private FrameworkElement PresetRow(bool compact)
    {
        var row = new Grid { HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch };
        if (!compact) row.Margin = new Thickness(0, Space.S3, 0, 0);
        for (int i = 0; i < Presets.Length; i++)
        {
            int minutes = Presets[i];
            // En L, les durées se partagent la largeur ; le Pomodoro (icône) reste étroit.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = compact || minutes == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            var button = minutes == 0
                ? TilePieces.GlyphButton(Icons.Pomodoro, Loc.T("tiles.timer.pomodoro"), StartPomodoro, EverydayTileShape.PresetHeight)
                : TilePieces.TextButton(compact ? minutes.ToString(Loc.Culture) : Loc.F("tiles.timer.preset", minutes), Loc.F("tiles.timer.preset_tip", minutes), () => Start(minutes));
            if (minutes == 0)
            {
                button.Width = double.NaN;
                button.MinWidth = compact ? EverydayTileShape.PresetMinWidth : EverydayTileShape.PresetHeight + Space.S4;
            }
            button.Margin = new Thickness(i == 0 ? 0 : EverydayTileShape.PresetGap, 0, 0, 0);
            Grid.SetColumn(button, i);
            row.Children.Add(button);
            _presets.Add((minutes, button));
        }
        return row;
    }

    private TimerService Timer => Context.Timer;

    private void Toggle()
    {
        if (Timer.State == TimerState.Finished) Timer.Acknowledge();
        else Timer.StartPause();
        Refresh();
    }

    private void Start(int minutes)
    {
        Timer.StartWith(TimeSpan.FromMinutes(minutes));
        Refresh();
    }

    private void StartPomodoro()
    {
        var island = SettingsStore.Current.Island;
        Timer.StartPomodoro(TimeSpan.FromMinutes(island.PomodoroFocusMinutes), TimeSpan.FromMinutes(island.PomodoroBreakMinutes));
        Refresh();
    }

    /// <summary>Texte d'état (calcul pur, --self-test).</summary>
    internal static string StateText(TimerState state, TimerKind kind) => state switch
    {
        TimerState.Running => Loc.T(kind switch { TimerKind.Focus => "tiles.timer.focus", TimerKind.Break => "tiles.timer.break", _ => "tiles.timer.running" }),
        TimerState.Paused => Loc.T("tiles.timer.paused"),
        TimerState.Finished => Loc.T("tiles.timer.done"),
        _ => Loc.T("tiles.timer.ready"),
    };

    protected override void Update()
    {
        var state = Timer.State;
        var kind = Timer.Kind;
        var remaining = state == TimerState.Idle ? Timer.Duration : Timer.Remaining;
        string time = TimerService.Format(remaining);
        string key = $"{state}|{kind}|{time}|{Math.Round(Timer.Progress * 240)}";
        if (key == _shown) return; // rien n'a bougé
        _shown = key;

        bool finished = state == TimerState.Finished;
        _time.Text = time;
        if (Small) Numerals.Apply(_time, TimerService.HasHours(remaining) ? TypeRamp.NumberTile : TypeRamp.Title1);
        else if (TileSize == TileSize.L) Numerals.Apply(_time, TimerService.HasHours(remaining) ? TypeRamp.NumberTile : TypeRamp.NumberClock);
        else Numerals.Apply(_time, TimerService.HasHours(remaining) ? TypeRamp.NumberTile : TypeRamp.Title1);
        _time.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(finished ? InkRole.TimerText : InkRole.TextPrimary));
        _state.Text = StateText(state, kind);
        _toggle.Text = state == TimerState.Running ? Icons.Pause : finished ? Icons.Check : Icons.Play;
        _chip.IsActive = state == TimerState.Running;

        var brush = CalendarTimerView.KindBrush(kind);
        double progress = finished ? 1 : state == TimerState.Idle ? 0 : Timer.Progress;
        _ring.Show(progress, brush, state == TimerState.Paused ? Interaction.DimmedOpacity : 1);

        // Le préréglage qui tourne est marqué (même durée, minuteur simple ; Pomodoro pendant concentration / pause).
        bool busy = state is TimerState.Running or TimerState.Paused;
        foreach (var (minutes, button) in _presets)
        {
            bool selected = busy && (minutes == 0 ? kind != TimerKind.Timer : kind == TimerKind.Timer && Math.Abs(Timer.Total.TotalMinutes - minutes) < 0.01);
            TilePieces.SetSelected(button, selected);
        }
    }
}
