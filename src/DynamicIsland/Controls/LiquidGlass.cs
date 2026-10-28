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

    static LiquidGlass()
    {
        Timer.Tick += (_, _) => TrackCursor();
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
        SetEnabled(false);
    }

    /// <summary>--snapshot et mesure : place le curseur (repère de la forme) sans toucher à la vraie souris.</summary>
    internal void SimulateCursor(Point? local)
    {
        _forcedCursor = local;
        Track(default);
    }

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
            Timer.Start();
        }
        else
        {
            Active.Remove(this);
            if (Active.Count == 0) Timer.Stop();
        }
    }

    /// <summary>Lecture du curseur pour toutes les formes : rapide seulement quand une lumière bouge.</summary>
    private static void TrackCursor()
    {
        if (!GetCursorPos(out var cursor)) return;
        bool moving = false;
        foreach (var glass in Active.ToList()) moving |= glass.Track(cursor);
        Timer.Interval = moving ? Glass.ActiveTick : Glass.IdleTick;
    }

    /// <returns>Vrai si la lumière bouge encore (le curseur est proche ou la lumière revient au repos).</returns>
    private bool Track(POINT cursor)
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
        double follow = _forcedCursor != null ? 1 : Glass.Follow; // rendu figé : pas de lissage
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
        var look = new GlassLook(
            Band: Glass.Band,
            Refraction: Glass.Refraction * strength,
            Aberration: Glass.Aberration,
            Rim: Glass.Rim * strength * (Glass.RimAtRest + (1 - Glass.RimAtRest) * _proximity),
            Sheen: Glass.Sheen * strength * _proximity,
            SheenRadius: Glass.SheenRadius,
            TopLight: Glass.TopLight * strength);
        _effect.Update(size, shape, _radius(), light, look);
    }
}
