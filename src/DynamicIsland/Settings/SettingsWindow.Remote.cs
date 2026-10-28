using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Services.Remote;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>
/// Page « Connecté » → télécommande mobile (6D) : interrupteur (avec la règle du pare-feu demandée d'abord), état du réseau,
/// QR code d'appairage (5 minutes), téléphones reliés.
/// </summary>
public partial class SettingsWindow
{
    private DispatcherTimer? _pairingTimer;
    private string? _qrLink;

    private void OnRemoteChanged() => Dispatcher.BeginInvoke(ShowRemote);

    private void ShowRemote()
    {
        var remote = SettingsStore.Current.Remote;
        bool loading = _loading;
        _loading = true;
        RemoteToggle.IsChecked = remote.Enabled;
        _loading = loading;
        RemoteToggle.IsEnabled = !SettingsStore.IsReadOnly;
        RemoteStatusCard.Visibility = RemoteDevicesCard.Visibility = WakeCard.Visibility = remote.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (remote.Enabled) _ = ShowWakeAsync();
        RemoteFirewallCard.Visibility = !remote.Enabled && remote.FirewallRule ? Visibility.Visible : Visibility.Collapsed;
        if (remote.Enabled) ShowRemoteStatus();
        else StopPairingTimer();
        ShowRemoteDevices(remote.Devices);
    }

    private void ShowRemoteStatus()
    {
        var state = RemoteControl.State;
        var code = RemoteControl.PairingCode;
        bool listening = state == RemoteControl.Status.Listening;
        bool pairing = listening && code != null;
        RemoteQrFrame.Visibility = RemoteCodeText.Visibility = CancelPairingButton.Visibility = pairing ? Visibility.Visible : Visibility.Collapsed;
        AddPhoneButton.Visibility = listening && !pairing ? Visibility.Visible : Visibility.Collapsed;
        NetworkSettingsButton.Visibility = state == RemoteControl.Status.NoPrivateNetwork ? Visibility.Visible : Visibility.Collapsed;
        RemoteUrlBox.Visibility = listening ? Visibility.Visible : Visibility.Collapsed;
        RemoteUrlBox.Text = RemoteControl.Url ?? "";

        switch (state)
        {
            case RemoteControl.Status.Listening when pairing:
                RemoteStatusTitle.Text = Loc.T("settings.remote.pairing.title");
                RemoteCodeText.Text = $"{code![..3]} {code[3..]}";
                ShowQr(RemoteControl.PairingLink);
                UpdatePairingCountdown();
                _pairingTimer ??= NewPairingTimer();
                _pairingTimer.Start();
                return;
            case RemoteControl.Status.Listening:
                RemoteStatusTitle.Text = Loc.T("settings.remote.ready");
                RemoteStatusText.Text = RemoteControl.LastPaired is { } name ? Loc.F("settings.remote.paired", name) : Loc.T("settings.remote.ready.text");
                break;
            case RemoteControl.Status.NoPrivateNetwork:
                var networks = RemoteControl.PublicNetworks.Select(n => n.Interface).Distinct().ToList();
                RemoteStatusTitle.Text = Loc.T("settings.remote.public.title");
                RemoteStatusText.Text = Loc.F("settings.remote.public.text", networks.Count > 0 ? string.Join(", ", networks) : Loc.T("settings.remote.public.unknown"));
                break;
            case RemoteControl.Status.PortBusy:
                RemoteStatusTitle.Text = Loc.F("settings.remote.port.title", SettingsStore.Current.Remote.Port);
                RemoteStatusText.Text = Loc.T("settings.remote.port.text");
                break;
            default:
                RemoteStatusTitle.Text = Loc.T("settings.remote.stopped");
                RemoteStatusText.Text = Loc.T("settings.remote.stopped.text");
                break;
        }
        StopPairingTimer();
    }

    private void ShowQr(string? link)
    {
        if (link == _qrLink) return;
        _qrLink = link;
        RemoteQr.Source = link != null ? QrCode.Encode(link)?.ToBitmap(6) : null;
    }

    private DispatcherTimer NewPairingTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            RemoteControl.CheckPairing(); // expiré : RemoteControl.Changed redessine la carte
            if (RemoteControl.PairingCode != null) UpdatePairingCountdown();
        };
        return timer;
    }

    private void UpdatePairingCountdown()
    {
        var left = RemoteControl.PairingUntil - DateTime.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        RemoteStatusText.Text = Loc.F("settings.remote.pairing.text", $"{(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    private void StopPairingTimer()
    {
        _pairingTimer?.Stop();
        _qrLink = null;
        RemoteQr.Source = null;
    }

    /// <summary>Fermer les paramètres ferme aussi l'appairage : un code ne reste pas valable sans personne pour le voir.</summary>
    private void ClosePairingOnExit()
    {
        _pairingTimer?.Stop();
        RemoteControl.ClosePairing();
    }

    private void ShowRemoteDevices(IReadOnlyList<RemoteDevice> devices)
    {
        RemoteDeviceList.Children.Clear();
        RemoteNoDeviceText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var device in devices)
        {
            var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = Icons.Smartphone,
                FontFamily = FontStack.Icons,
                FontSize = IconSize.M,
                Foreground = Paint.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Space.S4, 0),
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = device.Name, FontSize = TypeRamp.Body, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Loc.F("settings.remote.devices.added", device.Added.ToString("d MMMM", Loc.Culture)), Style = (Style)FindResource("CardDescription") });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var remove = new Button
            {
                Style = (Style)FindResource("SmallIconButton"),
                Content = Icons.Close,
                FontSize = IconSize.S,
                ToolTip = Loc.T("settings.remote.devices.remove"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var id = device.Id;
            remove.Click += (_, _) => SettingsStore.Update(s => s.Remote.Devices.RemoveAll(d => d.Id == id)); // son jeton ne marche plus
            // Contrôle complet (veille, redémarrage, arrêt, presse-papiers du PC) : téléphone par téléphone, coupé par défaut.
            var fullLabel = new TextBlock
            {
                Text = Loc.T("settings.remote.full"),
                Style = (Style)FindResource("CardDescription"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Space.S4, 0, Space.S3, 0),
            };
            Grid.SetColumn(fullLabel, 2);
            row.Children.Add(fullLabel);
            var full = new CheckBox
            {
                Style = (Style)FindResource("SwitchToggle"),
                IsChecked = device.FullControl,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.T("settings.remote.full.tip"),
                Margin = new Thickness(0, 0, Space.S4, 0),
            };
            full.Click += (_, _) => SettingsStore.Update(s =>
            {
                if (s.Remote.Devices.FirstOrDefault(d => d.Id == id) is { } phone) phone.FullControl = full.IsChecked == true;
            });
            Grid.SetColumn(full, 3);
            row.Children.Add(full);
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);
            RemoteDeviceList.Children.Add(row);
        }
    }

    // ------------------------------------------------------------------ actions

    private async void RemoteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = RemoteToggle.IsChecked == true;
        RemoteToggle.IsEnabled = false;
        RemoteMessageText.Visibility = Visibility.Collapsed;
        try
        {
            if (on) await EnableRemoteAsync();
            else await DisableRemoteAsync();
        }
        finally
        {
            ShowRemote();
        }
    }

    /// <summary>D'abord la règle du pare-feu (expliquée, puis la fenêtre administrateur de Windows), ensuite l'écoute.</summary>
    private async Task EnableRemoteAsync()
    {
        int port = SettingsStore.Current.Remote.Port;
        if (!await Task.Run(FirewallRule.Exists))
        {
            var answer = MessageBox.Show(this, Loc.F("settings.remote.firewall.confirm", port), AppInfo.Name,
                MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK) return;
            var added = await FirewallRule.AddAsync(port);
            if (added != true)
            {
                ShowRemoteMessage(Loc.T(added == null ? "settings.remote.firewall.refused" : "settings.remote.firewall.failed"));
                return;
            }
        }
        SettingsStore.Update(s =>
        {
            s.Remote.Enabled = true;
            s.Remote.FirewallRule = true;
        });
        if (SettingsStore.Current.Remote.Devices.Count == 0) RemoteControl.OpenPairing();
    }

    /// <summary>On coupe l'écoute tout de suite, puis on retire la règle (la fenêtre administrateur revient).</summary>
    private async Task DisableRemoteAsync()
    {
        SettingsStore.Update(s => s.Remote.Enabled = false);
        await RemoveFirewallRuleAsync();
    }

    private async Task RemoveFirewallRuleAsync()
    {
        if (!await Task.Run(FirewallRule.Exists))
        {
            SettingsStore.Update(s => s.Remote.FirewallRule = false);
            return;
        }
        var removed = await FirewallRule.RemoveAsync();
        if (removed == true) SettingsStore.Update(s => s.Remote.FirewallRule = false);
        else ShowRemoteMessage(Loc.T("settings.remote.firewall.kept"));
    }

    private async void RemoveFirewall_Click(object sender, RoutedEventArgs e)
    {
        await RemoveFirewallRuleAsync();
        ShowRemote();
    }

    private void AddPhone_Click(object sender, RoutedEventArgs e) => RemoteControl.OpenPairing();

    private void CancelPairing_Click(object sender, RoutedEventArgs e) => RemoteControl.ClosePairing();

    private void NetworkSettings_Click(object sender, RoutedEventArgs e) => Start(RemoteControl.NetworkSettingsUri, "");

    private void ShowRemoteMessage(string text)
    {
        RemoteMessageText.Text = text;
        RemoteMessageText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ allumer à distance (6D-1b)

    private WakeOnLan.Status? _wake;

    /// <summary>État de la carte réseau, lu à côté (powercfg prend un instant).</summary>
    private async Task ShowWakeAsync()
    {
        (_wake, _lastWake) = await Task.Run(() =>
        {
            var wake = WakeOnLan.Read();
            return (wake, WakeOnLan.LastWake(wake?.Description));
        });
        ShowWake();
    }

    private WakeOnLan.LastWakeInfo? _lastWake;

    private void ShowWake()
    {
        var backup = SettingsStore.Current.Remote.Wake;
        if (_wake is not { } wake)
        {
            WakeInfoText.Text = Loc.T("settings.wake.no_adapter");
            WakeStateText.Text = "";
            WakePrepareButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            WakeInfoText.Text = Loc.F("settings.wake.info", wake.Description, wake.Mac, wake.Broadcast);
            WakeStateText.Text = string.Join("  ·  ",
                Loc.F("settings.wake.magic", Loc.T(wake.MagicPacket == "0" ? "settings.wake.off" : "settings.wake.on")),
                Loc.F("settings.wake.armed", Loc.T(wake.Armed ? "settings.wake.yes" : "settings.wake.no")),
                Loc.F("settings.wake.fast", Loc.T(wake.FastStartup ? "settings.wake.on" : "settings.wake.off")))
                + "\n" + Loc.T(wake.Ready ? "settings.wake.ready" : "settings.wake.not_ready")
                + (_lastWake is { } last ? "\n" + WakeOnLan.Describe(last) : "");
            WakePrepareButton.Visibility = wake.Ready ? Visibility.Collapsed : Visibility.Visible;
        }
        WakeUndoButton.Visibility = backup != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void WakePrepare_Click(object sender, RoutedEventArgs e)
    {
        if (_wake is not { } wake) return;
        if (MessageBox.Show(this, Loc.F("settings.wake.confirm", wake.Description), AppInfo.Name,
                MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        WakePrepareButton.IsEnabled = false;
        var done = await WakeOnLan.PrepareAsync(wake);
        ShowWakeMessage(Loc.T(done == true ? "settings.wake.prepared" : done == null ? "settings.wake.refused" : "settings.wake.failed"));
        WakePrepareButton.IsEnabled = true;
        await Task.Delay(TimeSpan.FromSeconds(3)); // la carte redémarre
        await ShowWakeAsync();
    }

    private async void WakeUndo_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsStore.Current.Remote.Wake is not { } backup) return;
        WakeUndoButton.IsEnabled = false;
        var done = await WakeOnLan.UndoAsync(backup);
        ShowWakeMessage(Loc.T(done == true ? "settings.wake.undone" : done == null ? "settings.wake.refused" : "settings.wake.failed"));
        WakeUndoButton.IsEnabled = true;
        await Task.Delay(TimeSpan.FromSeconds(3));
        await ShowWakeAsync();
    }

    private void ShowWakeMessage(string text)
    {
        WakeMessageText.Text = text;
        WakeMessageText.Visibility = Visibility.Visible;
    }

    /// <summary>Rendu --snapshot : la carte d'appairage avec un QR et un téléphone d'exemple (rien n'écoute, rien n'est enregistré).</summary>
    internal void ShowRemoteSample()
    {
        _loading = true;
        RemoteToggle.IsChecked = true;
        _loading = false;
        RemoteStatusCard.Visibility = RemoteDevicesCard.Visibility = Visibility.Visible;
        RemoteQrFrame.Visibility = RemoteCodeText.Visibility = CancelPairingButton.Visibility = RemoteUrlBox.Visibility = Visibility.Visible;
        AddPhoneButton.Visibility = NetworkSettingsButton.Visibility = Visibility.Collapsed;
        RemoteStatusTitle.Text = Loc.T("settings.remote.pairing.title");
        RemoteStatusText.Text = Loc.F("settings.remote.pairing.text", "4:59");
        RemoteCodeText.Text = "482 913";
        RemoteUrlBox.Text = "http://192.168.1.20:48765/";
        RemoteQr.Source = QrCode.Encode("http://192.168.1.20:48765/#p=482913")?.ToBitmap(6);
        ShowRemoteDevices([new RemoteDevice { Id = "demo", Name = "iPhone", Added = DateTime.Now, FullControl = true }]);
        WakeCard.Visibility = Visibility.Visible;
        _wake = new WakeOnLan.Status("Ethernet", "Intel(R) Ethernet Connection I219-V", "D8:5E:D3:00:00:00", "192.168.1.255", "0", false, false);
        ShowWake();
    }
}
