using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>Couleur de l'icône d'une tuile-outil : au repos, allumée (accent), ou en alerte (rouge : micro coupé, enregistrement).</summary>
public enum ToolTone
{
    Idle,
    Accent,
    Alert,
}

/// <summary>
/// Vue commune des tuiles-outils (O6, lot B) : même verre que <see cref="TileFace"/> (fond laiteux, bordure irisée, inclinaison,
/// appui), avec en plus une icône qui peut passer au rouge et un « accessoire » (miniature, pastilles, QR code, gros bouton)
/// placé selon la taille :
/// <list type="bullet">
/// <item>S : icône (accessoire en petit à droite), (chiffre), nom, info ;</item>
/// <item>W : à gauche icône, nom, info ; à droite l'accessoire, sinon le chiffre ;</item>
/// <item>L : icône et chiffre en haut, l'accessoire (sinon le chiffre en grand) au centre, nom, info.</item>
/// </list>
/// <see cref="Refresh"/> (page visible, île ouverte, au plus 1 fois par seconde) appelle <see cref="Update"/> : une erreur ne
/// coupe que cette tuile.
/// </summary>
public abstract class ToolFace : TileFace
{
    private readonly Grid _root = new();
    private readonly Grid _chip = new() { Width = Sizes.IconChip, Height = Sizes.IconChip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SquircleBorder _halo = new() { CornerRadius = Corners.IconChip, Opacity = 0, IsHitTestVisible = false };
    private readonly SquircleBorder _backdrop = new() { CornerRadius = Corners.IconChip, IsHitTestVisible = false };
    private bool _onImage;
    private readonly TextBlock _glyph = new() { FontFamily = FontStack.Icons, FontSize = IconSize.L, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new();
    private readonly TextBlock _line = new();
    private readonly TextBlock _value = new();
    private readonly Border _accessoryHost = new();
    private bool _failed;
    private ToolTone _tone = (ToolTone)(-1);
    private Color _surface;

    protected TileContext Context { get; }

    protected ToolFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, null)
    {
        Context = context;
        Child = _root; // remplace la disposition de la tuile simple
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            _root.RowDefinitions.Add(new RowDefinition { Height = height });

        _chip.Children.Add(_backdrop);
        _chip.Children.Add(_halo);
        _chip.Children.Add(_glyph);
        _root.Children.Add(_chip);

        _title.FontSize = TypeRamp.Caption;
        _title.FontWeight = FontWeights.SemiBold;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Text = definition.Name;
        _line.FontSize = TypeRamp.Caption2;
        _line.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
        _line.TextTrimming = TextTrimming.CharacterEllipsis;
        _line.Margin = new Thickness(0, Space.Px, 0, 0);
        _line.TextWrapping = TextWrapping.Wrap;
        _line.MaxHeight = 2 * TypeRamp.Caption2 * TileGridShape.LineHeight; // 2 lignes au plus
        Numerals.Apply(_value, size == TileSize.S ? TypeRamp.NumberTile : TypeRamp.NumberClock);
        _value.TextTrimming = TextTrimming.CharacterEllipsis;
        _value.Visibility = Visibility.Collapsed;
        _accessoryHost.Visibility = Visibility.Collapsed;

        switch (size)
        {
            case TileSize.S:
                Grid.SetRow(_value, 1);
                Grid.SetColumnSpan(_value, 2);
                _value.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetColumn(_accessoryHost, 1);
                _accessoryHost.VerticalAlignment = VerticalAlignment.Top;
                _title.TextWrapping = TextWrapping.Wrap;
                _title.MaxHeight = 2 * TypeRamp.Caption * TileGridShape.LineHeight;
                break;
            case TileSize.W:
                Grid.SetColumn(_value, 1);
                _value.HorizontalAlignment = HorizontalAlignment.Right;
                _value.VerticalAlignment = VerticalAlignment.Top;
                _value.Margin = new Thickness(Space.S3, -Space.S1, 0, 0);
                Grid.SetColumn(_accessoryHost, 1);
                Grid.SetRowSpan(_accessoryHost, 4);
                _accessoryHost.VerticalAlignment = VerticalAlignment.Center;
                _accessoryHost.Margin = new Thickness(Space.S4, 0, 0, 0);
                break;
            default:
                Grid.SetColumn(_value, 1);
                _value.HorizontalAlignment = HorizontalAlignment.Right;
                _value.VerticalAlignment = VerticalAlignment.Top;
                _value.Margin = new Thickness(Space.S3, -Space.S1, 0, 0);
                Grid.SetRow(_accessoryHost, 1);
                Grid.SetColumnSpan(_accessoryHost, 2);
                _accessoryHost.HorizontalAlignment = HorizontalAlignment.Center;
                _accessoryHost.VerticalAlignment = VerticalAlignment.Center;
                _accessoryHost.Margin = new Thickness(0, Space.S2, 0, Space.S3);
                break;
        }
        // W : nom et info restent dans la colonne de gauche (l'accessoire est à droite) ; S et L : toute la largeur.
        Grid.SetRow(_title, 2);
        Grid.SetRow(_line, 3);
        if (size != TileSize.W)
        {
            Grid.SetColumnSpan(_title, 2);
            Grid.SetColumnSpan(_line, 2);
        }
        _root.Children.Add(_value);
        _root.Children.Add(_accessoryHost);
        _root.Children.Add(_title);
        _root.Children.Add(_line);
        SetTone(Definition.Glyph, ToolTone.Idle);
    }

    /// <summary>Grille de la tuile (pour poser un fond, ex. la miniature du fond d'écran, sous tout le reste).</summary>
    protected Grid Root => _root;

    /// <summary>Le nom de la tuile (texte blanc sur une image, par exemple).</summary>
    protected TextBlock TitleText => _title;

    /// <summary>La ligne d'info.</summary>
    protected TextBlock LineText => _line;

    /// <summary>
    /// Tuile posée sur une image (miniature du fond d'écran) : textes et icône blancs (l'image porte un voile sombre dans les deux
    /// thèmes), pastille sombre sous l'icône ; faux = couleurs du thème.
    /// </summary>
    protected void SetOnImage(bool on)
    {
        if (on == _onImage) return;
        _onImage = on;
        _tone = (ToolTone)(-1); // l'icône repeint à la prochaine mise à jour
        if (on)
        {
            _title.Foreground = Paint.Frozen(Swatch.White);
            _line.Foreground = Paint.Frozen(ToolTileShape.OnImageText);
            _backdrop.Background = Paint.Frozen(ToolTileShape.ThumbChip);
        }
        else
        {
            _title.ClearValue(TextBlock.ForegroundProperty);
            _line.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
            _backdrop.Background = null;
        }
    }

    /// <summary>Nombre de relectures (auto-test : une tuile d'une page cachée n'est jamais relue).</summary>
    internal int Updates { get; private set; }

    public sealed override void Refresh()
    {
        if (_failed) return;
        Updates++;
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            _failed = true; // plus relue de la session : la tuile garde son dernier état
            Log.Error($"Tuile « {Definition.Id} » : lecture", ex);
        }
    }

    /// <summary>Relit l'état (en mémoire seulement : rien sur le disque ni le réseau à chaque seconde).</summary>
    protected abstract void Update();

    /// <summary>Icône et sa couleur (rouge = lisible sur le fond actuel de l'île, sombre ou opale blanche).</summary>
    protected void SetTone(string glyph, ToolTone tone)
    {
        if (_glyph.Text != glyph) _glyph.Text = glyph;
        var surface = AppearanceService.SurfaceBase;
        if (tone == _tone && (tone != ToolTone.Alert || surface == _surface)) return;
        _tone = tone;
        _surface = surface;
        switch (tone)
        {
            case ToolTone.Accent:
                _glyph.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentGradientKey);
                _halo.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentBrushKey);
                _halo.Opacity = Interaction.ActiveTintLight;
                break;
            case ToolTone.Alert:
                var red = Paint.Frozen(Design.OpalColor.Readable(Swatch.Red, surface, 3));
                _glyph.Foreground = red;
                _halo.Background = red;
                _halo.Opacity = Interaction.ActiveTintLight;
                break;
            default:
                if (_onImage) _glyph.Foreground = Paint.Frozen(Swatch.White);
                else _glyph.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
                _halo.Opacity = 0;
                break;
        }
    }

    /// <summary>Chiffre (dernier résultat, temps écoulé…) et ligne d'info ; null = caché.</summary>
    protected void SetTexts(string? value, string? line)
    {
        _title.Text = Definition.Name;
        bool hasValue = !string.IsNullOrEmpty(value);
        if (_value.Text != (value ?? "")) _value.Text = value ?? "";
        // W : le chiffre cède la place à l'accessoire ; L : sans accessoire, il passe au centre en grand.
        bool accessory = _accessoryHost.Child != null;
        _value.Visibility = hasValue && !(TileSize == TileSize.W && accessory) ? Visibility.Visible : Visibility.Collapsed;
        if (TileSize == TileSize.L)
        {
            bool hero = hasValue && !accessory;
            Grid.SetRow(_value, hero ? 1 : 0);
            Grid.SetColumn(_value, hero ? 0 : 1);
            Grid.SetColumnSpan(_value, hero ? 2 : 1);
            Numerals.Apply(_value, hero ? TypeRamp.Display : TypeRamp.NumberClock);
            _value.HorizontalAlignment = hero ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            _value.VerticalAlignment = hero ? VerticalAlignment.Center : VerticalAlignment.Top;
            _value.Margin = hero ? new Thickness(0) : new Thickness(Space.S3, -Space.S1, 0, 0);
        }
        if (_line.Text != (line ?? "")) _line.Text = line ?? "";
        _line.Visibility = string.IsNullOrEmpty(line) ? Visibility.Collapsed : Visibility.Visible;
        if (TileSize == TileSize.S)
            _line.MaxHeight = (hasValue ? 1 : 2) * TypeRamp.Caption2 * TileGridShape.LineHeight; // la case reste pleine sans déborder
    }

    /// <summary>Accessoire de la tuile (miniature, pastilles, QR code, gros bouton) ; null = aucun.</summary>
    protected void SetAccessory(FrameworkElement? element)
    {
        if (ReferenceEquals(_accessoryHost.Child, element)) return;
        _accessoryHost.Child = element;
        _accessoryHost.Visibility = element == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Gros bouton rond (micro, mémo) : glyphe au centre, plein en rouge / accent quand actif, verre sinon.</summary>
    protected static (SquircleBorder Button, TextBlock Glyph) RoundButton(double size)
    {
        var glyph = new TextBlock { FontFamily = FontStack.Icons, FontSize = size >= ToolTileShape.ButtonLarge ? IconSize.Huge : IconSize.XXL, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var button = new SquircleBorder { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = glyph, IsHitTestVisible = false };
        return (button, glyph);
    }

    /// <summary>Habille un gros bouton : rouge plein (alerte), accent plein, ou verre de tuile.</summary>
    protected void PaintButton(SquircleBorder button, TextBlock glyph, string icon, ToolTone tone)
    {
        glyph.Text = icon;
        switch (tone)
        {
            case ToolTone.Alert:
                button.Background = Paint.Frozen(Design.OpalColor.Readable(Swatch.Red, AppearanceService.SurfaceBase, 3));
                glyph.Foreground = Paint.Frozen(Swatch.White);
                break;
            case ToolTone.Accent:
                button.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
                glyph.Foreground = Paint.Frozen(Swatch.White);
                break;
            default:
                button.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Control));
                glyph.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
                break;
        }
    }
}
