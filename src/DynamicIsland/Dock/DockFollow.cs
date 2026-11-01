using DynamicIsland.Services;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Option « Le dock suit l'écran de la souris » : décision pure (aucun appel à Windows, testée par --self-test avec des
/// écrans simulés). La souris doit rester <see cref="Delay"/> sur un autre écran avant que le dock y passe : traverser un
/// écran pour aller sur un troisième ne le fait pas sauter.
/// </summary>
internal sealed class DockFollow
{
    /// <summary>Temps passé sur un autre écran avant que le dock le rejoigne.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

    private string? _candidate;
    private DateTime _since;

    /// <summary>Écran que la souris est en train de « viser » (nom Windows), null sinon.</summary>
    public string? Candidate => _candidate;

    /// <summary>
    /// Appelée à chaque vérification (toutes les ~400 ms) : renvoie l'écran à rejoindre, ou null pour rester.
    /// <paramref name="busy"/> = glisser en cours, menu, dossier, aperçu ou souris sur le dock : on ne bouge pas.
    /// <paramref name="gameOn"/> = un jeu est sur cet écran : le dock y serait caché, il reste où il est.
    /// </summary>
    public ScreenInfo.Monitor? Next(POINT cursor, IReadOnlyList<ScreenInfo.Monitor> monitors, string currentDevice,
        Func<ScreenInfo.Monitor, bool> gameOn, bool busy, DateTime now)
    {
        ScreenInfo.Monitor? under = null;
        foreach (var monitor in monitors)
        {
            var b = monitor.Bounds;
            if (cursor.X >= b.Left && cursor.X < b.Right && cursor.Y >= b.Top && cursor.Y < b.Bottom)
            {
                under = monitor;
                break;
            }
        }
        if (busy || under is not { } target || target.Device == currentDevice || gameOn(target))
        {
            _candidate = null;
            return null;
        }
        if (_candidate != target.Device)
        {
            _candidate = target.Device;
            _since = now;
            return null;
        }
        if (now - _since < Delay) return null;
        _candidate = null;
        return target;
    }

    public void Reset() => _candidate = null;
}
