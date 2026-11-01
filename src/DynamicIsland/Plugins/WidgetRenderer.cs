using MemoryStream = System.IO.MemoryStream;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Plugins;

/// <summary>
/// Widget des potes dessiné en WPF à sa taille de base (l'hôte le met à l'échelle dans un Viewbox). Le rendu et chaque mise
/// à jour sont protégés : la moindre erreur remplace le contenu par une case « Widget indisponible », jamais d'exception
/// qui remonte. Le contenu est découpé à sa taille : un widget ne peut rien dessiner en dehors de sa case.
/// </summary>
public sealed class RenderedWidget
{
    private readonly List<Action<IWidgetData>> _updates = [];
    private readonly string _id;

    private RenderedWidget(string id, double width, double height)
    {
        _id = id;
        Root = new Grid { Width = width, Height = height, ClipToBounds = true };
    }

    /// <summary>Case du widget (taille de base, sans fond : l'hôte pose la carte).</summary>
    public Grid Root { get; }

    public bool Failed { get; private set; }

    /// <summary>Dessine un widget ; en cas d'erreur, la case « Widget indisponible ».</summary>
    public static RenderedWidget Render(WidgetPackage package, IWidgetData? initial = null)
    {
        var definition = package.Definition;
        var widget = new RenderedWidget(definition.Id, definition.Width, definition.Height);
        try
        {
            var builder = new Builder(package, widget._updates);
            var content = builder.Build(definition.Root);
            if (definition.Card) content.Margin = new Thickness(PluginShape.Padding);
            widget.Root.Children.Add(content);
            if (initial != null) widget.Update(initial);
        }
        catch (Exception ex)
        {
            widget.Fail(ex);
        }
        return widget;
    }

    /// <summary>Remet les textes et les jauges à jour (protégé).</summary>
    public void Update(IWidgetData data)
    {
        if (Failed) return;
        try
        {
            foreach (var update in _updates) update(data);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception ex)
    {
        Failed = true;
        _updates.Clear();
        Log.Error($"Widget des potes « {_id} » : rendu impossible, case « Widget indisponible »", ex);
        try
        {
            Root.Children.Clear();
            Root.Children.Add(Unavailable());
        }
        catch (Exception inner)
        {
            Log.Error("Widget des potes : case « indisponible »", inner);
        }
    }

    /// <summary>Case grise « Widget indisponible » (aussi pour un widget dont le fichier ne se relit plus).</summary>
    public static FrameworkElement Unavailable()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = Icons.Alert,
            FontFamily = FontStack.Icons,
            FontSize = IconSize.XXL,
            Foreground = Paint.TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Loc.T("plugins.unavailable"),
            FontSize = TypeRamp.Footnote,
            FontWeight = FontWeights.SemiBold,
            Foreground = Paint.TextSecondary,
            Margin = new Thickness(0, Space.S2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return stack;
    }

    /// <summary>Fabrique les contrôles WPF d'après la définition (déjà vérifiée par le lecteur).</summary>
    private sealed class Builder(WidgetPackage package, List<Action<IWidgetData>> updates)
    {
        public FrameworkElement Build(WidgetElement element)
        {
            FrameworkElement built = element.Type switch
            {
                WidgetElementType.Stack => Stack(element),
                WidgetElementType.Grid => GridOf(element),
                WidgetElementType.Text => Text(element),
                WidgetElementType.Icon => Icon(element),
                WidgetElementType.Image => Image(element),
                WidgetElementType.Ring => Ring(element),
                WidgetElementType.Bar => Bar(element),
                _ => new Border { Width = element.Size, Height = element.Size },
            };
            built.HorizontalAlignment = element.Align switch
            {
                "center" => HorizontalAlignment.Center,
                "end" => HorizontalAlignment.Right,
                "stretch" => HorizontalAlignment.Stretch,
                _ => element.Type is WidgetElementType.Stack or WidgetElementType.Grid or WidgetElementType.Bar ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            };
            return built;
        }

        private FrameworkElement Stack(WidgetElement element)
        {
            var panel = new StackPanel { Orientation = element.Horizontal ? Orientation.Horizontal : Orientation.Vertical };
            bool first = true;
            foreach (var child in element.Children)
            {
                var built = Build(child);
                if (!first) built.Margin = element.Horizontal ? new Thickness(element.Gap, 0, 0, 0) : new Thickness(0, element.Gap, 0, 0);
                if (element.Horizontal)
                {
                    built.VerticalAlignment = child.Align switch
                    {
                        "start" => VerticalAlignment.Top,
                        "end" => VerticalAlignment.Bottom,
                        _ => VerticalAlignment.Center,
                    };
                    if (child.Align is "end" or "center") built.HorizontalAlignment = HorizontalAlignment.Left;
                }
                panel.Children.Add(built);
                first = false;
            }
            return Padded(panel, element.Padding);
        }

        private FrameworkElement GridOf(WidgetElement element)
        {
            var grid = new UniformGrid { Columns = element.Columns, Margin = new Thickness(-element.Gap / 2) };
            foreach (var child in element.Children)
            {
                var built = Build(child);
                var cell = new Border { Margin = new Thickness(element.Gap / 2), Child = built };
                built.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(cell);
            }
            return Padded(new Border { Child = grid }, element.Padding);
        }

        private static FrameworkElement Padded(FrameworkElement content, double padding) =>
            padding > 0 ? new Border { Padding = new Thickness(padding), Child = content } : content;

        private FrameworkElement Text(WidgetElement element)
        {
            var text = new TextBlock
            {
                FontSize = element.Size,
                FontFamily = element.Font == "display" ? FontStack.Display : FontStack.Ui,
                FontWeight = element.Weight switch
                {
                    "light" => FontWeights.Light,
                    "semibold" => FontWeights.SemiBold,
                    "bold" => FontWeights.Bold,
                    _ => FontWeights.Normal,
                },
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = element.Align switch { "center" => TextAlignment.Center, "end" => TextAlignment.Right, _ => TextAlignment.Left },
            };
            if (element.Font == "numbers") // grands chiffres (O4) : Inter Display, chiffres à largeur fixe, graisses embarquées seulement
            {
                Numerals.Apply(text, element.Size);
                if (element.Weight is "semibold" or "bold") text.FontWeight = NumberWeight.Text;
                else if (element.Weight == "light") text.FontWeight = NumberWeight.Light;
            }
            if (element.MaxLines > 1)
            {
                text.TextWrapping = TextWrapping.Wrap;
                text.MaxHeight = element.Size * PluginShape.LineHeight * element.MaxLines;
            }
            WidgetBrush.Set(text, TextBlock.ForegroundProperty, element.Color);
            var template = element.Text ?? "";
            if (template.Contains('{')) updates.Add(data => SetText(text, WidgetTemplate.Fill(template, data)));
            else text.Text = template;
            return text;
        }

        private FrameworkElement Icon(WidgetElement element)
        {
            var icon = new TextBlock { FontFamily = FontStack.Icons, FontSize = element.Size, VerticalAlignment = VerticalAlignment.Center };
            WidgetBrush.Set(icon, TextBlock.ForegroundProperty, element.Color);
            var name = element.Icon ?? "";
            if (name.Contains('{'))
            {
                // Gabarit (« {weather.icon} ») : seulement un caractère de la police d'icônes, sinon une icône neutre.
                updates.Add(data =>
                {
                    var glyph = WidgetTemplate.Fill(name, data);
                    SetText(icon, glyph.Length == 1 && glyph[0] is >= '' and <= '' ? glyph : Icons.Widgets);
                });
            }
            else icon.Text = LucideNames.Glyph(name) ?? Icons.Widgets;
            return icon;
        }

        private FrameworkElement Image(WidgetElement element)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = (int)Math.Min(WidgetPackageReader.MaxImagePixels, Math.Ceiling(element.Width * PluginShape.ImageOversample));
            bitmap.StreamSource = new MemoryStream(package.Images[element.Src!], writable: false);
            bitmap.EndInit();
            bitmap.Freeze();
            var image = new SquircleBorder
            {
                Width = element.Width,
                Height = element.Height,
                CornerRadius = element.Round ? Corners.XL : new CornerRadius(0),
                Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        private FrameworkElement Ring(WidgetElement element)
        {
            double size = element.Size, thickness = element.Thickness;
            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(new Ellipse { Stroke = Paint.Track, StrokeThickness = thickness });
            var arc = new System.Windows.Shapes.Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            WidgetBrush.Set(arc, Shape.StrokeProperty, element.Color);
            grid.Children.Add(arc);
            TextBlock? label = null;
            if (element.Text is { } template)
            {
                // Valeur au centre de l'anneau : police des chiffres (O4), chiffres à largeur fixe
                label = Numerals.Apply(new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = size - 2 * thickness,
                    Foreground = Paint.TextPrimary,
                }, Math.Max(TypeRamp.Caption2, size * PluginShape.RingLabel));
                grid.Children.Add(label);
                if (!template.Contains('{')) label.Text = template;
            }
            double shown = double.NaN;
            updates.Add(data =>
            {
                double fraction = Fraction(element, data);
                if (fraction != shown)
                {
                    shown = fraction;
                    arc.Data = ArcGeometry(size, thickness, fraction);
                }
                if (label != null && element.Text!.Contains('{')) SetText(label, WidgetTemplate.Fill(element.Text, data));
            });
            return grid;
        }

        private FrameworkElement Bar(WidgetElement element)
        {
            var bar = new Grid { Height = element.Thickness };
            if (element.Width > 0) bar.Width = element.Width;
            bar.Children.Add(new SquircleBorder { CornerRadius = Corners.Pill, Background = Paint.Track });
            var fill = new SquircleBorder { CornerRadius = Corners.Pill, RenderTransformOrigin = new Point(0, 0.5) };
            var scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            WidgetBrush.Set(fill, Border.BackgroundProperty, element.Color);
            bar.Children.Add(fill);
            updates.Add(data => scale.ScaleX = Fraction(element, data));
            return bar;
        }

        private static double Fraction(WidgetElement element, IWidgetData data) =>
            data.Number(element.Value!) is { } value && !double.IsNaN(value) ? Math.Clamp((value - element.Min) / (element.Max - element.Min), 0, 1) : 0;

        /// <summary>Arc d'anneau depuis le haut, dans le sens des aiguilles d'une montre.</summary>
        private static Geometry ArcGeometry(double size, double thickness, double fraction)
        {
            if (fraction <= 0) return Geometry.Empty;
            double radius = (size - thickness) / 2, center = size / 2;
            if (fraction >= 0.9999) return new EllipseGeometry(new Point(center, center), radius, radius);
            double angle = fraction * 2 * Math.PI;
            var start = new Point(center, center - radius);
            var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            return geometry;
        }

        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }
    }

    /// <summary>Couleurs des widgets : accent d'Opaline (suit le profil), couleurs du thème, ou #RRGGBB / #AARRGGBB.</summary>
    private static class WidgetBrush
    {
        public static void Set(FrameworkElement element, DependencyProperty property, string color)
        {
            switch (color)
            {
                case "accent":
                    element.SetResourceReference(property, AppearanceService.AccentGradientKey);
                    return;
                case "accent-start":
                    element.SetValue(property, Paint.Frozen(AppearanceService.AccentStart));
                    return;
                case "accent-end":
                    element.SetValue(property, Paint.Frozen(AppearanceService.AccentEnd));
                    return;
            }
            element.SetValue(property, color switch
            {
                "secondary" => Paint.TextSecondary,
                "tertiary" => Paint.TextTertiary,
                "white" => Paint.White,
                "red" => Paint.Danger,
                "orange" => Paint.Timer,
                "green" => Paint.Break,
                "text" => Paint.TextPrimary,
                _ => Paint.Frozen((Color)ColorConverter.ConvertFromString(color)),
            });
        }
    }
}
