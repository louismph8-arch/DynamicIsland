using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// « Ne pas déranger » d'Opaline (7D, interface en 7F) : tant qu'il est actif, l'île ne montre plus d'annonces (appareils,
/// téléchargements, amis, mises à jour, automatisations…) et ne joue plus de son. Avec une durée (fin toute seule) ou jusqu'à ce
/// qu'on l'enlève ; gardé dans les réglages (il survit à un redémarrage). Ne touche pas au « Ne pas déranger » de Windows.
/// </summary>
public static class FocusService
{
    private static readonly DispatcherTimer End = new();

    /// <summary>Activé ou arrêté (sur le thread de l'interface).</summary>
    public static event Action? Changed;

    public static bool DoNotDisturb => Until is { } until && until > DateTime.Now;

    /// <summary>Fin prévue ; DateTime.MaxValue = jusqu'à ce qu'on l'enlève ; null = arrêté.</summary>
    public static DateTime? Until => SettingsStore.Current.Focus.DoNotDisturbUntil;

    public static void Start()
    {
        End.Tick += (_, _) => Schedule(); // fin atteinte → arrêt, sinon minute suivante
        Schedule();
    }

    /// <param name="duration">null = jusqu'à ce qu'on l'enlève.</param>
    public static void TurnOn(TimeSpan? duration)
    {
        var until = duration is { } d ? DateTime.Now + d : DateTime.MaxValue;
        SettingsStore.Update(s => s.Focus.DoNotDisturbUntil = until);
        Log.Info($"Ne pas déranger : activé {(duration is { } span ? $"pour {span.TotalMinutes:0} min" : "jusqu'à ce qu'on l'enlève")}");
        Schedule();
        Changed?.Invoke();
    }

    /// <summary>Jusqu'à une heure précise (« jusqu'à demain matin »).</summary>
    public static void TurnOnUntil(DateTime until)
    {
        if (until <= DateTime.Now) return;
        SettingsStore.Update(s => s.Focus.DoNotDisturbUntil = until);
        Log.Info($"Ne pas déranger : activé jusqu'à {until:dd/MM HH:mm}");
        Schedule();
        Changed?.Invoke();
    }

    /// <summary>Heure du réveil pour « jusqu'à demain matin ».</summary>
    public const int MorningHour = 8;

    /// <summary>Prochain 8 h strictement après <paramref name="now"/> (22 h → le lendemain, 3 h → le jour même). Calcul pur.</summary>
    public static DateTime NextMorning(DateTime now)
    {
        var today = now.Date.AddHours(MorningHour);
        return now < today ? today : today.AddDays(1);
    }

    /// <summary>Choix proposés partout (île, palette, icône de notification) : durée, ou null = « jusqu'à demain matin ».</summary>
    public enum Choice { HalfHour, Hour, Morning, Forever }

    public static readonly Choice[] Choices = [Choice.HalfHour, Choice.Hour, Choice.Morning, Choice.Forever];

    public static void TurnOn(Choice choice)
    {
        switch (choice)
        {
            case Choice.HalfHour: TurnOn(TimeSpan.FromMinutes(30)); break;
            case Choice.Hour: TurnOn(TimeSpan.FromHours(1)); break;
            case Choice.Morning: TurnOnUntil(NextMorning(DateTime.Now)); break;
            default: TurnOn((TimeSpan?)null); break;
        }
    }

    /// <summary>Libellé d'un choix (« 30 minutes », « Jusqu'à demain matin (8 h) »…).</summary>
    public static string Label(Choice choice) => choice switch
    {
        Choice.HalfHour => Loc.T("dnd.choice.30min"),
        Choice.Hour => Loc.T("dnd.choice.1h"),
        Choice.Morning => Loc.F("dnd.choice.morning", new DateTime(2000, 1, 1, MorningHour, 0, 0).ToString(Loc.T("format.hour"), Loc.Culture)),
        _ => Loc.T("dnd.choice.forever"),
    };

    /// <summary>
    /// Ce qu'il reste (calcul pur) : -1 = jusqu'à ce qu'on l'enlève, 0 = arrêté, sinon minutes restantes arrondies au-dessus.
    /// </summary>
    public static int MinutesLeft(DateTime? until, DateTime now) =>
        until is not { } end || end <= now ? 0
        : end == DateTime.MaxValue ? -1
        : (int)Math.Ceiling((end - now).TotalMinutes);

    /// <summary>« Désactivé », « Encore 25 min », « Jusqu'à 8:00 », « Jusqu'à ce que je l'arrête ».</summary>
    public static string Describe(DateTime now)
    {
        var until = Until;
        int left = MinutesLeft(until, now);
        if (left == 0) return Loc.T("dnd.state.off");
        if (left < 0) return Loc.T("dnd.state.forever");
        if (left <= 90) return Loc.F("dnd.state.minutes", left);
        string hour = until!.Value.ToString(Loc.T("format.hour"), Loc.Culture);
        return until.Value.Date == now.Date ? Loc.F("dnd.state.until", hour) : Loc.F("dnd.state.until_tomorrow", hour);
    }

    public static void Stop()
    {
        End.Stop();
        if (SettingsStore.Current.Focus.DoNotDisturbUntil == null) return;
        SettingsStore.Update(s => s.Focus.DoNotDisturbUntil = null);
        Log.Info("Ne pas déranger : arrêté");
        Changed?.Invoke();
    }

    /// <summary>Minuterie jusqu'à la fin prévue (vérifiée chaque minute : le PC a pu dormir entre-temps).</summary>
    private static void Schedule()
    {
        End.Stop();
        if (Until is not { } until || until == DateTime.MaxValue) return;
        if (until <= DateTime.Now)
        {
            Stop();
            return;
        }
        var left = until - DateTime.Now;
        End.Interval = left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1);
        End.Start();
    }
}
