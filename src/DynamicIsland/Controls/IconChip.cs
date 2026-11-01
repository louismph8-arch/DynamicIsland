using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Icône Lucide avec son état « actif » (O4). Lucide n'a pas de variantes pleines : une icône active =
/// glyphe en dégradé d'accent + petite pastille squircle teintée d'accent derrière (style partagé « ActiveIcon »
/// de Themes/Styles.xaml, appliqué d'office à chaque IconChip). Au repos : le glyphe prend la couleur héritée
/// (texte secondaire dans les onglets et les cases), sans pastille.
/// </summary>
public sealed class IconChip : Control
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(IconChip), new PropertyMetadata(""));
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(IconChip), new PropertyMetadata(false));
    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(IconChip), new PropertyMetadata(IconSize.M));
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(IconChip), new PropertyMetadata(Corners.IconChip));
    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(nameof(Tint), typeof(double), typeof(IconChip), new PropertyMetadata(Interaction.ActiveTint));

    static IconChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconChip), new FrameworkPropertyMetadata(typeof(IconChip)));
        FocusableProperty.OverrideMetadata(typeof(IconChip), new FrameworkPropertyMetadata(false));
    }

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    /// <summary>Opacité de la pastille d'accent quand l'icône est active.</summary>
    public double Tint { get => (double)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    /// <summary>
    /// Même rendu pour un glyphe construit en C# (cases de la zone d'état du dock) : glyphe en dégradé d'accent et
    /// pastille <paramref name="halo"/> visible quand actif ; sinon couleur <paramref name="idle"/>.
    /// </summary>
    public static void Paint(TextBlock glyph, FrameworkElement? halo, bool active, Brush idle)
    {
        if (active) glyph.SetResourceReference(TextBlock.ForegroundProperty, Services.AppearanceService.AccentGradientKey);
        else glyph.Foreground = idle;
        if (halo != null) halo.Opacity = active ? Interaction.ActiveTint : 0;
    }

    /// <summary>Pastille d'accent à poser derrière un glyphe construit en C# (cachée au départ).</summary>
    public static SquircleBorder CreateHalo(CornerRadius corners)
    {
        var halo = new SquircleBorder { CornerRadius = corners, Opacity = 0, IsHitTestVisible = false };
        halo.SetResourceReference(Border.BackgroundProperty, Services.AppearanceService.AccentBrushKey);
        return halo;
    }
}
