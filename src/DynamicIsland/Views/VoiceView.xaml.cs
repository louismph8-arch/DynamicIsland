using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Vocal » de l'île : les présents du salon vocal Discord (avatar, nom, micro / son coupé) ; l'anneau vert entoure ceux
/// qui parlent. Pour chaque personne (sauf moi) : volume 0–200 % (curseur, molette) et « couper pour moi », plus un menu au clic
/// droit — réglages LOCAUX (comme dans Discord : seulement pour moi), par la commande RPC officielle SET_USER_VOICE_SETTINGS.
/// En haut : MON micro et MON casque (SET_VOICE_SETTINGS). Voir <see cref="DiscordVoice"/>.
/// </summary>
public partial class VoiceView : UserControl
{
    /// <summary>Vert « en train de parler » de Discord.</summary>
    public static readonly Brush SpeakingBrush = Paint.Speaking;
    private static readonly Dictionary<string, ImageSource> Avatars = [];
    /// <summary>Envoi des volumes regroupé pendant un glisser (Discord n'a pas besoin de 60 commandes par seconde).</summary>
    private static readonly TimeSpan VolumeSendDelay = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<string, Row> _rows = [];
    private readonly Dictionary<string, double> _pendingVolumes = [];
    private readonly DispatcherTimer _volumeTimer = new() { Interval = VolumeSendDelay };
    private bool _dirty = true;
    private bool _active;

    private sealed class Row
    {
        public required Grid Root;
        public required Grid Avatar;
        public required TextBlock Name;
        public required TextBlock Status;
        public required LineSlider Volume;
        public required TextBlock Percent;
        public required Button Mute;
        public VoiceMember Member = null!;
        public bool Self;
    }

    public VoiceView()
    {
        InitializeComponent();
        DiscordVoice.Changed += () =>
        {
            _dirty = true;
            if (_active) Refresh();
        };
        _volumeTimer.Tick += (_, _) => SendPendingVolumes();
    }

    /// <summary>Vrai quand l'onglet est affiché : on ne redessine que dans ce cas.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            if (value && _dirty) Refresh();
        }
    }

    private void Refresh()
    {
        _dirty = false;
        bool inVoice = DiscordVoice.InVoice;
        Header.Visibility = inVoice ? Visibility.Visible : Visibility.Collapsed;
        MembersScroller.Visibility = inVoice ? Visibility.Visible : Visibility.Collapsed;
        LocalNote.Visibility = inVoice ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = inVoice ? Visibility.Collapsed : Visibility.Visible;
        WriteBanner.Visibility = inVoice && DiscordVoice.IsConnected && !DiscordVoice.CanWrite ? Visibility.Visible : Visibility.Collapsed;

        if (!inVoice)
        {
            MembersPanel.Children.Clear();
            _rows.Clear();
            bool enabled = SettingsStore.Current.Discord.Enabled;
            EmptyTitle.Text = Loc.T(!enabled ? "voice.off" : DiscordVoice.IsConnected ? "voice.no_channel" : "voice.not_connected");
            EmptyText.Text = !enabled || !DiscordVoice.IsConnected
                ? Loc.T("voice.setup")
                : Loc.T("voice.join");
            if (enabled && !DiscordVoice.IsConnected) EmptyText.Text = DiscordVoice.Status;
            return;
        }

        ChannelText.Text = DiscordVoice.Channel;
        int count = DiscordVoice.Members.Count;
        CountText.Text = count == 1 ? Loc.T("voice.one_person") : Loc.F("voice.people", count);
        ShowSelfButtons();

        // Lignes gardées d'une fois sur l'autre (sinon un curseur en cours de glisser disparaîtrait à chaque « parle / se tait »).
        var members = DiscordVoice.Members;
        var ids = members.Select(m => m.Id).ToHashSet();
        foreach (var gone in _rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            MembersPanel.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (!_rows.TryGetValue(member.Id, out var row))
            {
                row = CreateRow(member);
                _rows[member.Id] = row;
            }
            int at = MembersPanel.Children.IndexOf(row.Root);
            if (at != i)
            {
                if (at >= 0) MembersPanel.Children.RemoveAt(at);
                MembersPanel.Children.Insert(Math.Min(i, MembersPanel.Children.Count), row.Root);
            }
            UpdateRow(row, member);
        }
    }

    // ------------------------------------------------------------------ mon micro / mon casque

    private void ShowSelfButtons()
    {
        bool known = DiscordVoice.SelfMuted != null;
        bool muted = DiscordVoice.SelfMuted == true, deaf = DiscordVoice.SelfDeafened == true;
        SelfMicButton.IsEnabled = SelfDeafButton.IsEnabled = known && DiscordVoice.CanWrite;
        SelfMicButton.Content = muted || deaf ? Icons.MicOff : Icons.Voice;
        SelfDeafButton.Content = deaf ? Icons.HeadphoneOff : Icons.Headphones;
        SelfMicButton.Foreground = muted || deaf ? Paint.Danger : Paint.TextPrimary;
        SelfDeafButton.Foreground = deaf ? Paint.Danger : Paint.TextPrimary;
        SelfMicButton.ToolTip = Loc.T(muted ? "voice.self.unmute" : "voice.self.mute");
        SelfDeafButton.ToolTip = Loc.T(deaf ? "voice.self.undeafen" : "voice.self.deafen");
    }

    private void SelfMic_Click(object sender, RoutedEventArgs e) => _ = DiscordVoice.SetVoiceAsync(mute: DiscordVoice.SelfMuted != true);

    private void SelfDeaf_Click(object sender, RoutedEventArgs e) => _ = DiscordVoice.SetVoiceAsync(deaf: DiscordVoice.SelfDeafened != true);

    private void Reconnect_Click(object sender, RoutedEventArgs e) => DiscordVoice.ReconnectForWrite();

    // ------------------------------------------------------------------ une ligne par personne

    private Row CreateRow(VoiceMember member)
    {
        var grid = new Grid { Height = IslandShape.VoiceRowHeight, Background = Paint.HitTarget };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                              // avatar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandShape.VoiceName) });       // nom
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                              // micro / son coupé
        grid.ColumnDefinitions.Add(new ColumnDefinition());                                                        // volume
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                              // %
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                              // couper pour moi

        var avatar = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(avatar);
        var name = new TextBlock
        {
            FontSize = TypeRamp.Callout,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Space.S4, 0, Space.S2, 0),
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var status = new TextBlock
        {
            FontFamily = FontStack.Icons,
            FontSize = IconSize.S,
            Foreground = Paint.Danger,
            VerticalAlignment = VerticalAlignment.Center,
            Width = IconSize.S, // largeur fixe : les curseurs restent alignés, icône ou pas
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, Space.Px, Space.S3, 0),
        };
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);

        var volume = new LineSlider
        {
            VerticalAlignment = VerticalAlignment.Center,
            WheelStep = IslandShape.VoiceWheelStep / DiscordVoice.MaxUserVolume, // un cran = ±5 %
            Margin = new Thickness(Space.S2, 0, 0, 0),
        };
        Grid.SetColumn(volume, 3);
        grid.Children.Add(volume);
        var percent = new TextBlock
        {
            MinWidth = Sizes.SoftButton + Space.S3,
            TextAlignment = TextAlignment.Right,
            Foreground = Paint.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.S3, 0, 0, 0),
        };
        Numerals.Apply(percent, TypeRamp.Caption2);
        Grid.SetColumn(percent, 4);
        grid.Children.Add(percent);
        var mute = new Button
        {
            Style = (Style)FindResource("SmallIconButton"),
            Content = Icons.VolumeHigh,
            Margin = new Thickness(Space.S2, 0, 0, 0),
        };
        Grid.SetColumn(mute, 5);
        grid.Children.Add(mute);

        var row = new Row { Root = grid, Avatar = avatar, Name = name, Status = status, Volume = volume, Percent = percent, Mute = mute };
        volume.UserChanged += value =>
        {
            int level = DiscordVoice.ClampUserVolume(value * DiscordVoice.MaxUserVolume);
            row.Percent.Text = Loc.F("format.percent", level);
            _pendingVolumes[row.Member.Id] = level;
            if (!_volumeTimer.IsEnabled) _volumeTimer.Start();
        };
        mute.Click += (_, _) => _ = DiscordVoice.SetUserVoiceAsync(row.Member.Id, mute: !row.Member.LocalMuted);
        grid.MouseRightButtonUp += (_, e) =>
        {
            if (row.Self) return;
            ShowMenu(row.Member);
            e.Handled = true;
        };
        return row;
    }

    private static void UpdateRow(Row row, VoiceMember member)
    {
        row.Member = member;
        row.Self = member.Id == DiscordVoice.SelfId;
        row.Avatar.Children.Clear();
        row.Avatar.Children.Add(AvatarCircle(member, Sizes.Avatar));

        row.Name.Text = row.Self ? Loc.F("voice.self.name", member.Name) : member.Name;
        row.Name.FontWeight = member.Speaking ? FontWeights.Bold : FontWeights.SemiBold;
        row.Name.Opacity = member.Deafened || member.LocalMuted ? Interaction.MutedOpacity : 1;
        row.Status.Text = member.Deafened ? Icons.HeadphoneOff : member.Muted ? Icons.MicOff : "";
        row.Status.ToolTip = member.Deafened ? Loc.T("voice.status.deafened") : member.Muted ? Loc.T("voice.status.muted") : null;

        // Ma propre ligne : pas de réglage (Discord ne règle pas son propre volume) ; mes boutons sont en haut.
        var controls = row.Self ? Visibility.Collapsed : Visibility.Visible;
        row.Volume.Visibility = row.Percent.Visibility = row.Mute.Visibility = controls;
        if (row.Self) return;

        bool can = DiscordVoice.CanWrite;
        row.Volume.IsEnabled = row.Mute.IsEnabled = can;
        if (!row.Volume.IsDragging) row.Volume.Value = (double)member.Volume / DiscordVoice.MaxUserVolume;
        row.Percent.Text = member.LocalMuted ? Loc.T("voice.local_muted.short") : Loc.F("format.percent", member.Volume);
        row.Percent.Foreground = member.LocalMuted ? Paint.Danger : Paint.TextSecondary;
        row.Mute.Content = member.LocalMuted ? Icons.VolumeMute : Icons.VolumeHigh;
        row.Mute.Foreground = member.LocalMuted ? Paint.Danger : Paint.TextPrimary;
        row.Mute.ToolTip = Loc.F(member.LocalMuted ? "voice.local_unmute" : "voice.local_mute", member.Name);
        row.Volume.ToolTip = Loc.F("voice.volume.tip", member.Name);
    }

    private void SendPendingVolumes()
    {
        _volumeTimer.Stop();
        foreach (var (id, level) in _pendingVolumes) _ = DiscordVoice.SetUserVoiceAsync(id, volume: level);
        _pendingVolumes.Clear();
    }

    /// <summary>Clic droit : couper / rétablir, 50 / 100 / 150 %, remettre à 100 % (tout pour moi seulement).</summary>
    private static void ShowMenu(VoiceMember member)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = member.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new MenuItem { Header = Loc.T("voice.local_note"), IsEnabled = false });
        menu.Items.Add(new Separator());
        bool can = DiscordVoice.CanWrite;
        menu.Items.Add(Entry(Loc.T(member.LocalMuted ? "voice.menu.unmute" : "voice.menu.mute"), can,
            () => _ = DiscordVoice.SetUserVoiceAsync(member.Id, mute: !member.LocalMuted)));
        menu.Items.Add(new Separator());
        foreach (int level in IslandShape.VoiceMenuLevels)
        {
            var item = Entry(Loc.F("voice.menu.volume", level), can, () => _ = DiscordVoice.SetUserVoiceAsync(member.Id, volume: level));
            item.IsCheckable = false;
            item.IsChecked = member.Volume == level;
            menu.Items.Add(item);
        }
        menu.Items.Add(Entry(Loc.T("voice.menu.reset"), can && (member.Volume != DiscordVoice.DefaultUserVolume || member.LocalMuted),
            () => _ = DiscordVoice.SetUserVoiceAsync(member.Id, mute: false, volume: DiscordVoice.DefaultUserVolume)));
        menu.IsOpen = true;
    }

    private static MenuItem Entry(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Avatar rond, entouré de vert quand la personne parle (sert aussi à l'île réduite).</summary>
    public static FrameworkElement AvatarCircle(VoiceMember member, double size)
    {
        double ring = Stroke.Focus;
        var grid = new Grid { Width = size + 2 * ring, Height = size + 2 * ring };
        grid.Children.Add(new Ellipse
        {
            Stroke = member.Speaking ? SpeakingBrush : Brushes.Transparent,
            StrokeThickness = ring,
        });
        grid.Children.Add(new Ellipse
        {
            Width = size - ring,
            Height = size - ring,
            Fill = Avatar(member.AvatarUrl) is { } image
                ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }
                : Paint.SurfaceHover,
            Opacity = member.Deafened || member.LocalMuted ? Interaction.MutedOpacity : 1,
        });
        return grid;
    }

    /// <summary>Avatars téléchargés une fois (en arrière-plan par WPF) puis gardés.</summary>
    private static ImageSource? Avatar(string url)
    {
        if (Avatars.TryGetValue(url, out var cached)) return cached;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "cdn.discordapp.com") return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = 64;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            Avatars[url] = image;
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException)
        {
            return null;
        }
    }
}
