using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Controls;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

public partial class CalendarTimerView : UserControl
{

    private TimerService? _timer;
    private DateTime _month;

    public CalendarTimerView()
    {
        InitializeComponent();
        foreach (var name in Loc.T("calendar.weekdays").Split(','))
        {
            DayNames.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = TypeRamp.Caption2,
                FontWeight = FontWeights.SemiBold,
                Foreground = Paint.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        ShowToday();
    }

    public void Attach(TimerService timer)
    {
        _timer = timer;
        timer.Changed += UpdateTimer;
        UpdateTimer();
    }

    /// <summary>Revient au mois courant (appelé à chaque ouverture de l'île).</summary>
    public void ShowToday()
    {
        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        RenderMonth();
    }

    private void RenderMonth()
    {
        var label = _month.ToString("MMMM yyyy", Loc.Culture);
        MonthText.Text = char.ToUpper(label[0], Loc.Culture) + label[1..];

        DaysGrid.Children.Clear();
        int offset = ((int)_month.DayOfWeek + 6) % 7; // semaine commençant le lundi
        var first = _month.AddDays(-offset);
        var today = DateTime.Today;

        for (int i = 0; i < 42; i++)
        {
            var day = first.AddDays(i);
            bool inMonth = day.Month == _month.Month;
            bool isToday = day == today;
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var text = new TextBlock
            {
                Text = day.Day.ToString(Loc.Culture),
                FontSize = TypeRamp.Footnote,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isToday ? Paint.White
                    : !inMonth ? Paint.TextFaint
                    : weekend ? Paint.TextSecondary
                    : Paint.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DaysGrid.Children.Add(new SquircleBorder
            {
                Width = Sizes.CalendarDayWidth,
                Height = Sizes.CalendarDayHeight,
                CornerRadius = Corners.Pill,
                Background = isToday ? Paint.Today : Brushes.Transparent,
                Child = text,
            });
        }
    }

    private void UpdateTimer()
    {
        var timer = _timer;
        if (timer == null) return;

        TimerText.Text = TimerService.Format(timer.State == TimerState.Idle ? timer.Duration : timer.Remaining);
        var kindBrush = KindBrush(timer.Kind);
        TimerText.Foreground = timer.State == TimerState.Running ? kindBrush : Paint.TextPrimary;
        PhaseText.Text = timer.Kind switch
        {
            TimerKind.Focus => Loc.T("timer.focus"),
            TimerKind.Break => Loc.T("timer.break"),
            _ => Loc.T("calendar.minuteur"),
        };
        PhaseText.Foreground = timer.Kind == TimerKind.Timer ? Paint.TextTertiary : kindBrush;
        StartButton.Content = timer.State switch
        {
            TimerState.Running => Loc.T("timer.pause"),
            TimerState.Paused => Loc.T("timer.resume"),
            _ => Loc.T("calendar.demarrer"),
        };
        ResetButton.IsEnabled = timer.State != TimerState.Idle;
    }

    /// <summary>Orange = minuteur, rouge = concentration, vert = pause (aussi pour l'anneau de l'île).</summary>
    public static Brush KindBrush(TimerKind kind) => kind switch
    {
        TimerKind.Focus => Paint.Today,
        TimerKind.Break => Paint.Break,
        _ => Paint.Timer,
    };

    private void Month_Click(object sender, RoutedEventArgs e) => ShowToday();

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        RenderMonth();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        RenderMonth();
    }

    private void Minus_Click(object sender, RoutedEventArgs e) => _timer?.AddMinutes(-1);

    private void Plus_Click(object sender, RoutedEventArgs e) => _timer?.AddMinutes(1);

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        if (tag == "pomodoro")
        {
            var island = SettingsStore.Current.Island;
            _timer?.StartPomodoro(TimeSpan.FromMinutes(island.PomodoroFocusMinutes), TimeSpan.FromMinutes(island.PomodoroBreakMinutes));
        }
        else
        {
            _timer?.StartWith(TimeSpan.FromMinutes(int.Parse(tag, CultureInfo.InvariantCulture)));
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _timer?.StartPause();

    private void Reset_Click(object sender, RoutedEventArgs e) => _timer?.Reset();
}
