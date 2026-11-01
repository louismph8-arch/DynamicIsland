using System.Runtime.CompilerServices;
using System.Windows;
using DynamicIsland.Themes;

namespace DynamicIsland.Animation;

/// <summary>
/// Changement de page (refonte O3 ; aujourd'hui les onglets de l'île, demain les pages de tuiles d'O6) : la nouvelle page arrive
/// en glissant du côté où l'on va, avec un élan (le ressort dépasse à peine), l'ancienne part de l'autre côté (moitié moins loin,
/// pendant son fondu). Horizontal ou vertical (onglets en colonne). Les opacités restent à l'appelant (fondus existants) ; ici
/// seulement la translation <see cref="MotionParts.Slide"/>, remise à 0 à la fin.
/// </summary>
public static class PageTransition
{
    private static readonly ConditionalWeakTable<UIElement, Slide> Slides = new();

    /// <param name="direction">+1 = page suivante (arrive de la droite / du bas), -1 = précédente.</param>
    /// <param name="clip">Conteneur à découper pendant le glissement (sinon la page passerait sur l'en-tête), rendu ensuite.</param>
    public static void Play(UIElement? from, UIElement to, int direction, bool vertical, FrameworkElement? clip = null)
    {
        if (direction == 0 || !MotionPrefs.Rich) return;
        double distance = Kinetics.PageDistance * Math.Sign(direction);
        if (from != null && !ReferenceEquals(from, to)) Start(from, 0, -distance * Kinetics.PageExit, 0, Springs.PageExit, vertical, clip);
        Start(to, distance, 0, -Math.Sign(direction) * Kinetics.PageFling, Springs.Page, vertical, clip);
    }

    /// <summary>Plus de glissement : page remise à sa place tout de suite.</summary>
    public static void Stop(UIElement page)
    {
        if (!Slides.TryGetValue(page, out var slide)) return;
        MotionClock.Stop(slide);
        slide.Finish();
    }

    private static void Start(UIElement page, double from, double to, double fling, SpringSpec spec, bool vertical, FrameworkElement? clip)
    {
        if (MotionTransform.For(page) is not { } parts) return;
        if (Slides.TryGetValue(page, out var old))
        {
            MotionClock.Stop(old);
            from = old.Value; // reprise d'un glissement interrompu : on part d'où il en est
            old.ResetOffset();
        }
        var slide = new Slide(parts, from, to, fling, spec, vertical, clip);
        Slides.AddOrUpdate(page, slide);
        MotionClock.Start(slide);
    }

    private sealed class Slide : IMotion
    {
        private readonly MotionParts _parts;
        private readonly Spring _spring;
        private readonly bool _vertical;
        private readonly bool _returns; // page qui part : remise à 0 une fois finie (elle est cachée)
        private readonly FrameworkElement? _clip;

        public Slide(MotionParts parts, double from, double to, double fling, SpringSpec spec, bool vertical, FrameworkElement? clip)
        {
            _parts = parts;
            _vertical = vertical;
            _returns = to != 0;
            _clip = clip;
            _spring = new Spring(from);
            _spring.SetTarget(to, spec);
            _spring.Impulse(fling);
            if (clip != null) clip.ClipToBounds = true;
            Apply();
        }

        public double Value => _spring.Value;

        public bool Step(double dt)
        {
            bool done = _spring.Step(dt);
            Apply();
            if (done) Finish();
            return done;
        }

        public void Finish()
        {
            _spring.Snap();
            ResetOffset();
            if (_clip != null) _clip.ClipToBounds = false;
        }

        public void ResetOffset()
        {
            if (_returns || _spring.Value == 0) _parts.Slide.X = _parts.Slide.Y = 0;
        }

        private void Apply()
        {
            if (_vertical) _parts.Slide.Y = _spring.Value;
            else _parts.Slide.X = _spring.Value;
        }
    }
}

/// <summary>
/// Effet élastique au bout d'une rangée (refonte O3) : molette au-delà du premier / dernier élément = la rangée cède un peu
/// (jamais plus de <see cref="Kinetics.ElasticMax"/> DIP, courbe « caoutchouc ») puis revient en rebondissant.
/// </summary>
public static class Elastic
{
    private static readonly ConditionalWeakTable<UIElement, Bounce> Bounces = new();

    /// <summary>Courbe « caoutchouc » : suit d'abord le geste puis cède de moins en moins ; |résultat| &lt; <paramref name="max"/>.</summary>
    public static double Rubber(double offset, double max)
    {
        if (max <= 0 || double.IsNaN(offset)) return 0;
        double magnitude = Math.Abs(offset);
        return Math.Sign(offset) * max * (1 - 1 / (1 + magnitude / max));
    }

    /// <param name="push">Sens et force du geste (cran de molette ≈ ±1) : positif = vers la droite / le bas.</param>
    public static void Kick(UIElement element, double push, bool vertical)
    {
        if (!MotionPrefs.Rich || push == 0 || MotionTransform.For(element) is not { } parts) return;
        var bounce = Bounces.GetValue(element, _ => new Bounce(parts));
        bounce.Push(push * Kinetics.ElasticGain * Kinetics.ElasticMax * 10, vertical);
        MotionClock.Start(bounce);
    }

    private sealed class Bounce(MotionParts parts) : IMotion
    {
        private readonly Spring _spring = new(0);
        private bool _vertical;

        public void Push(double velocity, bool vertical)
        {
            _vertical = vertical;
            _spring.SetTarget(0, Springs.Elastic);
            _spring.Impulse(velocity);
        }

        public bool Step(double dt)
        {
            bool done = _spring.Step(dt);
            Apply();
            return done;
        }

        public void Finish()
        {
            _spring.Snap();
            Apply();
        }

        private void Apply()
        {
            double shown = Rubber(_spring.Value, Kinetics.ElasticMax);
            if (_vertical) parts.Slide.Y = shown;
            else parts.Slide.X = shown;
        }
    }
}
