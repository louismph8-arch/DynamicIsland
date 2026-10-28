using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services.Friends;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Amis » de l'île (6D-4) : jusqu'à 6 amis, ceux en jeu d'abord, puis en ligne, puis hors ligne (estompés).
/// Visible seulement si « Amis en ligne » est activé et qu'il y a au moins un ami. La liste est relue plus souvent tant que
/// l'onglet est ouvert.
/// </summary>
public sealed class FriendsView : UserControl
{
    private const int MaxShown = 6;
    private readonly UniformGrid _grid = new() { Columns = 2 };
    private readonly TextBlock _empty = new()
    {
        FontSize = TypeRamp.Footnote,
        Foreground = Paint.TextSecondary,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, Space.S8, 0, 0),
    };
    private bool _active;

    public FriendsView()
    {
        Width = IslandShape.ContentWidth;
        var panel = new StackPanel();
        panel.Children.Add(_grid);
        panel.Children.Add(_empty);
        Content = panel;
        FriendsService.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            FriendsService.Watch(value);
            if (value) Refresh();
        }
    }

    private IReadOnlyList<Friend>? _sample;

    public void Refresh() => Show(_sample ?? FriendsService.Lists.Friends);

    /// <summary>Rendu --snapshot : des amis d'exemple, gardés même quand l'onglet s'ouvre.</summary>
    internal void ShowSample(IReadOnlyList<Friend> friends)
    {
        _sample = friends;
        Show(friends);
    }

    private void Show(IReadOnlyList<Friend> friends)
    {
        _grid.Children.Clear();
        var sorted = friends.OrderBy(f => f.Status switch { "game" => 0, "online" => 1, _ => 2 }).ThenBy(f => f.Display).ToList();
        foreach (var friend in sorted.Take(MaxShown)) _grid.Children.Add(Card(friend));
        _empty.Text = friends.Count == 0 ? Loc.T("friends.island.none")
            : sorted.Count > MaxShown ? Loc.F("friends.island.more", sorted.Count - MaxShown)
            : sorted.All(f => !f.Online) ? Loc.T("friends.island.all_offline") : "";
        _empty.Visibility = _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static SquircleBorder Card(Friend friend)
    {
        var row = new Grid { Margin = new Thickness(Space.S3, 0, Space.S3, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        // Pastille avec l'initiale ; point vert (en ligne) ou couleur d'accent (en jeu) en bas à droite.
        var initial = new Grid { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center };
        initial.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(17),
            Background = Paint.Raised,
            Child = new TextBlock
            {
                Text = friend.Display[..1].ToUpperInvariant(),
                FontSize = TypeRamp.Body,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        if (friend.Online)
        {
            var dot = new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(5.5),
                BorderThickness = new Thickness(2),
                BorderBrush = Paint.Window,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            if (friend.Status == "game") dot.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
            else dot.Background = new SolidColorBrush(Swatch.Green);
            initial.Children.Add(dot);
        }
        row.Children.Add(initial);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = friend.Display, FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = friend.Status switch
            {
                "game" => friend.Game is { Length: > 0 } game ? Loc.F("friends.status.game", game) : Loc.T("friends.status.game_hidden"),
                "online" => Loc.T("friends.status.online"),
                _ => Loc.T("friends.status.offline"),
            },
            FontSize = TypeRamp.Caption,
            Foreground = Paint.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        return new SquircleBorder
        {
            Height = 52,
            Margin = new Thickness(Space.S1),
            CornerRadius = Corners.L,
            Background = Paint.Control,
            Opacity = friend.Online ? 1 : 0.5,
            Child = row,
        };
    }
}
