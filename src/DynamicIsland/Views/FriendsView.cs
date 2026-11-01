using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services.Friends;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Amis » de l'île (6D-4) : jusqu'à 6 amis, ceux en jeu d'abord, puis en ligne, puis hors ligne (estompés).
/// Visible seulement si « Amis en ligne » est activé et qu'il y a au moins un ami. La liste est relue plus souvent tant que
/// l'onglet est ouvert. Clic droit sur un ami : statut, message rapide, annonces coupées, retirer, bloquer (correctif 24).
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
    /// <summary>Retour d'une action du menu (« Message envoyé à Max »), à la place de la ligne du bas pendant quelques secondes.</summary>
    private readonly TextBlock _feedback = new()
    {
        FontSize = TypeRamp.Footnote,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, Space.S8, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly DispatcherTimer _feedbackTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _active;

    public FriendsView()
    {
        Width = IslandShape.ContentWidth;
        var panel = new StackPanel();
        panel.Children.Add(_grid);
        panel.Children.Add(_empty);
        panel.Children.Add(_feedback);
        Content = panel;
        FriendsService.Changed += () => Dispatcher.BeginInvoke(Refresh);
        _feedbackTimer.Tick += (_, _) =>
        {
            _feedbackTimer.Stop();
            _feedback.Visibility = Visibility.Collapsed;
            Refresh();
        };
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
        _empty.Visibility = _empty.Text.Length > 0 && _feedback.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ clic droit sur un ami (correctif 24)

    /// <summary>
    /// Profil et statut, message rapide (liste fermée : l'île ne prend pas le clavier), annonces coupées, paramètres, retirer et
    /// bloquer (avec confirmation).
    /// </summary>
    private void ShowMenu(Friend friend)
    {
        if (_sample != null) return; // rendu d'exemple : rien à faire
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = $"{friend.Display}  ·  {FriendsApi.FormatCode(friend.Code)}", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new MenuItem { Header = FriendsService.StatusText(friend, since: true), IsEnabled = false });
        menu.Items.Add(new Separator());
        if (FriendsService.MessagesAvailable)
        {
            var send = new MenuItem { Header = Loc.T("friends.menu.message") };
            foreach (var id in QuickMessages.Ids) send.Items.Add(Entry(QuickMessages.Text(id), () => _ = SendAsync(friend, id)));
            menu.Items.Add(send);
        }
        bool muted = FriendsService.IsMuted(friend.Code);
        menu.Items.Add(Entry(Loc.T(muted ? "friends.menu.unmute" : "friends.menu.mute"), () => FriendsService.SetMuted(friend.Code, !muted)));
        menu.Items.Add(Entry(Loc.T("friends.menu.settings"), () => Settings.SettingsWindow.Open(Settings.SettingsPage.Friends)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("friends.menu.remove"), () => _ = RemoveAsync(friend)));
        menu.Items.Add(Entry(Loc.T("friends.menu.block"), () => _ = BlockAsync(friend)));
        menu.IsOpen = true;
    }

    private static MenuItem Entry(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private async Task SendAsync(Friend friend, string message)
    {
        var problem = await FriendsService.SendMessageAsync(friend.Code, message);
        ShowFeedback(problem ?? Loc.F("friends.message.sent", QuickMessages.Text(message), friend.Display), problem != null);
    }

    private async Task RemoveAsync(Friend friend)
    {
        if (MessageBox.Show(Loc.F("settings.friends.remove.confirm", friend.Display), AppInfo.Name, MessageBoxButton.OKCancel,
                MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await FriendsService.RemoveAsync(friend.Code);
    }

    private async Task BlockAsync(Friend friend)
    {
        if (MessageBox.Show(Loc.F("friends.block.confirm", friend.Display), AppInfo.Name, MessageBoxButton.OKCancel,
                MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var problem = await FriendsService.BlockAsync(friend);
        ShowFeedback(problem ?? Loc.F("friends.blocked.done", friend.Display), problem != null);
    }

    private void ShowFeedback(string text, bool error)
    {
        _feedback.Text = text;
        _feedback.Foreground = error ? Paint.Timer : Paint.TextSecondary;
        _feedback.Visibility = Visibility.Visible;
        _empty.Visibility = Visibility.Collapsed;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private SquircleBorder Card(Friend friend)
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
        // Nom (+ cloche barrée si ses annonces sont coupées)
        var name = new DockPanel();
        if (_sample == null && FriendsService.IsMuted(friend.Code))
        {
            var bell = new TextBlock
            {
                Text = Icons.BellOff,
                FontFamily = FontStack.Icons,
                FontSize = IconSize.S,
                Foreground = Paint.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Space.S2, 0, 0, 0),
                ToolTip = Loc.T("friends.menu.muted"),
            };
            DockPanel.SetDock(bell, System.Windows.Controls.Dock.Right);
            name.Children.Add(bell);
        }
        name.Children.Add(new TextBlock { Text = friend.Display, FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(name);
        text.Children.Add(new TextBlock
        {
            Text = FriendsService.StatusText(friend),
            FontSize = TypeRamp.Caption,
            Foreground = Paint.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var card = Design.OpalTile.Apply(new SquircleBorder // verre opalin (O1)
        {
            Height = 52,
            Margin = new Thickness(Space.S1),
            CornerRadius = Corners.L,
            Opacity = friend.Online ? 1 : 0.5,
            Child = row,
        });
        card.MouseRightButtonUp += (_, e) =>
        {
            ShowMenu(friend);
            e.Handled = true;
        };
        return card;
    }
}
