using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using DynamicIsland.Themes;

namespace DynamicIsland.Animation;

/// <summary>
/// Contenu qui arrive en cascade (refonte O3) : quand l'île s'ouvre, chaque élément de l'onglet (puis, en O6, chaque tuile d'une
/// page) arrive à son tour (décalage <see cref="Motion.CascadeStagger"/>) avec un petit ressort d'échelle 0,94 → 1, un décalage
/// de quelques DIP et un flou qui devient net ; à la fermeture, l'inverse, plus vite et dans l'autre sens.
/// <para>Éléments : les enfants du premier panneau du contenu qui en a plusieurs ; un panneau marqué
/// <c>a:Cascade.IsEnabled="True"</c> est « ouvert » (ses enfants arrivent un par un, et ainsi de suite). Le flou (BlurEffect)
/// n'est posé que sur un élément sans effet et retiré dès qu'il est net ; l'opacité seulement si personne d'autre ne la règle ;
/// la transformation seulement si l'élément n'en a pas déjà une (<see cref="MotionTransform"/>). Fin = tout est remis comme
/// avant : rien ne reste à dessiner.</para>
/// </summary>
public static class Cascade
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Cascade), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly ConditionalWeakTable<FrameworkElement, Run> Runs = new();

    /// <summary>Pose d'un élément à l'avancement <paramref name="t"/> (0 = départ, 1 = posé ; le ressort peut dépasser 1).</summary>
    public readonly record struct Pose(double Scale, double Shift, double Blur, double Opacity);

    public static Pose PoseAt(double t)
    {
        double clamped = Math.Clamp(t, 0, 1);
        return new Pose(
            Kinetics.CascadeScale + (1 - Kinetics.CascadeScale) * t,
            Kinetics.CascadeShift * (1 - t),
            Kinetics.CascadeBlur * (1 - clamped),
            Math.Clamp(t / Kinetics.CascadeOpaqueAt, 0, 1));
    }

    /// <summary>Délai (s) avant que l'élément <paramref name="index"/> sur <paramref name="count"/> parte (calcul pur, --self-test).</summary>
    public static double DelayFor(int index, int count, bool reverse)
    {
        if (reverse)
        {
            int fromEnd = Math.Min(Math.Max(count - 1 - index, 0), Kinetics.CascadeMaxItems - 1);
            return fromEnd * Motion.CascadeReverseStagger.TotalSeconds;
        }
        return Motion.CascadeStart.TotalSeconds + Math.Min(index, Kinetics.CascadeMaxItems - 1) * Motion.CascadeStagger.TotalSeconds;
    }

    /// <summary>Ouverture (<paramref name="reverse"/> faux) ou fermeture du contenu <paramref name="view"/>.</summary>
    public static void Play(FrameworkElement view, bool reverse)
    {
        Runs.TryGetValue(view, out var old);
        if (old != null) MotionClock.Stop(old);
        if (!MotionPrefs.Rich)
        {
            old?.Restore();
            Runs.Remove(view);
            return;
        }
        var elements = ItemsOf(view);
        if (elements.Count == 0)
        {
            old?.Restore();
            Runs.Remove(view);
            return;
        }
        var run = new Run(elements, reverse, old);
        Runs.AddOrUpdate(view, run);
        MotionClock.Start(run);
    }

    /// <summary>Plus de cascade sur cette vue : tout est remis en place tout de suite.</summary>
    public static void Clear(FrameworkElement view)
    {
        if (!Runs.TryGetValue(view, out var run)) return;
        MotionClock.Stop(run);
        run.Restore();
        Runs.Remove(view);
    }

    /// <summary>Les éléments qui arrivent un par un, dans l'ordre de l'arbre.</summary>
    public static List<UIElement> ItemsOf(FrameworkElement view)
    {
        var list = new List<UIElement>();
        if (ContentRoot(view) is not { } root) return list;
        foreach (UIElement child in root.Children)
            if (child.Visibility == Visibility.Visible) Collect(child, list);
        // Ordre de lecture (de haut en bas, puis de gauche à droite), pas celui du XAML : une rangée posée en bas arrive en dernier.
        return list.OrderBy(e => Place(e, view).Row).ThenBy(e => Place(e, view).X).ToList();
    }

    private static (double Row, double X) Place(UIElement element, UIElement view)
    {
        try
        {
            var point = element.TranslatePoint(new Point(0, 0), view);
            return (Math.Round(point.Y / Kinetics.CascadeRow), point.X);
        }
        catch (InvalidOperationException)
        {
            return (0, 0);
        }
    }

    private static Panel? ContentRoot(DependencyObject node)
    {
        for (int depth = 0; depth < 8; depth++)
        {
            switch (node)
            {
                case Panel panel:
                    var visible = panel.Children.OfType<UIElement>().Where(c => c.Visibility == Visibility.Visible).ToList();
                    if (GetIsEnabled(panel) || visible.Count > 1) return panel;
                    if (visible.Count == 0) return null;
                    node = visible[0];
                    break;
                case ContentControl { Content: UIElement content }:
                    node = content;
                    break;
                case Decorator { Child: { } child }:
                    node = child;
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    private static void Collect(UIElement element, List<UIElement> list)
    {
        if (element is Panel panel && GetIsEnabled(panel))
        {
            foreach (UIElement child in panel.Children)
                if (child.Visibility == Visibility.Visible) Collect(child, list);
            return;
        }
        list.Add(element);
    }

    private sealed class Item
    {
        public required UIElement Element;
        public MotionParts? Parts;
        public required Spring Spring;
        public double Delay;
        public bool OwnOpacity;
        public BlurEffect? Blur;
    }

    private sealed class Run : IMotion
    {
        private readonly List<Item> _items = [];
        private double _elapsed;

        public Run(List<UIElement> elements, bool reverse, Run? previous)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var before = previous?._items.FirstOrDefault(it => ReferenceEquals(it.Element, element));
                var spring = new Spring(before?.Spring.Value ?? (reverse ? 100 : 0));
                spring.SetTarget(reverse ? 0 : 100, reverse ? Springs.CascadeOut : Springs.CascadeIn);
                var item = new Item
                {
                    Element = element,
                    Parts = MotionTransform.For(element),
                    Spring = spring,
                    Delay = DelayFor(i, elements.Count, reverse),
                    OwnOpacity = before?.OwnOpacity ?? OpacityIsFree(element),
                    Blur = before?.Blur,
                };
                if (item.Blur == null && element.Effect == null && !reverse)
                {
                    item.Blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
                    element.Effect = item.Blur;
                }
                _items.Add(item);
                Apply(item);
            }
            // Éléments de la cascade d'avant qui ne sont plus là (onglet changé) : remis en place.
            if (previous != null)
                foreach (var gone in previous._items.Where(it => !elements.Contains(it.Element))) Rest(gone);
        }

        public bool Step(double dt)
        {
            _elapsed += dt;
            bool done = true;
            foreach (var item in _items)
            {
                if (_elapsed < item.Delay)
                {
                    done = false;
                    continue;
                }
                if (!item.Spring.Step(dt)) done = false;
                Apply(item);
            }
            if (done) Restore();
            return done;
        }

        public void Finish()
        {
            foreach (var item in _items) item.Spring.Snap();
            Restore();
        }

        /// <summary>Tout remis comme avant la cascade (posé, net, opacité rendue à qui la règle).</summary>
        public void Restore()
        {
            foreach (var item in _items) Rest(item);
        }

        private static void Apply(Item item)
        {
            var pose = PoseAt(item.Spring.Value / 100);
            if (item.Parts is { } parts)
            {
                parts.Arrive.ScaleX = parts.Arrive.ScaleY = pose.Scale;
                parts.ArriveShift.Y = pose.Shift;
            }
            if (item.OwnOpacity) item.Element.Opacity = pose.Opacity;
            if (item.Blur is { } blur)
            {
                if (pose.Blur < 0.25)
                {
                    if (ReferenceEquals(item.Element.Effect, blur)) item.Element.Effect = null; // net : plus rien à flouter
                    item.Blur = null;
                }
                else blur.Radius = pose.Blur;
            }
        }

        private static void Rest(Item item)
        {
            if (item.Parts is { } parts)
            {
                parts.Arrive.ScaleX = parts.Arrive.ScaleY = 1;
                parts.ArriveShift.Y = 0;
            }
            if (item.OwnOpacity) item.Element.ClearValue(UIElement.OpacityProperty);
            if (item.Blur != null && ReferenceEquals(item.Element.Effect, item.Blur)) item.Element.Effect = null;
            item.Blur = null;
        }

        /// <summary>Opacité que personne d'autre ne règle (ni animation, ni liaison, ni valeur locale autre que 1).</summary>
        private static bool OpacityIsFree(UIElement element)
        {
            var source = DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty);
            if (source.IsAnimated || source.IsExpression || source.IsCoerced) return false;
            return source.BaseValueSource == BaseValueSource.Default;
        }
    }
}
