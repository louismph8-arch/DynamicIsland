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

    public MusicView()
    {
        InitializeComponent();
        _progressTimer.Tick += (_, _) =>
        {
            UpdateProgress();
            UpdateAppVolume(); // suit aussi un changement fait dans le mélangeur de Windows
        };
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
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
        ProgressFill.Background = new SolidColorBrush(media.Accent);
        UpdateProgress();
        UpdateAppVolume();
    }

    /// <summary>Curseur du volume de l'appli musique (grisé si elle n'a pas de session audio).</summary>
    private void UpdateAppVolume()
    {
        if (!_isActive || AppVolumeSlider.IsDragging || _media is not { HasSession: true } media) return;
        bool found = _appVolume.TryRead(media.SourceAppId, out double level, out bool muted);
        AppVolumeSlider.IsEnabled = found;
        if (!found)
        {
            AppVolumeIcon.Text = Icons.VolumeMute;
            AppVolumeText.Text = "—";
            return;
        }
        AppVolumeSlider.Value = muted ? 0 : level;
        ShowAppVolume(level, muted);
    }

    private void ShowAppVolume(double level, bool muted)
    {
        AppVolumeIcon.Text = muted || level < 0.005 ? Icons.VolumeMute
            : level < 0.34 ? Icons.VolumeLow
            : level < 0.67 ? Icons.VolumeMedium
            : Icons.VolumeHigh;
        AppVolumeText.Text = muted ? Loc.T("volume.muted") : Loc.F("format.percent", Math.Round(level * 100));
    }

    private void UpdateProgress()
    {
        var media = _media;
        if (media == null) return;

        var duration = media.Duration;
        bool hasTimeline = duration > TimeSpan.FromSeconds(1);
        ProgressPanel.Visibility = hasTimeline ? Visibility.Visible : Visibility.Hidden;
        if (!hasTimeline) return;

        var position = media.Position;
        ProgressFill.Width = ProgressTrack.ActualWidth * Math.Clamp(position / duration, 0, 1);
        PositionText.Text = Format(position);
        DurationText.Text = Format(duration);
    }

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void Previous_Click(object sender, RoutedEventArgs e) => _media?.Previous();

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();

    private void Next_Click(object sender, RoutedEventArgs e) => _media?.Next();
}
