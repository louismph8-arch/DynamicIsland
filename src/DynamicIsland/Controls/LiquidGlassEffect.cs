using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace DynamicIsland.Controls;

/// <summary>
/// Pixel shader du verre liquide (Themes/Shaders/LiquidGlass.fx, ps_2_0). Les registres sont remplis par
/// <see cref="Update"/>, qui précalcule tout ce qui peut l'être (le shader est limité à 64 instructions).
/// </summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly PixelShader? Shader = Load();

    /// <summary>Faux si le shader n'a pas pu être chargé : le verre est alors simplement désactivé.</summary>
    public static bool IsAvailable => Shader != null;

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0);
    private static readonly DependencyProperty ShapeProperty = Register("Shape", 0);
    private static readonly DependencyProperty LightProperty = Register("Light", 1);
    private static readonly DependencyProperty OpticsProperty = Register("Optics", 2);
    private static readonly DependencyProperty InnerProperty = Register("Inner", 3);
    private static readonly DependencyProperty SpotProperty = Register("Spot", 4);

    public LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <param name="size">Taille de l'élément (DIP).</param>
    /// <param name="shape">Forme du verre dans le repère de l'élément ; elle peut déborder (côtés sans liseré).</param>
    /// <param name="radius">Rayon des coins de la forme.</param>
    /// <param name="light">Position de la lumière dans le repère de l'élément.</param>
    public void Update(Size size, Rect shape, double radius, Point light, GlassLook look)
    {
        if (size.Width < 1 || size.Height < 1) return;
        double band = Math.Max(1, look.Band);
        // Rayon au moins égal à la bande : la normale reste définie partout où le bord est visible.
        double r = Math.Max(Math.Min(radius, Math.Min(shape.Width, shape.Height) / 2), band);
        var center = new Point(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
        var spot = new Point(Math.Clamp(light.X, 0, size.Width), Math.Clamp(light.Y, 0, size.Height));

        Set(ShapeProperty, new Point4D(size.Width, size.Height, r, 1 / band));
        Set(LightProperty, new Point4D(light.X, light.Y, look.Rim, look.Sheen));
        Set(OpticsProperty, new Point4D(look.Refraction, look.Aberration,
            -1.4426950408889634 / Math.Max(1, look.SheenRadius * look.SheenRadius), look.TopLight));
        Set(InnerProperty, new Point4D(shape.Width / 2 - r, shape.Height / 2 - r, 1 / size.Width, 1 / size.Height));
        Set(SpotProperty, new Point4D(spot.X, spot.Y, center.X, center.Y));
    }

    /// <summary>Ne touche au registre que s'il change : chaque changement redessine la fenêtre.</summary>
    private void Set(DependencyProperty property, Point4D value)
    {
        if ((Point4D)GetValue(property) != value) SetValue(property, value);
    }

    private static DependencyProperty Register(string name, int register) =>
        DependencyProperty.Register(name, typeof(Point4D), typeof(LiquidGlassEffect),
            new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(register)));

    private static PixelShader? Load()
    {
        try
        {
            var shader = new PixelShader { UriSource = new Uri("pack://application:,,,/Themes/Shaders/LiquidGlass.ps") };
            shader.Freeze();
            return shader;
        }
        catch (Exception ex)
        {
            Log.Error("Verre liquide : chargement du shader", ex);
            return null;
        }
    }
}

/// <summary>Réglages du verre au moment du rendu (tokens × intensité × proximité du curseur).</summary>
public readonly record struct GlassLook(double Band, double Refraction, double Aberration, double Rim, double Sheen,
    double SheenRadius, double TopLight);
