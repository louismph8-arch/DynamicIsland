using System.Windows.Threading;

namespace DynamicIsland.Services;

public enum TimerState
{
    Idle,
    Running,
    Paused,
    Finished,
}

/// <summary>Minuteur simple, ou phase d'un Pomodoro (concentration puis pause).</summary>
public enum TimerKind
{
    Timer,
    Focus,
    Break,
}

/// <summary>
/// Minuteur (1 à 99 min) et Pomodoro, basés sur l'heure de fin pour ne pas dériver. Un Pomodoro enchaîne
/// la concentration puis la pause automatiquement ; l'alerte de fin ne sonne qu'à la fin de la pause.
/// </summary>
public sealed class TimerService
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(99);
    private static readonly TimeSpan MinRemaining = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DateTime _endsAt;
    private TimeSpan _breakDuration = TimeSpan.FromMinutes(5);

    public TimerService() => _tick.Tick += (_, _) => OnTick();

    public event Action? Changed;
    /// <summary>Fin d'un minuteur ou de la pause d'un Pomodoro (alerte).</summary>
    public event Action? Finished;
    /// <summary>Fin de la concentration d'un Pomodoro : la pause démarre toute seule.</summary>
    public event Action? FocusEnded;

    /// <summary>Durée réglée pour le minuteur simple.</summary>
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(5);
    /// <summary>Durée totale de ce qui tourne (pour l'anneau de progression).</summary>
    public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(5);
    public TimerState State { get; private set; }
    public TimerKind Kind { get; private set; }

    /// <summary>Avancement de 0 à 1.</summary>
    public double Progress => Total > TimeSpan.Zero ? Math.Clamp(1 - Remaining / Total, 0, 1) : 0;

    public void StartWith(TimeSpan duration)
    {
        Duration = Clamp(duration);
        Begin(Duration, TimerKind.Timer);
        Changed?.Invoke();
    }

    /// <summary>Démarre un Pomodoro : <paramref name="focus"/> de concentration puis <paramref name="pause"/> de pause.</summary>
    public void StartPomodoro(TimeSpan focus, TimeSpan pause)
    {
        _breakDuration = pause;
        Begin(focus, TimerKind.Focus);
        Changed?.Invoke();
    }

    public void StartPause()
    {
        switch (State)
        {
            case TimerState.Running:
                Remaining = _endsAt - DateTime.UtcNow;
                State = TimerState.Paused;
                _tick.Stop();
                break;
            case TimerState.Paused:
                Resume(Remaining);
                break;
            default:
                Begin(Duration, TimerKind.Timer);
                break;
        }
        Changed?.Invoke();
    }

    public void AddMinutes(int minutes)
    {
        var delta = TimeSpan.FromMinutes(minutes);
        switch (State)
        {
            case TimerState.Running:
                var remaining = _endsAt - DateTime.UtcNow + delta;
                if (remaining < MinRemaining) remaining = MinRemaining;
                _endsAt = DateTime.UtcNow + remaining;
                Remaining = remaining;
                Total = TimeSpan.FromTicks(Math.Max(Total.Ticks, remaining.Ticks));
                break;
            case TimerState.Paused:
                Remaining += delta;
                if (Remaining < MinRemaining) Remaining = MinRemaining;
                Total = TimeSpan.FromTicks(Math.Max(Total.Ticks, Remaining.Ticks));
                break;
            default:
                Duration = Clamp(Duration + delta);
                Remaining = Total = Duration;
                State = TimerState.Idle;
                Kind = TimerKind.Timer;
                break;
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        _tick.Stop();
        Remaining = Total = Duration;
        State = TimerState.Idle;
        Kind = TimerKind.Timer;
        Changed?.Invoke();
    }

    /// <summary>L'utilisateur a vu l'alerte de fin : on revient à l'état initial.</summary>
    public void Acknowledge()
    {
        if (State == TimerState.Finished) Reset();
    }

    public static string Format(TimeSpan time)
    {
        int seconds = (int)Math.Ceiling(Math.Max(time.TotalSeconds, 0));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void Begin(TimeSpan duration, TimerKind kind)
    {
        Kind = kind;
        Total = duration;
        Resume(duration);
    }

    private void Resume(TimeSpan remaining)
    {
        Remaining = remaining;
        _endsAt = DateTime.UtcNow + remaining;
        State = TimerState.Running;
        _tick.Start();
    }

    private void OnTick()
    {
        Remaining = _endsAt - DateTime.UtcNow;
        if (Remaining > TimeSpan.Zero)
        {
            Changed?.Invoke();
            return;
        }

        if (Kind == TimerKind.Focus)
        {
            Begin(_breakDuration, TimerKind.Break); // la pause enchaîne toute seule
            Changed?.Invoke();
            FocusEnded?.Invoke();
            return;
        }

        Remaining = TimeSpan.Zero;
        _tick.Stop();
        State = TimerState.Finished;
        Changed?.Invoke();
        Finished?.Invoke();
    }

    private static TimeSpan Clamp(TimeSpan duration) =>
        duration < MinDuration ? MinDuration : duration > MaxDuration ? MaxDuration : duration;
}
