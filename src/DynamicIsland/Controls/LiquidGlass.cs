using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Controls;

/// <summary>
/// Verre liquide sur une forme (île, palette, dock) : pose <see cref="LiquidGlassEffect"/> quand le réglage est
/// actif (jamais en mode Gaming) et le tient à jour : taille, arrondi, lumière qui suit le curseur quand il
/// approche. Au repos, la lumière vient d'au-dessus et plus rien n'est redessiné.
/// </summary>
public sealed class LiquidGlass
{
    private static readonly List<LiquidGlass> Active = [];
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Render) { Interval = Glass.IdleTick };

    private readonly FrameworkElement _surface;
    private readonly Func<double> _radius;
    private readonly Func<Thickness>? _overflow;
    private readonly LiquidGlassEffect _effect = new();
    private Point? _light;          // position actuelle (repère de la forme) ; null = au repos
    private double _proximity;      // 0 = curseur loin, 1 = dessus
    private Point? _forcedCursor;   // --snapshot / mesure : curseur simulé
    private bool _enabled;

    /// <summary>
    /// La lumière a bougé (position dans le repère de la forme, taille de la forme) : le verre opalin oriente sa bordure irisée
    /// dessus (O1). Rien de plus : c'est le même suivi du curseur, déjà limité aux moments où une lumière bouge.
    /// </summary>
    public event Action<Point, Size>? LightMoved;

    /// <summary>Verre posé (réglage actif, pas en jeu) : sinon la lumière ne bouge jamais.</summary>
    public bool IsEnabled => _enabled;

    // Lumière qui bouge : suivie aux images de WPF (pas une minuterie de 33 ms, arrondie à 46,8 ms par Windows → 21 i/s).
    private static bool _framed;
    private static TimeSpan _lastFrame;   // RenderingTime de la dernière image vue
    private static long _lastStep;        // Stopwatch : dernière mise à jour de la lumière (dt réel du lissage)

    static LiquidGlass()
    {
        Timer.Tick += (_, _) => TrackCursor(Glass.ActiveTick.TotalSeconds);
    }

    /// <param name="surface">Élément qui dessine la forme (fond + contenu à réfracter).</param>
    /// <param name="radius">Rayon actuel des coins de la forme.</param>
    /// <param name="overflow">Débord de la forme hors de l'élément (côtés collés au bord de l'écran : pas de liseré).</param>
    public LiquidGlass(FrameworkElement surface, Func<double> radius, Func<Thickness>? overflow = null)
    {
        _surface = surface;
        _radius = radius;
        _overflow = overflow;
        surface.SizeChanged += (_, _) => Refresh();
        SettingsStore.Changed += Apply;
        GamingMode.Changed += Apply;
        Design.OpalShade.ThemeChanged += Refresh; // opale blanche : liseré et lumière adoucis
        Apply();
    }

    /// <summary>La forme a changé (taille ou arrondi animés par un ressort).</summary>
    public void Refresh()
    {
        if (_enabled) Push();
    }

    /// <summary>Fenêtre fermée : on arrête de suivre la forme.</summary>
    public void Detach()
    {
        SettingsStore.Changed -= Apply;
        GamingMode.Changed -= Apply;
        Design.OpalShade.ThemeChanged -= Refresh;
        SetEnabled(false);
    }

    /// <summary>--snapshot et mesure : place le curseur (repère de la forme) sans toucher à la vraie souris.</summary>
    /// <param name="smooth">Vrai : la lumière rejoint ce curseur par le suivi normal (cadence, lissage), comme une vraie souris.</param>
    internal void SimulateCursor(Point? local, bool smooth = false)
    {
        _forcedCursor = local;
        _forcedSmooth = smooth && local != null;
        if (!_forcedSmooth) Track(default, Glass.ActiveTick.TotalSeconds);
        else if (_enabled) SetFramed(true); // la lumière va bouger : suivie aux images, comme pour une vraie souris
    }

    private bool _forcedSmooth;

    private void Apply()
    {
        var look = SettingsStore.Current.Appearance;
        SetEnabled(look.LiquidGlass && !GamingMode.IsActive && LiquidGlassEffect.IsAvailable);
        Refresh();
    }

    private void SetEnabled(bool enabled)
    {
        if (enabled == _enabled) return;
        _enabled = enabled;
        _surface.Effect = enabled ? _effect : null;
        if (enabled)
        {
            Active.Add(this);
            if (!_framed) Timer.Start();
        }
        else
        {
            Active.Remove(this);
            if (Active.Count == 0) SetFramed(false, stopAll: true);
        }
    }

    /// <summary>
    /// Lecture du curseur pour toutes les formes. Curseur loin : minuterie lente (<see cref="Glass.IdleTick"/>). Dès qu'une
    /// lumière bouge : une mise à jour par image de WPF (au plus toutes les <see cref="Glass.MinFrame"/>), puis retour à la
    /// minuterie lente quand toutes les lumières sont posées et le curseur loin. Une seule boucle pour toutes les formes.
    /// </summary>
    private static void TrackCursor(double dt)
    {
        if (!GetCursorPos(out var cursor)) return;
        bool moving = false;
        foreach (var glass in Active.ToList()) moving |= glass.Track(cursor, dt);
        SetFramed(moving);
    }

    private static void SetFramed(bool framed, bool stopAll = false)
    {
        if (framed != _framed)
        {
            _framed = framed;
            if (framed)
            {
                Timer.Stop();
                _lastFrame = TimeSpan.Zero;
                _lastStep = System.Diagnostics.Stopwatch.GetTimestamp();
                System.Windows.Media.CompositionTarget.Rendering += OnRendering;
            }
            else System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
        }
        if (stopAll) Timer.Stop();
        else if (!framed && !Timer.IsEnabled) Timer.Start();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var time = ((System.Windows.Media.RenderingEventArgs)e).RenderingTime;
        if (time == _lastFrame) return; // Rendering peut être levé plusieurs fois par image
        _lastFrame = time;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = System.Diagnostics.Stopwatch.GetElapsedTime(_lastStep, now).TotalSeconds;
        if (dt < Glass.MinFrame.TotalSeconds) return; // écran rapide : pas plus de ~110 mises à jour/s
        _lastStep = now;
        try
        {
            TrackCursor(Math.Min(dt, 0.1));
        }
        catch (Exception ex)
        {
            SetFramed(false); // jamais une exception par image : retour à la minuterie lente
            Log.Error("Verre liquide : suivi du curseur", ex);
        }
    }

    /// <returns>Vrai si la lumière bouge encore (le curseur est proche ou la lumière revient au repos).</returns>
    private bool Track(POINT cursor, double dt)
    {
        if (!_enabled || !_surface.IsVisible || PresentationSource.FromVisual(_surface) == null) return false;
        var size = new Size(_surface.ActualWidth, _surface.ActualHeight);
        var rest = new Point(size.Width * Glass.RestX, -size.Height * Glass.RestHeights);

        Point target = rest;
        double proximity = 0;
        var local = _forcedCursor ?? (SettingsStore.Current.Appearance.GlassFollowMouse ? ToLocal(cursor) : null);
        if (local is Point point)
        {
            double dx = Math.Max(Math.Max(-point.X, point.X - size.Width), 0);
            double dy = Math.Max(Math.Max(-point.Y, point.Y - size.Height), 0);
            proximity = 1 - Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / Glass.LightRange, 0, 1);
            if (proximity > 0) target = point;
        }

        var current = _light ?? rest;
        // Lissage indépendant de la cadence : Follow est la part du chemin parcourue en ActiveTick. Rendu figé : pas de lissage.
        double follow = _forcedCursor != null && !_forcedSmooth ? 1
            : 1 - Math.Pow(1 - Glass.Follow, dt / Glass.ActiveTick.TotalSeconds);
        var next = new Point(current.X + (target.X - current.X) * follow, current.Y + (target.Y - current.Y) * follow);
        double nextProximity = _proximity + (proximity - _proximity) * follow;
        bool settled = Math.Abs(next.X - target.X) < 0.5 && Math.Abs(next.Y - target.Y) < 0.5 && Math.Abs(nextProximity - proximity) < 0.01;
        if (settled)
        {
            next = target;
            nextProximity = proximity;
        }
        bool changed = next != current || nextProximity != _proximity;
        _light = next;
        _proximity = nextProximity;
        if (changed) Push();
        return !settled || proximity > 0;
    }

    private Point? ToLocal(POINT cursor)
    {
        try
        {
            return _surface.PointFromScreen(new Point(cursor.X, cursor.Y));
        }
        catch (InvalidOperationException)
        {
            return null; // fenêtre en cours de fermeture
        }
    }

    private void Push()
    {
        var size = new Size(_surface.ActualWidth, _surface.ActualHeight);
        if (size.Width < 1 || size.Height < 1) return;
        var light = _light ?? new Point(size.Width * Glass.RestX, -size.Height * Glass.RestHeights);
        var overflow = _overflow?.Invoke() ?? default;
        var shape = new Rect(-overflow.Left, -overflow.Top,
            size.Width + overflow.Left + overflow.Right, size.Height + overflow.Top + overflow.Bottom);

        double strength = SettingsStore.Current.Appearance.GlassIntensity;
        // Opale blanche (O2) : le shader ajoute du blanc (liseré, reflet, lumière du haut), délavé sur un verre clair.
        bool pearl = Design.OpalShade.IsLight;
        double rim = pearl ? Opal.LightGlassRim : 1, sheen = pearl ? Opal.LightGlassSheen : 1, top = pearl ? Opal.LightGlassTopLight : 1;
        var look = new GlassLook(
            Band: Glass.Band,
            Refraction: Glass.Refraction * strength,
            Aberration: Glass.Aberration,
            Rim: rim * Glass.Rim * strength * (Glass.RimAtRest + (1 - Glass.RimAtRest) * _proximity),
            Sheen: sheen * Glass.Sheen * strength * _proximity,
            SheenRadius: Glass.SheenRadius,
            TopLight: top * Glass.TopLight * strength);
        _effect.Update(size, shape, _radius(), light, look);
        LightMoved?.Invoke(light, size);
    }
}
