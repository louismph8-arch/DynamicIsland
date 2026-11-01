using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

public partial class MusicView : UserControl
{
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MediaService? _media;
    private readonly AppVolumeService _appVolume = new();
    private BitmapSource? _shownCover;
    private bool _isActive;
    /// <summary>Temps visé pendant un glisser sur la barre de position (null hors glisser).</summary>
    private TimeSpan? _preview;
    private bool _appMuted;

    public MusicView()
    {
        InitializeComponent();
        _progressTimer.Tick += (_, _) =>
        {
            UpdateProgress();
            UpdateAppVolume(); // suit aussi un changement fait dans le mélangeur de Windows
        };
        SeekBar.SizeChanged += (_, _) => UpdateProgress();
        Numerals.Apply(PositionText, TypeRamp.Caption2);
        Numerals.Apply(DurationText, TypeRamp.Caption2);
        Numerals.Apply(AppVolumeText, TypeRamp.Caption2);

        // Glisser : on montre le temps visé sans toucher au lecteur ; lâcher (ou clic) = on y va.
        SeekBar.Previewing += fraction =>
        {
            if (_media == null) return;
            _preview = SeekMath.At(fraction, 1, _media.Duration);
            PositionText.Text = Format(_preview.Value);
        };
        SeekBar.Committed += fraction =>
        {
            if (_media == null) return;
            _media.Seek(SeekMath.At(fraction, 1, _media.Duration));
        };
        SeekBar.DragEnded += () =>
        {
            _preview = null;
            UpdateProgress();
        };
        SeekBar.Wheeled += delta =>
        {
            if (_media == null || !_media.CanSeek) return;
            _media.Seek(SeekMath.Wheel(_media.Position, delta, _media.Duration));
        };

        AppVolumeSlider.UserChanged += value =>
        {
            _appVolume.SetLevel(_media?.SourceAppId, value);
            ShowAppVolume(value, muted: false);
        };
    }

    /// <summary>Vrai quand l'onglet est visible : on rafraîchit alors la barre de progression.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            if (value)
            {
                Update();
                _progressTimer.Start();
            }
            else
            {
                _progressTimer.Stop();
            }
        }
    }

    public void Attach(MediaService media)
    {
        _media = media;
        media.Changed += Update;
        Update();
    }

    private void Update()
    {
        var media = _media;
        if (media == null) return;

        bool hasTrack = media.HasSession && media.Title.Length > 0;
        PlayerPanel.Visibility = hasTrack ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = hasTrack ? Visibility.Collapsed : Visibility.Visible;
        if (!hasTrack) return;

        TitleText.Text = media.Title;
        ArtistText.Text = string.Join(" · ", new[] { media.Artist, media.AppName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!ReferenceEquals(_shownCover, media.Cover))
        {
            _shownCover = media.Cover;
            CoverBorder.Background = media.Cover != null
                ? new ImageBrush(media.Cover) { Stretch = Stretch.UniformToFill }
                : Paint.Surface;
            CoverPlaceholder.Visibility = media.Cover != null ? Visibility.Collapsed : Visibility.Visible;
        }

        PlayButton.Content = media.IsPlaying ? Icons.Pause : Icons.Play;
        PrevButton.IsEnabled = media.CanGoPrevious;
        NextButton.IsEnabled = media.CanGoNext;
        SeekBar.FillBrush = new SolidColorBrush(media.Accent);
        SeekBar.Interactive = media.CanSeek;
        SeekBar.ToolTip = Loc.T(media.CanSeek ? "music.seek.tip" : "music.seek.unavailable");
        UpdateProgress();
        UpdateAppVolume();
    }

    /// <summary>Curseur du volume de l'appli musique (grisé si elle n'a pas de session audio).</summary>
    private void UpdateAppVolume()
    {
        if (!_isActive || AppVolumeSlider.IsDragging || _media is not { HasSession: true } media) return;
        double level;
        bool muted = false;
        // Rendus --demo : un volume d'exemple, jamais celui des applis du PC.
        bool found = App.SnapshotDemo ? (level = 0.72) > 0 : _appVolume.TryRead(media.SourceAppId, out level, out muted);
        // Introuvable (l'appli n'a pas encore ouvert de son : Spotify en pause depuis son lancement…) : un message plutôt
        // qu'un curseur mort.
        AppVolumeSlider.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        AppVolumeText.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        AppVolumeMissing.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        AppVolumeButton.IsEnabled = found;
        if (!found)
        {
            AppVolumeButton.Content = Icons.VolumeMute;
            AppVolumeButton.ToolTip = null;
            var app = media.AppName.Length > 0 ? media.AppName : Loc.T("music.volume.this_app");
            // Ça joue mais aucune sortie audio du PC n'a de son pour cette appli : lecture sur un autre appareil (Spotify
            // Connect, téléphone…), pas « en attente du premier son ».
            AppVolumeMissing.Text = Loc.F(media.IsPlaying ? "music.volume.elsewhere" : "music.volume.missing", app);
            return;
        }
        AppVolumeSlider.Value = muted ? 0 : level;
        ShowAppVolume(level, muted);
    }

    private void ShowAppVolume(double level, bool muted)
    {
        _appMuted = muted;
        AppVolumeButton.Content = muted || level < 0.005 ? Icons.VolumeMute
            : level < 0.34 ? Icons.VolumeLow
            : level < 0.67 ? Icons.VolumeMedium
            : Icons.VolumeHigh;
        AppVolumeButton.ToolTip = Loc.F(muted ? "music.volume.unmute" : "music.volume.mute", _media?.AppName is { Length: > 0 } app ? app : Loc.T("music.volume.this_app"));
        AppVolumeText.Text = muted ? Loc.T("volume.muted") : Loc.F("format.percent", Math.Round(level * 100));
    }

    private void AppVolumeMute_Click(object sender, RoutedEventArgs e)
    {
        if (_media is not { HasSession: true } media || App.SnapshotDemo) return;
        if (!_appVolume.SetMute(media.SourceAppId, !_appMuted)) return;
        UpdateAppVolume();
    }

    private void UpdateProgress()
    {
        var media = _media;
        if (media == null) return;

        var duration = media.Duration;
        bool hasTimeline = duration > TimeSpan.FromSeconds(1);
        ProgressPanel.Visibility = hasTimeline ? Visibility.Visible : Visibility.Hidden;
        if (!hasTimeline) return;

        // Pendant un glisser, la barre et le temps suivent la souris, pas le lecteur.
        if (SeekBar.IsDragging && _preview is { } preview)
        {
            PositionText.Text = Format(preview);
            DurationText.Text = Format(duration);
            return;
        }
        var position = media.Position;
        SeekBar.Value = SeekMath.Fraction(position, duration);
        PositionText.Text = Format(position);
        DurationText.Text = Format(duration);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void Previous_Click(object sender, RoutedEventArgs e) => _media?.Previous();

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();

    private void Next_Click(object sender, RoutedEventArgs e) => _media?.Next();
}
