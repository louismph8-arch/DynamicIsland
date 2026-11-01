using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Animation;

/// <summary>
/// Transformations d'un élément partagées par les mouvements (O3) — chacun a la sienne, elles se cumulent sans se gêner :
/// arrivée en cascade (échelle + décalage), toucher (survol / appui), inclinaison, glissement de page.
/// </summary>
public sealed class MotionParts
{
    public readonly ScaleTransform Arrive = new();
    public readonly TranslateTransform ArriveShift = new();
    public readonly ScaleTransform Touch = new();
    public readonly SkewTransform Skew = new();
    public readonly TranslateTransform TouchShift = new();
    public readonly TranslateTransform Slide = new();
    public readonly TransformGroup Group;

    public MotionParts() => Group = new TransformGroup { Children = { Arrive, Touch, Skew, ArriveShift, TouchShift, Slide } };
}

public static class MotionTransform
{
    private static readonly ConditionalWeakTable<UIElement, MotionParts> Parts = new();

    /// <summary>
    /// Les transformations de l'élément, posées au besoin. null si l'élément a déjà une transformation à quelqu'un d'autre
    /// (animation d'une vue, rotation…) : on n'y touche pas, le mouvement se fait sans elle.
    /// </summary>
    public static MotionParts? For(UIElement element)
    {
        var current = element.RenderTransform;
        if (Parts.TryGetValue(element, out var parts))
        {
            if (ReferenceEquals(current, parts.Group)) return parts;
            if (!ReferenceEquals(current, Transform.Identity) && current != null) return null; // remplacée entre-temps
        }
        else if (current != null && !ReferenceEquals(current, Transform.Identity))
        {
            return null;
        }

        parts ??= new MotionParts();
        Parts.AddOrUpdate(element, parts);
        element.RenderTransform = parts.Group;
        if (element.ReadLocalValue(UIElement.RenderTransformOriginProperty) == DependencyProperty.UnsetValue && element.RenderTransformOrigin == default)
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        return parts;
    }

    /// <summary>Les transformations déjà posées (sans en créer).</summary>
    public static MotionParts? Existing(UIElement element) =>
        Parts.TryGetValue(element, out var parts) && ReferenceEquals(element.RenderTransform, parts.Group) ? parts : null;
}
