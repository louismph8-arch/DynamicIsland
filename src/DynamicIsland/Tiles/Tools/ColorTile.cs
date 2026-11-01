using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Tuile Couleur : clic = pipette (<see cref="ColorPicker"/>). Montre la dernière couleur choisie (pastille + HEX) et, en W / L,
/// les couleurs d'avant : un clic sur l'une d'elles la recopie.
/// </summary>
internal sealed class ColorFace : ToolFace
{
    private int _revision = -1;
    private DateTime _copiedUntil = DateTime.MinValue;
    private string _copied = "";

    public ColorFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context) { }

    protected override void Update()
    {
        var colors = ColorHistory.All;
        if (_revision != ColorHistory.Revision) BuildAccessory(colors);
        _revision = ColorHistory.Revision;
        string line;
        if (DateTime.UtcNow < _copiedUntil) line = Loc.F("tiles.color.copied", _copied);
        else if (colors.Count == 0) line = Loc.T("tiles.color.hint.short");
        else line = TileSize == TileSize.L ? ColorHistory.Rgb(colors[0]) : ColorHistory.Hex(colors[0]);
        SetTone(Icons.Pipette, ColorPicker.IsOpen ? ToolTone.Accent : ToolTone.Idle);
        SetTexts(null, line);
    }

    private void BuildAccessory(IReadOnlyList<Color> colors)
    {
        if (colors.Count == 0)
        {
            SetAccessory(null);
            return;
        }
        var latest = colors[0];
        switch (TileSize)
        {
            case TileSize.S:
                SetAccessory(Swatch(latest, ToolTileShape.SwatchSmall, new CornerRadius(ToolTileShape.SwatchSmall / 2), copyable: false));
                break;
            case TileSize.W:
            {
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var big = Swatch(latest, ToolTileShape.SwatchLarge, Corners.M, copyable: false);
                big.HorizontalAlignment = HorizontalAlignment.Right;
                stack.Children.Add(big);
                if (History(colors, ToolTileShape.History - 1) is { } row)
                {
                    row.Margin = new Thickness(0, Space.S3, 0, 0);
                    row.HorizontalAlignment = HorizontalAlignment.Right;
                    stack.Children.Add(row);
                }
                SetAccessory(stack);
                break;
            }
            default:
            {
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var hero = Swatch(latest, ToolTileShape.SwatchHero, Corners.XL, copyable: false);
                hero.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(hero);
                var hex = new TextBlock { Text = ColorHistory.Hex(latest), FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S2, 0, 0) };
                hex.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
                stack.Children.Add(hex);
                if (History(colors, ToolTileShape.History) is { } row)
                {
                    row.Margin = new Thickness(0, Space.S2, 0, 0);
                    row.HorizontalAlignment = HorizontalAlignment.Center;
                    stack.Children.Add(row);
                }
                SetAccessory(stack);
                break;
            }
        }
    }

    /// <summary>Les couleurs d'avant (la dernière exclue), petites pastilles rondes cliquables.</summary>
    private static StackPanel? History(IReadOnlyList<Color> colors, int count)
    {
        if (colors.Count < 2) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in colors.Skip(1).Take(count))
        {
            var swatch = Swatch(color, ToolTileShape.HistorySwatch, new CornerRadius(ToolTileShape.HistorySwatch / 2), copyable: true);
            if (row.Children.Count > 0) swatch.Margin = new Thickness(ToolTileShape.HistoryGap, 0, 0, 0);
            row.Children.Add(swatch);
        }
        return row;
    }

    private static SquircleBorder Swatch(Color color, double size, CornerRadius corners, bool copyable)
    {
        var swatch = new SquircleBorder { Width = size, Height = size, CornerRadius = corners, Background = Paint.Frozen(color), BorderThickness = Borders.Hairline };
        swatch.SetResourceReference(Border.BorderBrushProperty, Paint.Key(InkRole.Divider));
        if (copyable)
        {
            swatch.Tag = color;
            swatch.ToolTip = Loc.F("tiles.color.copy", ColorHistory.Hex(color));
        }
        return swatch;
    }

    /// <summary>Clic sur une couleur d'avant : elle est recopiée (et repasse en tête) ; ailleurs, la pipette.</summary>
    protected override bool OnTileClick(Point position)
    {
        for (var node = InputHitTest(position) as DependencyObject; node != null && node != this; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not FrameworkElement { Tag: Color color }) continue;
            if (ColorHistory.Copy(color))
            {
                _copied = ColorHistory.Hex(color);
                _copiedUntil = DateTime.UtcNow + CopiedShown;
            }
            Refresh();
            return true;
        }
        return false;
    }

    private static readonly TimeSpan CopiedShown = TimeSpan.FromSeconds(2.5);
}
