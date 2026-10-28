using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Shader du fond animé « Aurore » (Themes/Shaders/AnimatedAurora.fx, ps_3_0 : rendu par le GPU seulement).
/// Il dessine tout lui-même ; on lui donne le format de l'écran, le temps et l'ambiance de couleurs.
/// </summary>
public sealed class AnimatedAuroraEffect : ShaderEffect
{
    private static readonly PixelShader? Shader = Load();

    public static bool IsAvailable => Shader != null;

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(nameof(Input), typeof(AnimatedAuroraEffect), 0);
    private static readonly DependencyProperty InfoProperty = DependencyProperty.Register("Info", typeof(Point4D), typeof(AnimatedAuroraEffect),
        new UIPropertyMetadata(new Point4D(16.0 / 9, 0, 0, 0), PixelShaderConstantCallback(0)));
    private static readonly DependencyProperty DeepProperty = RegisterColor("Deep", 1);
    private static readonly DependencyProperty BaseProperty = RegisterColor("Base", 2);
    private static readonly DependencyProperty Accent1Property = RegisterColor("Accent1", 3);
    private static readonly DependencyProperty Accent2Property = RegisterColor("Accent2", 4);
    private static readonly DependencyProperty GlowProperty = RegisterColor("Glow", 5);

    public AnimatedAuroraEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        SetMood(Wallpapers.Night);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <param name="aspect">Largeur / hauteur de l'écran.</param>
    /// <param name="time">Temps de l'animation (secondes × vitesse).</param>
    public void SetFrame(double aspect, double time) => SetValue(InfoProperty, new Point4D(aspect, time, 0, 0));

    public void SetMood(Mood mood)
    {
        SetValue(DeepProperty, mood.Deep);
        SetValue(BaseProperty, mood.Base);
        SetValue(Accent1Property, mood.Accent1);
        SetValue(Accent2Property, mood.Accent2);
        SetValue(GlowProperty, mood.Glow);
    }

    private static DependencyProperty RegisterColor(string name, int register) =>
        DependencyProperty.Register(name, typeof(Color), typeof(AnimatedAuroraEffect),
            new UIPropertyMetadata(Colors.Black, PixelShaderConstantCallback(register)));

    private static PixelShader? Load()
    {
        try
        {
            var shader = new PixelShader { UriSource = new Uri("pack://application:,,,/Themes/Shaders/AnimatedAurora.ps") };
            shader.Freeze();
            return shader;
        }
        catch (Exception ex)
        {
            Log.Error("Fond animé : chargement du shader", ex);
            return null;
        }
    }
}
