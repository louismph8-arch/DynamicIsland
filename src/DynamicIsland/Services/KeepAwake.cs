using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Live;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// « Café » (tuile de l'île, O6) : empêche la mise en veille du PC et l'extinction de l'écran, pour toujours ou pour 30 min / 1 h /
/// 2 h. Seulement au clic, jamais enregistré (un redémarrage d'Opaline l'arrête). <c>SetThreadExecutionState</c> est propre au fil
/// qui l'appelle : tout se fait sur le fil de l'interface, et l'arrêt remet <c>ES_CONTINUOUS</c> seul (Windows reprend ses
/// réglages) — à la fin de la durée, au clic, à la fermeture de l'app (<see cref="Stop"/> appelé par l'app) et si le module plante
/// (<see cref="ModuleGuard.Coffee"/>). Rien n'est écrit dans Windows : à la fin du processus, Windows oublie tout de lui-même.
/// <para>En mode développeur (--snapshot, --self-test) l'état est seulement simulé : rien n'est demandé à Windows.</para>
/// </summary>
public static class KeepAwake
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    /// <summary>Durées proposées (null = toujours, jusqu'au clic suivant).</summary>
    public static readonly TimeSpan?[] Choices = [null, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2)];

    private static DispatcherTimer? _end;
    private static DispatcherTimer? _tick; // temps restant de la pilule (seulement pendant une durée)
    private static TimeSpan? _length;
    private static bool _applied;

    /// <summary>Le PC reste éveillé.</summary>
    public static bool IsOn { get; private set; }

    /// <summary>Heure de fin (null = toujours, ou arrêté).</summary>
    public static DateTime? EndsAt { get; private set; }

    /// <summary>Durée du tout premier clic simple : jamais « toujours » d'office (on l'oublierait facilement).</summary>
    public static readonly TimeSpan DefaultChoice = TimeSpan.FromHours(1);

    /// <summary>Dernière durée choisie (pour le clic simple sur la tuile) ; null = toujours (seulement si on l'a choisi).</summary>
    public static TimeSpan? LastChoice { get; private set; } = DefaultChoice;

    /// <summary>Marche / arrêt / fin de durée (fil de l'interface).</summary>
    public static event Action? Changed;

    /// <summary>Temps restant (null = toujours, ou arrêté).</summary>
    public static TimeSpan? Remaining => IsOn && EndsAt is { } end ? Max(end - DateTime.Now, TimeSpan.Zero) : null;

    /// <summary>Clic sur la tuile : arrêté → en marche avec la dernière durée ; en marche → arrêté.</summary>
    public static void Toggle()
    {
        if (IsOn) Stop();
        else Start(LastChoice);
    }

    /// <summary>Garde le PC éveillé pendant <paramref name="duration"/> (null = jusqu'à l'arrêt). Refuse si le module est arrêté.</summary>
    public static void Start(TimeSpan? duration)
    {
        if (ModuleGuard.IsDisabled(ModuleGuard.Coffee)) return;
        if (!OnUiThread(() => Start(duration))) return;
        LastChoice = duration;
        if (!App.DeveloperMode)
        {
            if (SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) == 0)
            {
                Log.Info("Café : Windows a refusé de garder le PC éveillé");
                return;
            }
            _applied = true;
        }
        IsOn = true;
        EndsAt = duration is { } d ? DateTime.Now + d : null;
        _end ??= CreateEndTimer();
        _end.Stop();
        if (duration is { } length)
        {
            // Une seule minuterie, calée sur la fin (pas de sondage) ; relancée si Windows la réveille en avance.
            _end.Interval = length;
            _end.Start();
        }
        _length = duration;
        SyncActivity();
        Log.Info(duration is { } span ? $"Café : PC gardé éveillé {span.TotalMinutes:0} min" : "Café : PC gardé éveillé jusqu'à l'arrêt");
        Changed?.Invoke();
    }

    /// <summary>
    /// Activité de la pilule (O5) tant que le Café est en marche : on voit toujours que le PC ne se mettra pas en veille, même si
    /// la tuile n'est plus à l'écran (disposition « Onglets », grille refaite). Anneau plein pour « toujours » (jamais l'arc qui
    /// tourne : ça peut durer des heures), temps restant sinon, relu toutes les 30 s seulement pendant une durée.
    /// </summary>
    private static void SyncActivity()
    {
        if (!IsOn)
        {
            _tick?.Stop();
            LiveActivities.Remove(LiveIds.Coffee);
            return;
        }
        double progress = 1;
        string value = "";
        if (Remaining is { } left && _length is { } total && total > TimeSpan.Zero)
        {
            progress = Math.Clamp(Math.Ceiling(left.TotalMinutes) / Math.Ceiling(total.TotalMinutes), 0, 1);
            value = PillValue(left);
            if (_tick == null)
            {
                _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _tick.Tick += (_, _) => SyncActivity();
            }
            _tick.Start();
        }
        else _tick?.Stop();
        LiveActivities.Set(new LiveActivity(LiveIds.Coffee, Icons.Coffee, progress, value, null, LivePriority.Coffee) { Breathes = false });
    }

    /// <summary>Valeur courte de la pilule : « 1:25 » dès une heure, sinon « 12 min » (calcul pur).</summary>
    public static string PillValue(TimeSpan remaining)
    {
        int minutes = (int)Math.Ceiling(Math.Max(remaining.TotalMinutes, 0));
        return minutes >= 60 ? $"{minutes / 60}:{minutes % 60:00}" : Loc.F("tiles.coffee.minutes", Math.Max(minutes, 1));
    }

    /// <summary>Rend la veille à Windows (rien si le Café n'était pas en marche). Appelé aussi à la fermeture de l'app.</summary>
    public static void Stop()
    {
        if (!IsOn && !_applied) return;
        if (!OnUiThread(Stop)) return;
        _end?.Stop();
        if (_applied)
        {
            SetThreadExecutionState(EsContinuous); // retour au comportement normal de Windows
            _applied = false;
        }
        bool wasOn = IsOn;
        IsOn = false;
        EndsAt = null;
        _length = null;
        SyncActivity();
        if (wasOn)
        {
            Log.Info("Café : veille rendue à Windows");
            Changed?.Invoke();
        }
    }

    private static DispatcherTimer CreateEndTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (EndsAt is { } end && end - DateTime.Now > TimeSpan.FromSeconds(1))
            {
                timer.Interval = end - DateTime.Now; // réveil en avance (changement d'heure, veille du PC) : on attend la vraie fin
                timer.Start();
                return;
            }
            Stop();
        };
        return timer;
    }

    /// <summary>Exécute sur le fil de l'interface : vrai si on y est déjà (sinon l'appel y est renvoyé et faux est rendu).</summary>
    private static bool OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) return true;
        dispatcher.BeginInvoke(action);
        return false;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>« 1 h 25 » / « 12 min » / « < 1 min » : temps restant, arrondi à la minute supérieure (calcul pur).</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        int minutes = (int)Math.Ceiling(Math.Max(remaining.TotalMinutes, 0));
        if (minutes <= 0) return Loc.T("tiles.coffee.less_minute");
        return minutes >= 60 ? Loc.F("tiles.coffee.hours", minutes / 60, (minutes % 60).ToString("00", Loc.Culture)) : Loc.F("tiles.coffee.minutes", minutes);
    }

    /// <summary>Libellé court d'une durée proposée (« ∞ », « 30 min », « 1 h », « 2 h »).</summary>
    public static string ChoiceLabel(TimeSpan? choice) =>
        choice is not { } d ? Loc.T("tiles.coffee.always") : d.TotalMinutes < 60 ? Loc.F("tiles.coffee.choice_min", d.TotalMinutes) : Loc.F("tiles.coffee.choice_h", d.TotalHours);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
