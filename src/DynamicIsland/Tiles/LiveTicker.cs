using System.Diagnostics;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Design;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>Une tuile vivante qui anime quelque chose en continu (ciel, trotteuse, visualiseur).</summary>
internal interface ILiveAnimation
{
    /// <summary>Pose l'image du temps <paramref name="seconds"/> (horloge partagée, en secondes) : tout se calcule à partir du temps.</summary>
    void Tick(double seconds);
}

/// <summary>
/// Minuterie partagée des tuiles vivantes (O6) : UNE minuterie à 30 i/s au plus (<see cref="LiveTiles.Frame"/>), qui ne tourne
/// que tant qu'au moins une tuile l'a demandée. Une tuile ne la demande que si elle est à l'écran (<see cref="TileFace.IsLive"/>)
/// et que les animations sont permises (<see cref="Allowed"/>) : île fermée, autre page, jeu, « Réduire les animations » ou verre
/// coupé = minuterie arrêtée (0 % CPU). Une animation qui lève est retirée seule (journal), les autres continuent.
/// </summary>
internal static class LiveTicker
{
    private static readonly HashSet<ILiveAnimation> Running = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;

    /// <summary>Animations en cours (--self-test : 0 quand aucune tuile vivante n'est à l'écran).</summary>
    public static int Count => Running.Count;

    /// <summary>La minuterie tourne (--self-test).</summary>
    public static bool IsRunning => _timer?.IsEnabled == true;

    /// <summary>Cette animation tourne vraiment (--self-test).</summary>
    public static bool Has(ILiveAnimation animation) => Running.Contains(animation) && IsRunning;

    /// <summary>--snapshot : images posées à la main, la minuterie ne les fait plus avancer.</summary>
    public static bool Frozen { get; set; }

    /// <summary>Temps de l'horloge partagée (secondes).</summary>
    public static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>Animations permises : ni « Réduire les animations », ni jeu, et effets du verre allumés (sinon tout reste figé).</summary>
    public static bool Allowed => MotionPrefs.Rich && OpalTheme.EffectsOn;

    /// <summary>Démarre (<paramref name="run"/>) ou arrête l'animation d'une tuile ; la minuterie s'arrête quand plus rien ne tourne.</summary>
    public static void Set(ILiveAnimation animation, bool run)
    {
        if (run)
        {
            if (!Running.Add(animation)) return;
            _timer ??= CreateTimer();
            if (!_timer.IsEnabled) _timer.Start();
        }
        else if (Running.Remove(animation) && Running.Count == 0)
        {
            _timer?.Stop();
        }
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = LiveTiles.Frame };
        timer.Tick += (_, _) => OnTick();
        return timer;
    }

    private static void OnTick()
    {
        if (Frozen) return;
        double now = Now;
        foreach (var animation in Running.ToArray())
        {
            try
            {
                animation.Tick(now);
            }
            catch (Exception ex)
            {
                Running.Remove(animation);
                Log.Error("Tuile vivante : animation arrêtée", ex);
            }
        }
        if (Running.Count == 0) _timer?.Stop();
    }
}
