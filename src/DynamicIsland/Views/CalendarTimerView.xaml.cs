using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>Durée « Perso » en cours de réglage : heures, minutes, secondes.</summary>
    private readonly int[] _custom = new int[3];
    private readonly TextBlock[] _customDigits = new TextBlock[3];
    private static readonly int[] CustomMax = [23, 59, 59];

    public CalendarTimerView()
    {
        InitializeComponent();
        BuildCustomCounters();
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

    /// <summary>Revient au mois courant et au minuteur (appelé à chaque ouverture de l'île).</summary>
    public void ShowToday()
    {
        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        RenderMonth();
        ShowCustom(false);
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

        var shown = timer.State == TimerState.Idle ? timer.Duration : timer.Remaining;
        TimerText.Text = TimerService.Format(shown);
        TimerText.FontSize = TimerService.HasHours(shown) ? TypeRamp.Display : TypeRamp.Hero; // graisse : Text.Numbers.Hero // « 1:30:00 » tient entre − et +
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

    // ------------------------------------------------------------------ durée « Perso » (correctif 19)

    /// <summary>Trois compteurs h / min / s : unité, +, nombre, − ; la molette marche sur toute la colonne.</summary>
    private void BuildCustomCounters()
    {
        foreach (var _ in Enumerable.Range(0, 4)) CustomCounters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] units = [Loc.T("timer.unit.hours"), Loc.T("timer.unit.minutes"), Loc.T("timer.unit.seconds")];
        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                CustomCounters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var colon = Digits(":");
                colon.Foreground = Paint.TextTertiary;
                Place(colon, row: 2, column: CustomCounters.ColumnDefinitions.Count - 1);
            }
            CustomCounters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Sizes.TimerStepColumn) });
            int column = CustomCounters.ColumnDefinitions.Count - 1, part = i;
            // Zone de la molette : toute la colonne, derrière les boutons.
            var hit = new Border { Background = Brushes.Transparent, Tag = part };
            Grid.SetRowSpan(hit, 4);
            Place(hit, row: 0, column);
            Place(new TextBlock
            {
                Text = units[i], FontSize = TypeRamp.Caption2, FontWeight = FontWeights.SemiBold, Foreground = Paint.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center, Tag = part,
            }, row: 0, column);
            Place(StepButton(Icons.Plus, part, +1), row: 1, column);
            _customDigits[i] = Digits("00");
            _customDigits[i].Tag = part;
            Place(_customDigits[i], row: 2, column);
            Place(StepButton(Icons.Minus, part, -1), row: 3, column);
        }
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        CustomCounters.Children.Add(element);
    }

    private static TextBlock Digits(string text) => Numerals.Apply(new TextBlock
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    }, TypeRamp.Title1);

    private Button StepButton(string glyph, int part, int delta)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = glyph,
            Width = Sizes.TimerStepButton,
            Height = Sizes.TimerStepButton,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = part,
        };
        button.Click += (_, _) => Step(part, delta);
        return button;
    }

    /// <summary>+1 / −1 sur un compteur, en boucle (59 → 00) comme une molette d'horloge.</summary>
    private void Step(int part, int delta)
    {
        _custom[part] = CustomStep(_custom[part], delta, CustomMax[part]);
        ShowCustomDigits();
    }

    internal static int CustomStep(int value, int delta, int max) => ((value + delta) % (max + 1) + max + 1) % (max + 1);

    private void CustomCounters_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Colonne sous la souris : l'élément touché (ou son parent direct dans la grille) porte le numéro du compteur.
        for (var element = e.OriginalSource as DependencyObject; element != null && element != CustomCounters; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { Tag: int part } && VisualTreeHelper.GetParent(element) == CustomCounters)
            {
                Step(part, e.Delta > 0 ? 1 : -1);
                e.Handled = true;
                return;
            }
        }
    }

    private void ShowCustomDigits()
    {
        for (int i = 0; i < 3; i++) _customDigits[i].Text = _custom[i].ToString("00", CultureInfo.InvariantCulture);
        CustomStartButton.IsEnabled = CustomDuration >= TimerService.MinDuration; // 00:00:00 : rien à lancer
    }

    private TimeSpan CustomDuration => new(_custom[0], _custom[1], _custom[2]);

    /// <summary>Éditeur « Perso » à la place des préréglages (rouvert sur la dernière durée choisie), ou retour au minuteur.</summary>
    private void ShowCustom(bool show)
    {
        if (show)
        {
            var last = TimeSpan.FromSeconds(SettingsStore.Current.Timer.CustomSeconds);
            _custom[0] = Math.Min((int)last.TotalHours, CustomMax[0]);
            _custom[1] = last.Minutes;
            _custom[2] = last.Seconds;
            ShowCustomDigits();
        }
        CustomPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Custom_Click(object sender, RoutedEventArgs e) => ShowCustom(true);

    private void CustomClose_Click(object sender, RoutedEventArgs e) => ShowCustom(false);

    private void CustomStart_Click(object sender, RoutedEventArgs e)
    {
        var duration = CustomDuration;
        if (duration < TimerService.MinDuration) return;
        SettingsStore.Update(s => s.Timer.CustomSeconds = (int)duration.TotalSeconds); // rouverte telle quelle la prochaine fois
        _timer?.StartWith(duration);
        ShowCustom(false);
    }

    /// <summary>--snapshot : l'éditeur « Perso » ouvert sur une durée d'exemple.</summary>
    internal void ShowCustomSample(TimeSpan duration)
    {
        ShowCustom(true);
        _custom[0] = duration.Hours;
        _custom[1] = duration.Minutes;
        _custom[2] = duration.Seconds;
        ShowCustomDigits();
    }
}
