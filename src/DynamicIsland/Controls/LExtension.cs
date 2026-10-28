using System.Windows.Markup;

namespace DynamicIsland.Controls;

/// <summary>Texte traduit en XAML : <c>Text="{ui:L settings.general.title}"</c> (voir <see cref="Loc"/>).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
