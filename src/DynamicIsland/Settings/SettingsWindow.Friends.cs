using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services.Friends;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Page « Connecté » → amis en ligne (6D-4) : interrupteur, serveur (adresse + guide Cloudflare + code du serveur à copier),
/// mon code et l'invitation, pseudo, options, ajout d'un ami, amis / demandes reçues / demandes envoyées, quitter.
/// </summary>
public partial class SettingsWindow
{
    private const string CloudflareUrl = "https://dash.cloudflare.com/";
    private bool _watchingFriends;

    private void OnFriendsChanged() => Dispatcher.BeginInvoke(ShowFriends);

    private void WatchFriends(bool watching)
    {
        if (watching == _watchingFriends) return;
        _watchingFriends = watching;
        FriendsService.Watch(watching);
    }

    private void ShowFriends()
    {
        var friends = SettingsStore.Current.Friends;
        bool loading = _loading;
        _loading = true;
        FriendsToggle.IsChecked = friends.Enabled;
        FriendsInvisibleToggle.IsChecked = friends.Invisible;
        FriendsShareGameToggle.IsChecked = friends.ShareGame;
        FriendsNotifyToggle.IsChecked = friends.Notify;
        if (!FriendsServerBox.IsKeyboardFocused) FriendsServerBox.Text = friends.Server;
        if (!FriendsNameBox.IsKeyboardFocused) FriendsNameBox.Text = friends.Name;
        _loading = loading;
        FriendsToggle.IsEnabled = !SettingsStore.IsReadOnly;
        FriendsPanel.Visibility = friends.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!friends.Enabled) return;

        var status = FriendsService.Status;
        FriendsServerStatus.Text = Loc.T(status switch
        {
            FriendsService.State.Connected => "settings.friends.status.connected",
            FriendsService.State.Connecting => "settings.friends.status.connecting",
            FriendsService.State.ServerUnreachable => "settings.friends.status.unreachable",
            _ => "settings.friends.status.no_server",
        });
        bool connected = status == FriendsService.State.Connected;
        FriendsGuideText.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        FriendsMeCard.Visibility = FriendsListCard.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        FriendsCodeText.Text = Loc.F("settings.friends.code", FriendsService.MyCode ?? "—");
        ShowFriendList(FriendsService.Lists);
    }

    private void ShowFriendList(FriendLists lists)
    {
        FriendsList.Children.Clear();
        foreach (var request in lists.Incoming)
            FriendsList.Children.Add(FriendRow(request, Loc.T("settings.friends.incoming"), Swatch.Orange,
                (Loc.T("settings.friends.accept"), () => FriendsService.AddAsync(request.Code)),
                (Loc.T("settings.friends.refuse"), () => FriendsService.RemoveAsync(request.Code))));
        foreach (var friend in lists.Friends.OrderBy(f => f.Online ? 0 : 1).ThenBy(f => f.Display))
        {
            var status = friend.Status switch
            {
                "game" => friend.Game is { Length: > 0 } game ? Loc.F("friends.status.game", game) : Loc.T("friends.status.game_hidden"),
                "online" => Loc.T("friends.status.online"),
                _ => Loc.T("friends.status.offline"),
            };
            FriendsList.Children.Add(FriendRow(friend, status, friend.Online ? Swatch.Green : (Color?)null,
                (Loc.T("settings.friends.remove"), () => ConfirmRemoveAsync(friend))));
        }
        foreach (var pending in lists.Outgoing)
            FriendsList.Children.Add(FriendRow(pending, Loc.T("settings.friends.outgoing"), null,
                (Loc.T("settings.friends.cancel"), () => FriendsService.RemoveAsync(pending.Code))));
        if (FriendsList.Children.Count == 0)
            FriendsList.Children.Add(new TextBlock { Text = Loc.T("settings.friends.empty"), Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
    }

    private Grid FriendRow(Friend friend, string status, Color? dot, params (string Label, Func<Task> Action)[] buttons)
    {
        var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = dot is { } color ? new SolidColorBrush(color) : Paint.Track,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Space.S4, 0),
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = friend.Name.Length > 0 ? $"{friend.Name}  ·  {FriendsApi.FormatCode(friend.Code)}" : FriendsApi.FormatCode(friend.Code),
            FontSize = TypeRamp.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock { Text = status, Style = (Style)FindResource("CardDescription") });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        foreach (var (label, action) in buttons)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button { Style = (Style)FindResource("SettingsButton"), Content = label, Margin = new Thickness(Space.S3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                await action();
                button.IsEnabled = true;
            };
            Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
            row.Children.Add(button);
        }
        return row;
    }

    private async Task ConfirmRemoveAsync(Friend friend)
    {
        if (MessageBox.Show(this, Loc.F("settings.friends.remove.confirm", friend.Display), AppInfo.Name, MessageBoxButton.OKCancel,
                MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK)
            await FriendsService.RemoveAsync(friend.Code);
    }

    // ------------------------------------------------------------------ actions

    private void Friends_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsStore.Update(s => s.Friends.Enabled = FriendsToggle.IsChecked == true);
        ShowFriends();
    }

    private void FriendsOption_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsStore.Update(s =>
        {
            s.Friends.Invisible = FriendsInvisibleToggle.IsChecked == true;
            s.Friends.ShareGame = FriendsShareGameToggle.IsChecked == true;
            s.Friends.Notify = FriendsNotifyToggle.IsChecked == true;
        });
    }

    private void FriendsServerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SaveFriendsServerAsync();
    }

    private void FriendsServerSave_Click(object sender, RoutedEventArgs e) => _ = SaveFriendsServerAsync();

    /// <summary>L'adresse est d'abord essayée (le serveur doit répondre « opaline-amis ») avant d'être gardée.</summary>
    private async Task SaveFriendsServerAsync()
    {
        var server = FriendsServerBox.Text.Trim().TrimEnd('/');
        if (!server.StartsWith("http", StringComparison.OrdinalIgnoreCase) && server.Length > 0) server = "https://" + server;
        if (!FriendsApi.ValidServer(server))
        {
            ShowFriendsMessage(FriendsServerStatus, Loc.T("settings.friends.server.invalid"));
            return;
        }
        FriendsServerStatus.Text = Loc.T("settings.friends.status.connecting");
        if (!await FriendsApi.PingAsync(server))
        {
            ShowFriendsMessage(FriendsServerStatus, Loc.T("settings.friends.server.wrong"));
            return;
        }
        FriendsServerBox.Text = server;
        SettingsStore.Update(s => s.Friends.Server = server);
    }

    private void FriendsNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveFriendsName();
    }

    private void FriendsNameBox_LostFocus(object sender, RoutedEventArgs e) => SaveFriendsName();

    private void SaveFriendsName()
    {
        var name = FriendsNameBox.Text.Trim();
        if (name != SettingsStore.Current.Friends.Name) SettingsStore.Update(s => s.Friends.Name = name);
    }

    private void FriendsAddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = AddFriendAsync();
    }

    private void FriendsAdd_Click(object sender, RoutedEventArgs e) => _ = AddFriendAsync();

    private async Task AddFriendAsync()
    {
        var code = FriendsAddBox.Text.Trim();
        if (code.Length == 0) return;
        var problem = await FriendsService.AddAsync(code);
        if (problem == null) FriendsAddBox.Clear();
        ShowFriendsMessage(FriendsMessageText, problem ?? Loc.T("settings.friends.added"));
    }

    private void FriendsInvite_Click(object sender, RoutedEventArgs e)
    {
        if (FriendsService.InviteText() is not { } invite) return;
        if (CopyText(invite)) ShowFriendsMessage(FriendsMessageText, Loc.T("settings.friends.invite.copied"));
    }

    /// <summary>Code du serveur (server/amis/worker.js, inclus dans l'app) à coller dans Cloudflare.</summary>
    private void FriendsCopyWorker_Click(object sender, RoutedEventArgs e)
    {
        using var stream = typeof(FriendsService).Assembly.GetManifestResourceStream("DynamicIsland.Friends.worker.js");
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        if (CopyText(reader.ReadToEnd())) ShowFriendsMessage(FriendsServerStatus, Loc.T("settings.friends.copy_server.done"));
    }

    private void FriendsOpenCloudflare_Click(object sender, RoutedEventArgs e) => Start(CloudflareUrl, "");

    private async void FriendsLeave_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("settings.friends.leave.confirm"), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await FriendsService.LeaveAsync();
        ShowFriends();
    }

    private static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static void ShowFriendsMessage(TextBlock target, string text)
    {
        target.Text = text;
        target.Visibility = Visibility.Visible;
    }

    /// <summary>Rendu --snapshot : la page avec un serveur, des amis et une demande d'exemple (rien n'est envoyé).</summary>
    internal void ShowFriendsSample()
    {
        _loading = true;
        FriendsToggle.IsChecked = true;
        FriendsShareGameToggle.IsChecked = FriendsNotifyToggle.IsChecked = true;
        FriendsServerBox.Text = "https://opaline-amis.exemple.workers.dev";
        FriendsNameBox.Text = "Louis";
        _loading = false;
        FriendsPanel.Visibility = FriendsMeCard.Visibility = FriendsListCard.Visibility = Visibility.Visible;
        FriendsGuideText.Visibility = Visibility.Collapsed;
        FriendsServerStatus.Text = Loc.T("settings.friends.status.connected");
        FriendsCodeText.Text = Loc.F("settings.friends.code", "K7QM-2XPA");
        ShowFriendList(SampleFriends);
    }

    internal static readonly FriendLists SampleFriends = new(
        [new Friend("MAXX2345", "Max", "game", "Valorant"), new Friend("LEAA6789", "Léa", "online", null), new Friend("SAMM2222", "Sam", "offline", null)],
        [new Friend("NOAH3456", "Noah", "offline", null)],
        [new Friend("JULE4567", "", "offline", null)]);
}
