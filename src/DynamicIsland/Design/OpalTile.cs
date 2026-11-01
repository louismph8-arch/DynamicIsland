using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>
/// Tuiles en verre opalin (O1) à l'intérieur des onglets de l'île : petites surfaces laiteuses teintées d'accent, bordure irisée
/// partagée (orientée par la lumière de l'île), et lueur opaline douce sous la tuile survolée. En XAML :
/// <c>Background="{DynamicResource OpalTileBrush}"</c> + <c>d:OpalTile.Glow="True"</c> ; en code : <see cref="Apply"/>.
/// La lueur n'existe que pendant le survol (l'effet est retiré ensuite : rien à dessiner au repos).
/// </summary>
public static class OpalTile
{
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow", typeof(bool), typeof(OpalTile), new PropertyMetadata(false, OnGlowChanged));

    public static bool GetGlow(DependencyObject element) => (bool)element.GetValue(GlowProperty);
    public static void SetGlow(DependencyObject element, bool value) => element.SetValue(GlowProperty, value);

    /// <summary>Couleur de la lueur (sinon l'accent).</summary>
    public static readonly DependencyProperty GlowColorProperty = DependencyProperty.RegisterAttached(
        "GlowColor", typeof(Color?), typeof(OpalTile), new PropertyMetadata(null));

    public static Color? GetGlowColor(DependencyObject element) => (Color?)element.GetValue(GlowColorProperty);
    public static void SetGlowColor(DependencyObject element, Color? value) => element.SetValue(GlowColorProperty, value);

    /// <summary>Tuile construite en code : fond laiteux, bordure irisée fine, lueur au survol.</summary>
    public static T Apply<T>(T tile, bool glow = true) where T : Border
    {
        tile.SetResourceReference(Border.BackgroundProperty, OpalTheme.TileKey);
        tile.SetResourceReference(Border.BorderBrushProperty, OpalTheme.TileRimKey);
        tile.BorderThickness = Borders.Hairline;
        if (glow) SetGlow(tile, true);
        return tile;
    }

    /// <summary>Survol d'une tuile construite en code : fond un peu plus clair (à brancher à la place d'un ancien Paint.Card).</summary>
    public static void Hover(Border tile, bool over) =>
        tile.SetResourceReference(Border.BackgroundProperty, over ? OpalTheme.TileHoverKey : OpalTheme.TileKey);

    private static void OnGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.MouseEnter -= OnEnter;
        element.MouseLeave -= OnLeave;
        if ((bool)e.NewValue)
        {
            element.MouseEnter += OnEnter;
            element.MouseLeave += OnLeave;
        }
    }

    private static void OnEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not UIElement element || !OpalTheme.EffectsOn) return;
        // Une tuile qui porte déjà un effet (rare) le garde : un seul Effect par élément.
        if (element.Effect != null && element.Effect is not DropShadowEffect) return;
        var look = OpalTheme.Look;
        var effect = element.Effect as DropShadowEffect ?? new DropShadowEffect { ShadowDepth = 0, RenderingBias = RenderingBias.Performance, Opacity = 0 };
        effect.Color = GetGlowColor(element) ?? AppearanceService.AccentMid;
        effect.BlurRadius = look.GlowBlur * Opal.TileGlowBlur;
        element.Effect = effect;
        effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(look.GlowOpacity * Opal.TileGlow, Opal.GlowFade) { EasingFunction = Ease });
    }

    private static void OnLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not UIElement { Effect: DropShadowEffect effect } element) return;
        var fade = new DoubleAnimation(0, Opal.GlowFade) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            if (element.IsMouseOver || element.Effect != effect) return;
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            element.Effect = null; // plus rien à dessiner au repos
        };
        effect.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>--snapshot : lueur allumée sans animation.</summary>
    public static void ShowGlow(UIElement element, bool on)
    {
        if (!on)
        {
            element.Effect = null;
            return;
        }
        var look = OpalTheme.Look;
        element.Effect = new DropShadowEffect
        {
            ShadowDepth = 0, RenderingBias = RenderingBias.Performance, Color = GetGlowColor(element) ?? AppearanceService.AccentMid,
            BlurRadius = look.GlowBlur * Opal.TileGlowBlur, Opacity = look.GlowOpacity * Opal.TileGlow,
        };
    }
}
