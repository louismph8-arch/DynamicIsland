using System.Windows;
using System.Windows.Markup;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Marges et remplissages pris dans l'échelle d'espacement des tokens (<see cref="Space"/>) :
/// <c>{ui:Space s4}</c> (partout), <c>{ui:Space s6,s3}</c> (horizontal, vertical),
/// <c>{ui:Space 0,s2,s4,0}</c> (gauche, haut, droite, bas). « px » = 1, « -s3 » = négatif.
/// </summary>
[MarkupExtensionReturnType(typeof(Thickness))]
public sealed class SpaceExtension : MarkupExtension
{
    private readonly Thickness _value;

    public SpaceExtension(string all) => _value = new Thickness(Space.Parse(all));

    public SpaceExtension(string horizontal, string vertical)
    {
        double h = Space.Parse(horizontal), v = Space.Parse(vertical);
        _value = new Thickness(h, v, h, v);
    }

    public SpaceExtension(string left, string top, string right, string bottom) =>
        _value = new Thickness(Space.Parse(left), Space.Parse(top), Space.Parse(right), Space.Parse(bottom));

    public override object ProvideValue(IServiceProvider serviceProvider) => _value;
}
