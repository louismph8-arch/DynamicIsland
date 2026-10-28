using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Vocal » de l'île : les présents du salon vocal Discord (avatar, nom, micro / son coupé) ;
/// l'anneau vert entoure ceux qui parlent. Voir <see cref="DiscordVoice"/>.
/// </summary>
public partial class VoiceView : UserControl
{
    /// <summary>Vert « en train de parler » de Discord.</summary>
    public static readonly Brush SpeakingBrush = Paint.Speaking;
    private static readonly Dictionary<string, ImageSource> Avatars = [];
    private bool _dirty = true;
    private bool _active;

    public VoiceView()
    {
        InitializeComponent();
        DiscordVoice.Changed += () =>
        {
            _dirty = true;
            if (_active) Refresh();
        };
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
        EmptyPanel.Visibility = inVoice ? Visibility.Collapsed : Visibility.Visible;
        MembersPanel.Children.Clear();

        if (!inVoice)
        {
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
        foreach (var member in DiscordVoice.Members) MembersPanel.Children.Add(CreateTile(member));
    }

    private static FrameworkElement CreateTile(VoiceMember member)
    {
        var avatar = AvatarCircle(member, Sizes.Avatar);
        var name = new TextBlock
        {
            Text = member.Name,
            FontSize = TypeRamp.Callout,
            FontWeight = member.Speaking ? FontWeights.Bold : FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120,
            Margin = new Thickness(Space.S4, 0, 0, 0),
            Opacity = member.Deafened ? Interaction.MutedOpacity : 1,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Width = Sizes.VoiceRow, Margin = new Thickness(0, 0, Space.S1, Space.S3) };
        row.Children.Add(avatar);
        row.Children.Add(name);
        if (member.Deafened || member.Muted)
        {
            row.Children.Add(new TextBlock
            {
                Text = member.Deafened ? Icons.HeadphoneOff : Icons.MicOff, // son coupé / micro barré
                FontFamily = FontStack.Icons,
                FontSize = IconSize.S,
                Foreground = Paint.Danger,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Space.S3, Space.Px, 0, 0),
            });
        }
        return row;
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
            Opacity = member.Deafened ? Interaction.MutedOpacity : 1,
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
