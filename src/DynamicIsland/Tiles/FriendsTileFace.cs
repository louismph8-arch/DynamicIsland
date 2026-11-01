using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Friends;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Tuile vivante « Amis » (refonte O6) : avatars ronds (initiale) des amis en ligne avec un anneau de statut — vert en ligne, accent
/// + mini manette en jeu — et le nombre en ligne. Clic = outil Amis.
/// <list type="bullet">
/// <item>S : premier ami en ligne + nombre ;</item>
/// <item>W : icône, nom, nombre, puis jusqu'à 4 avatars (« +N » au-delà) ;</item>
/// <item>L : liste courte (4 lignes) : avatar, nom, « Joue à Valorant » / « En ligne » (hors ligne estompés pour finir la liste).</item>
/// </list>
/// <para>Données : la liste en mémoire de <see cref="FriendsService"/> (aucune requête à soi) ; tant que la tuile est affichée, le
/// service la relit plus souvent (<see cref="FriendsService.Watch"/>, comme l'onglet Amis). Reconstruite seulement si elle change.</para>
/// </summary>
public sealed class FriendsTileFace : TileFace
{
    private readonly Grid _layout = new();
    private readonly Panel _body;
    private readonly TextBlock _count;
    /// <summary>Tuile S : « en ligne », ou « Personne en ligne » (le nombre 0 est alors caché).</summary>
    private readonly TextBlock? _line;
    private readonly IconChip? _chip;
    private string _signature = "\0";
    private bool _watching;

    public FriendsTileFace(TileDefinition definition, TileSize size) : base(definition, size, null)
    {
        Child = _layout;
        _count = Numerals.Apply(new TextBlock { VerticalAlignment = VerticalAlignment.Center }, TypeRamp.NumberTile).WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);
        if (size == TileSize.S)
        {
            // Avatar + nombre en haut, nom et « en ligne » en bas.
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body = new Grid { Height = LiveTileShape.AvatarS, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            _layout.Children.Add(_body);
            _count.HorizontalAlignment = HorizontalAlignment.Right;
            _count.VerticalAlignment = VerticalAlignment.Top;
            _layout.Children.Add(_count);
            var title = Title();
            Grid.SetRow(title, 2);
            _layout.Children.Add(title);
            _line = new TextBlock { Text = Loc.T("tiles.friends.online"), FontSize = TypeRamp.Caption2, Margin = new Thickness(0, Space.Px, 0, 0),
                TextWrapping = TextWrapping.Wrap }.WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
            Grid.SetRow(_line, 3);
            _layout.Children.Add(_line);
        }
        else
        {
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _chip = new IconChip
            {
                Glyph = definition.Glyph, GlyphSize = IconSize.L, Width = Sizes.IconChip, Height = Sizes.IconChip,
                Tint = Interaction.ActiveTintLight, VerticalAlignment = VerticalAlignment.Center,
            };
            header.Children.Add(_chip);
            var title = Title();
            title.Margin = new Thickness(Space.S3, 0, Space.S2, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            // « 2 en ligne » : le nombre en chiffres fins, le mot plus petit.
            var count = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            count.Children.Add(_count);
            count.Children.Add(new TextBlock
            {
                Text = Loc.T("tiles.friends.online"), FontSize = TypeRamp.Caption2, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Space.S2, Space.S1, 0, 0),
            }.WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary));
            Grid.SetColumn(count, 2);
            header.Children.Add(count);
            _layout.Children.Add(header);
            _body = size == TileSize.W
                ? new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Bottom }
                : new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Space.S5, 0, 0) };
            Grid.SetRow(_body, 1);
            _layout.Children.Add(_body);
        }
    }

    private TextBlock Title() => new TextBlock
    {
        Text = Definition.Name,
        FontSize = TypeRamp.Caption,
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
    }.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);

    // ------------------------------------------------------------------ vie de la tuile

    /// <summary>Le service relit la liste plus souvent (--self-test : jamais pour une tuile cachée).</summary>
    internal bool IsWatching => _watching;

    protected override void OnLiveChanged(bool live)
    {
        bool watch = live && TileSamples.Friends == null;
        if (watch != _watching)
        {
            _watching = watch;
            FriendsService.Watch(watch);
        }
        if (live) Refresh();
    }

    /// <summary>Appelé chaque seconde par la grille (page visible) : ne refait les avatars que si la liste a changé.</summary>
    public override void Refresh()
    {
        var friends = TileSamples.Friends ?? FriendsService.Lists.Friends;
        string signature = string.Join("|", friends.Select(f => $"{f.Code}:{f.Name}:{f.Status}:{f.Game}"));
        if (signature == _signature) return;
        _signature = signature;
        try
        {
            Show(friends);
        }
        catch (Exception ex)
        {
            Log.Error("Tuile Amis : affichage", ex);
        }
    }

    /// <summary>En jeu d'abord, puis en ligne, puis hors ligne ; par nom.</summary>
    internal static List<Friend> Sorted(IEnumerable<Friend> friends) =>
        friends.OrderBy(f => f.Status switch { "game" => 0, "online" => 1, _ => 2 }).ThenBy(f => f.Display, StringComparer.CurrentCultureIgnoreCase).ToList();

    private void Show(IReadOnlyList<Friend> friends)
    {
        var sorted = Sorted(friends);
        var online = sorted.Where(f => f.Online).ToList();
        _count.Text = online.Count.ToString(Loc.Culture);
        if (_line != null)
        {
            _count.Visibility = online.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _line.Text = Loc.T(online.Count > 0 ? "tiles.friends.online" : "tiles.friends.nobody");
        }
        if (_chip != null) _chip.IsActive = online.Count > 0;
        _body.Children.Clear();
        System.Windows.Automation.AutomationProperties.SetName(this, $"{Definition.Name} : {Loc.F("tiles.friends.count", online.Count)}");
        switch (TileSize)
        {
            case TileSize.S:
                _body.Children.Add(online.Count > 0
                    ? Avatar(online[0], LiveTileShape.AvatarS, TypeRamp.Body)
                    : new IconChip { Glyph = Definition.Glyph, GlyphSize = IconSize.L, Width = Sizes.IconChip, Height = Sizes.IconChip, VerticalAlignment = VerticalAlignment.Center });
                break;
            case TileSize.W:
                if (online.Count == 0)
                {
                    _body.Children.Add(Nobody());
                    break;
                }
                // 4 avatars, ou 3 + « +N » ; s'il reste de la place (1 ou 2 avatars), le statut du premier à côté.
                int count = online.Count > LiveTileShape.AvatarsW ? LiveTileShape.AvatarsW - 1 : online.Count;
                var shown = online.Take(count).ToList();
                for (int i = 0; i < shown.Count; i++)
                {
                    var avatar = Avatar(shown[i], LiveTileShape.AvatarW, TypeRamp.Body);
                    if (i > 0) avatar.Margin = new Thickness(LiveTileShape.AvatarGap, 0, 0, 0);
                    DockPanel.SetDock(avatar, System.Windows.Controls.Dock.Left);
                    avatar.HorizontalAlignment = HorizontalAlignment.Left;
                    _body.Children.Add(avatar);
                }
                if (online.Count > shown.Count)
                {
                    var plus = More(online.Count - shown.Count);
                    DockPanel.SetDock(plus, System.Windows.Controls.Dock.Left);
                    plus.HorizontalAlignment = HorizontalAlignment.Left;
                    _body.Children.Add(plus);
                }
                else if (shown.Count <= 2)
                {
                    var first = shown[0];
                    string status = first.Status == "game" && first.Game is { Length: > 0 } game ? game : FriendsService.StatusText(first);
                    _body.Children.Add(Status($"{first.Display} · {status}", Space.S5));
                }
                break;
            default:
                if (online.Count == 0) _body.Children.Add(Nobody());
                var rows = online.Count > 0 ? sorted.Take(LiveTileShape.RowsL).ToList() : [];
                bool more = online.Count > LiveTileShape.RowsL;
                if (more) rows = rows.Take(LiveTileShape.RowsL - 1).ToList();
                foreach (var friend in rows) _body.Children.Add(Row(friend, _body.Children.Count > 0));
                if (more)
                {
                    var rest = new TextBlock { Text = Loc.F("tiles.friends.more", online.Count - rows.Count), FontSize = TypeRamp.Caption2, Margin = new Thickness(0, Space.S3, 0, 0) }
                        .WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);
                    _body.Children.Add(rest);
                }
                break;
        }
    }

    private static TextBlock Nobody() => new TextBlock
    {
        Text = Loc.T("tiles.friends.nobody"),
        FontSize = TypeRamp.Caption2,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    }.WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);

    /// <summary>« +3 » : une pastille de la taille d'un avatar.</summary>
    private static Grid More(int count)
    {
        var cell = new Grid { Width = LiveTileShape.AvatarW, Height = LiveTileShape.AvatarW, Margin = new Thickness(LiveTileShape.AvatarGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(new Ellipse().WithInk(Shape.FillProperty, InkRole.SelectedStrong));
        cell.Children.Add(Numerals.Apply(new TextBlock
        {
            Text = Loc.F("tiles.friends.plus", count), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }, TypeRamp.Caption).WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary));
        return cell;
    }

    private static TextBlock Status(string text, double left) => new TextBlock
    {
        Text = text,
        FontSize = TypeRamp.Caption2,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(left, 0, 0, 0),
    }.WithInk(TextBlock.ForegroundProperty, InkRole.TextSecondary);

    /// <summary>Ligne de la tuile L : avatar, nom, statut (à droite, coupé si trop long).</summary>
    private static Grid Row(Friend friend, bool spaced)
    {
        var row = new Grid { Margin = new Thickness(0, spaced ? Space.S3 : 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Avatar(friend, LiveTileShape.AvatarL, TypeRamp.Caption2));
        var name = new TextBlock
        {
            Text = friend.Display, FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = TileGridShape.Cell - Space.S8, Margin = new Thickness(Space.S4, 0, 0, 0),
        }.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var status = Status(FriendsService.StatusText(friend), Space.S3); // « Joue à Valorant », « En ligne »…
        status.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(status, 2);
        row.Children.Add(status);
        if (!friend.Online) row.Opacity = Interaction.DimmedOpacity;
        return row;
    }

    /// <summary>
    /// Avatar rond : initiale sur une pastille, anneau de statut (vert en ligne, accent en jeu + mini manette). Hors ligne : sans
    /// anneau, estompé.
    /// </summary>
    internal static Grid Avatar(Friend friend, double size, double letterSize)
    {
        var avatar = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        bool game = friend.Status == "game";
        if (friend.Online)
        {
            var ring = new Ellipse { StrokeThickness = LiveTileShape.AvatarRing };
            if (game) ring.SetResourceReference(Shape.StrokeProperty, AppearanceService.AccentGradientKey);
            else ring.Stroke = LiveTileShape.Online;
            avatar.Children.Add(ring);
        }
        double inset = LiveTileShape.AvatarRing + LiveTileShape.AvatarInset;
        avatar.Children.Add(new Ellipse { Margin = new Thickness(inset) }.WithInk(Shape.FillProperty, InkRole.Raised));
        avatar.Children.Add(new TextBlock
        {
            Text = friend.Display[..1].ToUpper(Loc.Culture),
            FontSize = letterSize,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.WithInk(TextBlock.ForegroundProperty, InkRole.TextPrimary));
        if (game && size >= LiveTileShape.AvatarS)
        {
            // Mini manette en bas à droite, sur une pastille d'accent bordée de la couleur de l'île.
            var badge = new Grid
            {
                Width = LiveTileShape.AvatarBadge, Height = LiveTileShape.AvatarBadge, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -Space.S1, -Space.S1),
            };
            var dot = new Ellipse { StrokeThickness = Stroke.Selected };
            dot.SetResourceReference(Shape.FillProperty, AppearanceService.AccentBrushKey);
            dot.SetResourceReference(Shape.StrokeProperty, AppearanceService.IslandBrushKey);
            badge.Children.Add(dot);
            badge.Children.Add(new TextBlock
            {
                Text = Icons.Gamepad, FontFamily = FontStack.Icons, FontSize = IconSize.S, Foreground = Paint.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            avatar.Children.Add(badge);
        }
        if (!friend.Online) avatar.Opacity = Interaction.DimmedOpacity;
        System.Windows.Automation.AutomationProperties.SetName(avatar, $"{friend.Display} · {FriendsService.StatusText(friend)}");
        return avatar;
    }
}
