using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Vue d'une tuile dans la grille (refonte O6) : verre opalin (<see cref="Design.OpalTile"/> : fond laiteux, bordure irisée, lueur
/// au survol, inclinaison vers la souris et appui qui s'enfonce), icône (pastille d'accent quand l'activité est en cours), nom
/// et une info en direct. Disposition selon la taille :
/// <list type="bullet">
/// <item>S : icône, (grand chiffre), nom, (ligne d'info) ;</item>
/// <item>W : icône + grand chiffre ou petite image à droite, nom, ligne d'info ;</item>
/// <item>L : icône + chiffre, au centre l'image (pochette) ou le grand chiffre ou l'icône en grand, nom, info sur 2 lignes.</item>
/// </list>
/// Clic (appui puis relâcher sur la tuile) = <see cref="Clicked"/> ; l'appui est « consommé » : il ne déplace pas l'île. Une
/// tuile vivante dérive de cette classe (<see cref="SetContent"/>), remplace <see cref="Refresh"/> et n'anime rien hors de
/// <see cref="IsLive"/> (<see cref="OnLiveChanged"/>).
/// </summary>
public class TileFace : SquircleBorder
{
    private readonly Func<TileInfo>? _read;
    private readonly IconChip _chip;
    private readonly TextBlock _title = new();
    private readonly TextBlock _line = new();
    private readonly TextBlock _value = new();
    private readonly TextBlock _hero = new();
    private readonly SquircleBorder _art = new();
    private readonly Grid _layout = new();
    private bool _pressed;
    private bool _failed;
    private TileInfo _shown;
    private bool _hasShown;

    public TileDefinition Definition { get; }
    public TileSize TileSize { get; }

    /// <summary>Clic sur la tuile (ouvrir son outil).</summary>
    public event Action<TileFace>? Clicked;

    /// <summary>
    /// Tuile à l'écran : île ouverte sur la grille ET tuile sur la page affichée (<see cref="TileGridView"/>). Une tuile vivante
    /// n'anime rien (ciel, trotteuse, visualiseur) tant que c'est faux.
    /// </summary>
    public bool IsLive { get; private set; }

    /// <summary>La molette sur cette tuile lui revient (Compteur) au lieu de changer de page.</summary>
    public virtual bool WantsWheel => false;

    /// <summary>Appelé par la grille quand la tuile devient visible / cachée. Une erreur n'arrête que cette tuile.</summary>
    internal void SetLive(bool live)
    {
        if (IsLive == live) return;
        IsLive = live;
        try
        {
            OnLiveChanged(live);
        }
        catch (Exception ex)
        {
            Log.Error($"Tuile « {Definition.Id} » : {(live ? "affichée" : "cachée")}", ex);
        }
    }

    /// <summary>Tuile vivante : démarrer / arrêter ses animations (rien ne doit tourner quand <paramref name="live"/> est faux).</summary>
    protected virtual void OnLiveChanged(bool live)
    {
    }

    /// <summary>Tuile vivante : remplace le contenu de la tuile simple (la tuile garde son verre, son clic et son mouvement).</summary>
    protected void SetContent(UIElement content, Thickness padding)
    {
        Padding = padding;
        Child = content;
    }
    /// <summary>
    /// Clic sur la tuile (ailleurs que sur un de ses boutons) : une tuile vivante peut le garder pour elle (Café : marche / arrêt,
    /// Compteur : +1) en rendant vrai ; sinon l'outil s'ouvre (<see cref="Clicked"/>).
    /// </summary>
    protected virtual bool OnTileClick() => false;
    /// <summary>Clic droit sur une tuile qui a une action de clic droit (<see cref="TileDefinition.RightClick"/>).</summary>
    public event Action<TileFace>? RightClicked;

    public TileFace(TileDefinition definition, TileSize size, Func<TileInfo>? read)
    {
        Definition = definition;
        TileSize = size;
        _read = read;
        CornerRadius = Corners.XXL;
        Padding = size == TileSize.S ? TileGridShape.PaddingSmall : TileGridShape.Padding;
        Cursor = Cursors.Hand;
        Design.OpalTile.Apply(this); // fond, bordure irisée, lueur, inclinaison, appui (O1 / O3)
        Child = _layout;

        _chip = new IconChip { Glyph = definition.Glyph, GlyphSize = IconSize.L, Width = Sizes.IconChip, Height = Sizes.IconChip,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Tint = Interaction.ActiveTintLight };
        _title.FontSize = TypeRamp.Caption;
        _title.FontWeight = FontWeights.SemiBold;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Text = definition.Name;
        _line.FontSize = TypeRamp.Caption2;
        _line.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
        _line.TextTrimming = TextTrimming.CharacterEllipsis;
        _line.Margin = new Thickness(0, Space.Px, 0, 0);
        Numerals.Apply(_value, size == TileSize.S ? TypeRamp.NumberTile : TypeRamp.NumberClock);
        _value.TextTrimming = TextTrimming.CharacterEllipsis;
        Numerals.Apply(_hero, TypeRamp.Display);
        _hero.HorizontalAlignment = HorizontalAlignment.Center;
        _hero.VerticalAlignment = VerticalAlignment.Center;
        _art.CornerRadius = Corners.L;
        _art.Visibility = Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(_art, BitmapScalingMode.HighQuality);

        // Lignes : en-tête (icône, chiffre ou petite image à droite), centre, nom, info.
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            _layout.RowDefinitions.Add(new RowDefinition { Height = height });
        _layout.Children.Add(_chip);
        switch (size)
        {
            case TileSize.S:
                Grid.SetRow(_value, 1);
                _value.VerticalAlignment = VerticalAlignment.Bottom;
                // Case étroite : le nom et l'info passent à la ligne plutôt que d'être coupés (« Presse-/papiers »).
                _title.TextWrapping = TextWrapping.Wrap;
                _title.MaxHeight = 2 * TypeRamp.Caption * TileGridShape.LineHeight;
                _line.TextWrapping = TextWrapping.Wrap;
                break;
            case TileSize.W:
                _value.HorizontalAlignment = HorizontalAlignment.Right;
                _value.VerticalAlignment = VerticalAlignment.Top;
                _value.Margin = new Thickness(Sizes.IconChip + Space.S3, -Space.S1, 0, 0);
                _value.TextAlignment = TextAlignment.Right;
                _art.Width = _art.Height = TileGridShape.ArtSmall + Space.S1;
                _art.CornerRadius = Corners.S;
                _art.HorizontalAlignment = HorizontalAlignment.Right;
                _art.VerticalAlignment = VerticalAlignment.Top;
                break;
            default:
                _value.HorizontalAlignment = HorizontalAlignment.Right;
                _value.VerticalAlignment = VerticalAlignment.Top;
                _value.Margin = new Thickness(Sizes.IconChip + Space.S3, -Space.S1, 0, 0);
                _art.Width = _art.Height = TileGridShape.Art;
                _art.HorizontalAlignment = HorizontalAlignment.Center;
                _art.VerticalAlignment = VerticalAlignment.Center;
                _art.Margin = new Thickness(0, Space.S2, 0, Space.S3);
                Grid.SetRow(_art, 1);
                Grid.SetRow(_hero, 1);
                _layout.Children.Add(_hero);
                _line.TextWrapping = TextWrapping.Wrap;
                _line.MaxHeight = TileGridShape.LineMaxHeight;
                break;
        }
        Grid.SetRow(_title, 2);
        Grid.SetRow(_line, 3);
        _layout.Children.Add(_value);
        _layout.Children.Add(_art);
        _layout.Children.Add(_title);
        _layout.Children.Add(_line);
        SetAutomationName();
    }

    private void SetAutomationName() => System.Windows.Automation.AutomationProperties.SetName(this, Definition.Name);

    /// <summary>--self-test (case S) : le nom passe sur 2 lignes, le chiffre est à droite de l'icône, l'info est affichée.</summary>
    internal (bool NameWraps, bool ValueBeside, bool LineShown) SmallLayout =>
        (Wraps(_title.Text, TypeRamp.Caption, FontWeights.SemiBold), Grid.GetRow(_value) == 0 && _value.Visibility == Visibility.Visible,
         _line.Visibility == Visibility.Visible);

    /// <summary>
    /// Clic à l'intérieur de la tuile (point relatif à la tuile) : vrai si la tuile l'a traité elle-même (pastille d'un profil…),
    /// alors <see cref="Clicked"/> n'est pas levé et aucun outil ne s'ouvre.
    /// </summary>
    protected virtual bool HandleClick(Point point) => false;

    /// <summary>Rendus figés (--snapshot) : une tuile vivante pose ses valeurs tout de suite (ressorts au repos, sans respiration).</summary>
    public virtual void SettleMotion() { }

    /// <summary>Relit l'info en direct (page visible, île ouverte). Une erreur de lecture n'arrête que cette tuile.</summary>
    public virtual void Refresh()
    {
        _title.Text = Definition.Name;
        if (_read == null || _failed) return;
        TileInfo info;
        try
        {
            info = _read();
        }
        catch (Exception ex)
        {
            _failed = true; // plus relue de la session : une tuile ne remplit pas le journal toutes les secondes
            Log.Error($"Tuile « {Definition.Id} » : lecture", ex);
            info = default;
        }
        Show(info);
    }

    /// <summary>Affiche une info (aussi utilisé par les rendus --snapshot).</summary>
    public void Show(TileInfo info)
    {
        if (_hasShown && info == _shown) return; // rien n'a changé : rien à redessiner
        _hasShown = true;
        _shown = info;
        _chip.IsActive = info.Active;
        bool hasValue = !string.IsNullOrEmpty(info.Value);
        bool hasLine = !string.IsNullOrEmpty(info.Line);
        _line.Text = info.Line ?? "";
        _value.Text = info.Value ?? "";
        _hero.Text = "";
        _art.Background = info.Art is { } art ? new ImageBrush(art) { Stretch = Stretch.UniformToFill } : null;
        switch (TileSize)
        {
            case TileSize.S:
                _art.Visibility = Visibility.Collapsed;
                _value.Visibility = hasValue ? Visibility.Visible : Visibility.Collapsed;
                bool wrapped = Wraps(_title.Text, TypeRamp.Caption, FontWeights.SemiBold);
                // Nom sur 2 lignes (« Presse-/papiers ») : plus de place pour le chiffre au-dessus du nom → il monte à droite de
                // l'icône (comme en W) et l'info garde sa ligne. Nom sur 1 ligne : le chiffre au-dessus du nom, sans l'info.
                bool valueBeside = hasValue && wrapped;
                Grid.SetRow(_value, valueBeside ? 0 : 1);
                _value.HorizontalAlignment = valueBeside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                _value.VerticalAlignment = valueBeside ? VerticalAlignment.Center : VerticalAlignment.Bottom;
                _value.Margin = valueBeside ? new Thickness(Sizes.IconChip + Space.S2, 0, 0, 0) : new Thickness(0);
                _line.Visibility = hasLine && (!hasValue || valueBeside) ? Visibility.Visible : Visibility.Collapsed;
                // Nom sur 2 lignes → l'info sur 1 ; sinon l'info peut en prendre 2 (la case reste pleine sans déborder).
                int lines = wrapped ? 1 : 2;
                _line.MaxHeight = lines * TypeRamp.Caption2 * TileGridShape.LineHeight;
                break;
            case TileSize.W:
                _art.Visibility = info.Art != null ? Visibility.Visible : Visibility.Collapsed;
                _value.Visibility = hasValue && info.Art == null ? Visibility.Visible : Visibility.Collapsed;
                _line.Visibility = hasLine ? Visibility.Visible : Visibility.Collapsed;
                break;
            default:
                _art.Visibility = info.Art != null ? Visibility.Visible : Visibility.Collapsed;
                // Pas d'image : le chiffre passe au centre, en grand ; ni l'un ni l'autre : l'icône en grand, discrète.
                bool heroValue = info.Art == null && hasValue;
                _value.Visibility = hasValue && !heroValue ? Visibility.Visible : Visibility.Collapsed;
                _hero.Visibility = info.Art == null ? Visibility.Visible : Visibility.Collapsed;
                if (heroValue)
                {
                    Numerals.Apply(_hero, TypeRamp.Display);
                    _hero.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
                    _hero.Text = info.Value;
                }
                else if (info.Art == null)
                {
                    _hero.FontFamily = FontStack.Icons;
                    _hero.FontSize = IconSize.Huge;
                    _hero.FontWeight = FontWeights.Normal;
                    _hero.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextFaint));
                    _hero.Text = Definition.Glyph;
                }
                _line.Visibility = hasLine ? Visibility.Visible : Visibility.Collapsed;
                break;
        }
    }

    /// <summary>Le texte dépasse la largeur utile d'une case S (il passera à la ligne).</summary>
    private bool Wraps(string text, double size, FontWeight weight)
    {
        // Largeur vraiment disponible : la bordure irisée de la tuile compte aussi (sinon « Presse-papiers » passait pour une ligne).
        double width = TileGridShape.Cell - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right;
        var typeface = new Typeface(FontStack.Ui, FontStyles.Normal, weight, FontStretches.Normal);
        var formatted = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0);
        return formatted.WidthIncludingTrailingWhitespace > width;
    }
    // ------------------------------------------------------------------ clic

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true; // l'appui sur une tuile ne déplace pas l'île
        _pressed = CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_pressed) return;
        _pressed = false;
        var point = e.GetPosition(this);
        bool inside = point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        ReleaseMouseCapture();
        e.Handled = true;
        if (!inside) return;
        bool handled;
        try
        {
            handled = HandleClick(point) || OnTileClick(point) || OnTileClick();
        }
        catch (Exception ex)
        {
            Log.Error($"Tuile « {Definition.Id} » : clic", ex);
            handled = true;
        }
        if (!handled) Clicked?.Invoke(this);
    }

    /// <summary>
    /// Clic à un endroit précis de la tuile (une pastille de couleur…) : vrai = traité ici, la tuile ne fait pas son action
    /// habituelle. Pour les tuiles vivantes.
    /// </summary>
    protected virtual bool OnTileClick(Point position) => false;

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Definition.RightClick == null) return; // sinon : le menu habituel de l'île
        e.Handled = true;
        RightClicked?.Invoke(this);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pressed = false;
    }
}
