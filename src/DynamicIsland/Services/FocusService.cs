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
