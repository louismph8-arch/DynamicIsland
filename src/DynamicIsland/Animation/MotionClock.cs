using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Animation;

/// <summary>Une animation pilotée par <see cref="MotionClock"/> : avance de dt secondes, true une fois terminée.</summary>
public interface IMotion
{
    bool Step(double dt);

    /// <summary>Saute à la fin (rendus --snapshot, fenêtre fermée).</summary>
    void Finish();
}

/// <summary>
/// Horloge partagée des mouvements (refonte O3 : cascade, tuiles, pages, outils) : une seule abonnée à
/// <c>CompositionTarget.Rendering</c>, et SEULEMENT tant qu'au moins une animation joue (0 travail au repos, règle 7A). Une
/// animation qui lève une exception est retirée seule (journal), les autres continuent.
/// </summary>
public static class MotionClock
{
    private static readonly List<IMotion> Active = [];
    private static bool _subscribed;
    private static TimeSpan _last;

    /// <summary>Animations en cours (--self-test, --perf-bench : 0 au repos).</summary>
    public static int Count => Active.Count;

    /// <summary>--snapshot : image intermédiaire figée (l'écran n'avance plus les animations jusqu'à ce qu'on la libère).</summary>
    public static bool Frozen { get; set; }

    public static void Start(IMotion motion)
    {
        if (!Active.Contains(motion)) Active.Add(motion);
        if (_subscribed) return;
        _subscribed = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    public static void Stop(IMotion motion) => Active.Remove(motion);

    public static bool IsRunning(IMotion motion) => Active.Contains(motion);

    /// <summary>Tout saute à la fin (rendus figés).</summary>
    public static void FinishAll()
    {
        foreach (var motion in Active.ToArray())
        {
            try { motion.Finish(); }
            catch (Exception ex) { Log.Error("Mouvement : fin", ex); }
        }
        Active.Clear();
        Unsubscribe();
    }

    /// <summary>Avance tout de <paramref name="seconds"/> par pas de 1/60 s, sans attendre l'écran (rendus d'une image intermédiaire).</summary>
    public static void Advance(double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 1 / 60.0) Tick(Math.Min(1 / 60.0, seconds - t));
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return; // Rendering peut être levé plusieurs fois par image
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _last).TotalSeconds, 1 / 30.0);
        _last = now;
        if (Frozen) return;
        Tick(dt * AppearanceService.Speed);
        if (Active.Count == 0) Unsubscribe();
    }

    private static void Tick(double dt)
    {
        foreach (var motion in Active.ToArray())
        {
            try
            {
                if (motion.Step(dt)) Active.Remove(motion);
            }
            catch (Exception ex)
            {
                Active.Remove(motion);
                Log.Error("Mouvement : animation arrêtée", ex);
            }
        }
    }

    private static void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }
}

/// <summary>
/// Calque des mouvements (Canvas non cliquable posé par-dessus le contenu de l'île) : reflet qui suit le curseur sur une tuile,
/// tuile qui s'agrandit en outil. Sans calque (autre fenêtre), ces effets se font sans reflet / sans morphing.
/// </summary>
public static class MotionLayer
{
    public static readonly DependencyProperty IsHostProperty = DependencyProperty.RegisterAttached(
        "IsHost", typeof(bool), typeof(MotionLayer), new PropertyMetadata(false));

    public static bool GetIsHost(DependencyObject element) => (bool)element.GetValue(IsHostProperty);
    public static void SetIsHost(DependencyObject element, bool value) => element.SetValue(IsHostProperty, value);

    /// <summary>Le calque de la même fenêtre (cherché depuis la racine de la fenêtre), ou null.</summary>
    public static Canvas? HostOf(DependencyObject element)
    {
        DependencyObject? root = element;
        while (root != null && VisualTreeHelper.GetParent(root) is { } parent) root = parent;
        return root == null ? null : Find(root);
    }

    private static Canvas? Find(DependencyObject node)
    {
        if (node is Canvas canvas && GetIsHost(canvas)) return canvas;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }
}
