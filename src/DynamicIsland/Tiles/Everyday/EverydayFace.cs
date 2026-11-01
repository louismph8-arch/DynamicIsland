using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Live;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Base des tuiles vivantes « de tous les jours » (O6, lot A) : la tuile dessine sa propre vue (<see cref="Root"/>) à la place
/// de la vue simple, relue au plus une fois par seconde (page visible, île ouverte) par <see cref="Update"/>. Une erreur de
/// lecture n'arrête que cette tuile (journal une seule fois). Tout est en jetons (<see cref="EverydayTileShape"/>…) et en
/// brosses du thème (sombre / opale blanche).
/// </summary>
public abstract class EverydayFace : TileFace
{
    protected readonly Grid Root = new();
    protected readonly TileContext Context;
    private bool _failed;

    protected EverydayFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, null)
    {
        Context = context;
        Child = Root;
    }

    protected bool Small => TileSize == TileSize.S;
    protected bool Large => TileSize == TileSize.L;

    public sealed override void Refresh()
    {
        if (_failed) return;
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            _failed = true; // plus relue de la session : le journal ne se remplit pas toutes les secondes
            Log.Error($"Tuile « {Definition.Id} » : mise à jour", ex);
        }
    }

    /// <summary>Relit les données et met la vue à jour (ne redessine que ce qui a changé).</summary>
    protected abstract void Update();

    /// <summary>Ajoute des lignes à <see cref="Root"/> (hauteurs données) et rend la grille.</summary>
    protected Grid Rows(params GridLength[] heights)
    {
        foreach (var height in heights) Root.RowDefinitions.Add(new RowDefinition { Height = height });
        return Root;
    }

    protected static GridLength Auto => GridLength.Auto;
    protected static GridLength Star => new(1, GridUnitType.Star);

    protected static T Place<T>(T element, int row, int column = 0, int columnSpan = 1) where T : UIElement
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }
}

/// <summary>Petits morceaux communs des tuiles de tous les jours (textes, icônes, boutons, anneau), tous en jetons.</summary>
public static class TilePieces
{
    public static TextBlock Text(double size, InkRole role, FontWeight? weight = null, bool wrap = false)
    {
        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(role));
        return text;
    }

    /// <summary>Nom de la tuile (comme les tuiles simples).</summary>
    public static TextBlock Name(string text)
    {
        var name = Text(TypeRamp.Caption, InkRole.TextPrimary, FontWeights.SemiBold);
        name.Text = text;
        return name;
    }

    /// <summary>Ligne d'information (secondaire).</summary>
    public static TextBlock Info() => Text(TypeRamp.Caption2, InkRole.TextSecondary);

    /// <summary>Grand chiffre (police des chiffres, O4).</summary>
    public static TextBlock Number(double size, InkRole role = InkRole.TextPrimary)
    {
        var text = Numerals.Apply(new TextBlock { TextTrimming = TextTrimming.None }, size);
        text.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(role));
        return text;
    }

    public static TextBlock Glyph(string glyph, double size, InkRole role)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = FontStack.Icons,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(role));
        return text;
    }

    /// <summary>Pastille d'icône de la tuile (en haut à gauche), allumée pendant une activité.</summary>
    public static IconChip Chip(string glyph) => new()
    {
        Glyph = glyph,
        GlyphSize = IconSize.L,
        Width = Sizes.IconChip,
        Height = Sizes.IconChip,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Tint = Interaction.ActiveTintLight,
    };

    /// <summary>
    /// Petit bouton (squircle) dans une tuile : son appui ne déplace pas l'île et n'ouvre pas l'outil de la tuile. Survol =
    /// fond un peu plus clair ; <paramref name="selected"/> = fond « choisi ».
    /// </summary>
    public static SquircleBorder Button(UIElement content, string tooltip, Action click, double height, double minWidth)
    {
        var button = new SquircleBorder
        {
            Child = content,
            Height = height,
            MinWidth = minWidth,
            CornerRadius = Corners.M,
            Padding = new Thickness(Space.S2, 0, Space.S2, 0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        SetSelected(button, false);
        bool pressed = false;
        button.MouseEnter += (_, _) => PaintButton(button, hover: true);
        button.MouseLeave += (_, _) => PaintButton(button, hover: false);
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // ni glisser de l'île, ni ouverture de l'outil
            pressed = button.CaptureMouse();
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!pressed) return;
            pressed = false;
            var point = e.GetPosition(button);
            button.ReleaseMouseCapture();
            if (point.X >= 0 && point.Y >= 0 && point.X <= button.ActualWidth && point.Y <= button.ActualHeight) click();
        };
        button.LostMouseCapture += (_, _) => pressed = false;
        return button;
    }

    /// <summary>Bouton à texte (pastille 5 / 10 / 25…) en chiffres.</summary>
    public static SquircleBorder TextButton(string label, string tooltip, Action click, double height = EverydayTileShape.PresetHeight)
    {
        var text = Numerals.Apply(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TypeRamp.Caption);
        text.FontWeight = FontWeights.Medium;
        text.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
        var button = Button(text, tooltip, click, height, EverydayTileShape.PresetMinWidth);
        button.Padding = new Thickness(Space.S1, 0, Space.S1, 0); // des pastilles serrées : 4 tiennent à côté d'un anneau
        return button;
    }

    /// <summary>Bouton à icône (« + », « − »…).</summary>
    public static SquircleBorder GlyphButton(string glyph, string tooltip, Action click, double size = EverydayTileShape.MiniButton)
    {
        var button = Button(Glyph(glyph, IconSize.M, InkRole.TextPrimary), tooltip, click, size, size);
        button.Padding = new Thickness(0);
        button.Width = size;
        return button;
    }

    private static readonly DependencyProperty SelectedProperty =
        DependencyProperty.RegisterAttached("Selected", typeof(bool), typeof(TilePieces), new PropertyMetadata(false));

    /// <summary>Bouton « choisi » (durée du Café en cours, préréglage du minuteur qui tourne).</summary>
    public static void SetSelected(SquircleBorder button, bool selected)
    {
        if (button.GetValue(SelectedProperty) is bool current && current == selected && button.Background != null) return;
        button.SetValue(SelectedProperty, selected);
        PaintButton(button, hover: button.IsMouseOver);
    }

    private static void PaintButton(SquircleBorder button, bool hover)
    {
        bool selected = (bool)button.GetValue(SelectedProperty);
        var role = selected ? InkRole.SelectedStrong : hover ? InkRole.SurfaceHover : InkRole.Control;
        button.SetResourceReference(Border.BackgroundProperty, Paint.Key(role));
    }

    /// <summary>Barre de progression fine (espace disque) : piste + remplissage.</summary>
    public static Grid Bar(out Border fill)
    {
        var bar = new Grid { Height = EverydayTileShape.DiskBar, ClipToBounds = true };
        var track = new Border { CornerRadius = new CornerRadius(EverydayTileShape.DiskBar / 2) };
        track.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Track));
        fill = new Border { CornerRadius = new CornerRadius(EverydayTileShape.DiskBar / 2), HorizontalAlignment = HorizontalAlignment.Left };
        bar.Children.Add(track);
        bar.Children.Add(fill);
        return bar;
    }
}

/// <summary>
/// Anneau de progression d'une tuile (même dessin que l'anneau de la pilule, <see cref="LivePill.Ring"/>) : piste discrète,
/// arc coloré, contenu au centre. L'arc n'est redessiné que s'il a bougé de plus d'1/240 de tour.
/// </summary>
public sealed class TileRing : Grid
{
    private readonly Ellipse _track = new();
    private readonly Path _arc = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly double _size, _stroke;
    private double _drawn = -1;

    public TileRing(double size, double stroke)
    {
        _size = size;
        _stroke = stroke;
        Width = Height = size;
        _track.StrokeThickness = stroke;
        _track.Margin = new Thickness(stroke / 2);
        _track.SetResourceReference(Shape.StrokeProperty, Paint.Key(InkRole.Track));
        _arc.StrokeThickness = stroke;
        Children.Add(_track);
        Children.Add(_arc);
    }

    /// <summary>Contenu posé au centre (temps, icône marche / pause).</summary>
    public UIElement Center
    {
        set => Children.Add(value);
    }

    /// <summary>Avancement 0–1 (0 = piste seule) et couleur de l'arc.</summary>
    public void Show(double progress, Brush brush, double opacity = 1)
    {
        progress = Math.Clamp(progress, 0, 1);
        _arc.Stroke = brush;
        _arc.Opacity = opacity;
        if (Math.Abs(progress - _drawn) < 1 / 240.0) return;
        _drawn = progress;
        _arc.Data = progress <= 0 ? null : LivePill.Ring(progress, _size, _stroke);
    }
}
