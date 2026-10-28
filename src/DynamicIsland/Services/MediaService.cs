using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// <summary>
/// Suit la session média de Windows (Spotify, YouTube dans le navigateur…) via
/// GlobalSystemMediaTransportControlsSessionManager. Les événements WinRT arrivent sur
/// des threads quelconques : tout est renvoyé sur le thread UI avant de lever <see cref="Changed"/>.
/// </summary>
public sealed class MediaService
{
    public static readonly Color DefaultAccent = Themes.Swatch.Pearl;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rescan = new() { Interval = TimeSpan.FromSeconds(2) };
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _sessionId = "";
    private int _propertiesVersion;
    private TimeSpan _position;
    private DateTimeOffset _positionStamp = DateTimeOffset.Now;
    private bool _demo;

    public MediaService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _rescan.Tick += (_, _) => PickSession();
    }

    /// <summary>Levé sur le thread UI à chaque changement (titre, pochette, lecture, position).</summary>
    public event Action? Changed;

    public bool HasSession => _session != null || _demo;
    /// <summary>Identifiant de l'appli de la session (« Spotify.exe », « Chrome », « Paquet!App »…), vide sans session.</summary>
    public string SourceAppId => _sessionId;
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string AppName { get; private set; } = "";
    public BitmapSource? Cover { get; private set; }
    public Color Accent { get; private set; } = DefaultAccent;
    public bool IsPlaying { get; private set; }
    public bool CanGoPrevious { get; private set; }
    public bool CanGoNext { get; private set; }
    public TimeSpan Duration { get; private set; }

    /// <summary>Position interpolée : les applis ne publient la position qu'à chaque événement.</summary>
    public TimeSpan Position
    {
        get
        {
            var position = IsPlaying ? _position + (DateTimeOffset.Now - _positionStamp) : _position;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Média : gestionnaire indisponible", ex);
            return;
        }

        _manager.SessionsChanged += (_, _) => _dispatcher.InvokeAsync(PickSession);
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(PickSession);
        PickSession();
        _rescan.Start();
    }

    /// <summary>
    /// Rendus <c>--snapshot --demo</c> (README, page de présentation — 7B) : un morceau inventé avec une pochette dessinée par
    /// l'app, à la place de ce qui joue vraiment sur le PC (rien de personnel ni de protégé dans les images publiées).
    /// </summary>
    internal void UseDemo()
    {
        Bind(null);
        _demo = true;
        _rescan.Stop();
        var cover = WallpaperArt.Render(WallpaperDesign.Prisme, Themes.Wallpapers.Sunset, 300, 300, 4.2);
        Title = "Lumières de minuit";
        Artist = "Opaline Sessions";
        AppName = "Spotify";
        Cover = cover;
        Accent = ExtractAccent(cover);
        Duration = TimeSpan.FromSeconds(222);
        _position = TimeSpan.FromSeconds(77);
        _positionStamp = DateTimeOffset.Now;
        IsPlaying = true;
        CanGoPrevious = CanGoNext = true;
        Changed?.Invoke();
    }

    public async void TogglePlayPause()
    {
        var session = _session;
        if (session == null) return;

        // Retour visuel immédiat, la vraie valeur arrive ensuite par PlaybackInfoChanged.
        _position = Position;
        _positionStamp = DateTimeOffset.Now;
        IsPlaying = !IsPlaying;
        Changed?.Invoke();

        try { await session.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { Log.Error("Média : lecture/pause", ex); }
    }

    public async void Next()
    {
        var session = _session;
        if (session == null) return;
        try { await session.TrySkipNextAsync(); }
        catch (Exception ex) { Log.Error("Média : suivant", ex); }
    }

    public async void Previous()
    {
        var session = _session;
        if (session == null) return;
        try { await session.TrySkipPreviousAsync(); }
        catch (Exception ex) { Log.Error("Média : précédent", ex); }
    }

    /// <summary>
    /// Choisit la session à afficher : celle qui joue en priorité, sinon on garde la
    /// session actuelle, sinon celle que Windows considère comme « courante ».
    /// </summary>
    private void PickSession()
    {
        if (_manager == null || _demo) return;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions(); }
        catch { return; }

        var bound = sessions.FirstOrDefault(IsBound);
        var best = bound != null && IsSessionPlaying(bound)
            ? bound
            : sessions.FirstOrDefault(IsSessionPlaying) ?? bound ?? TryGetCurrentSession() ?? sessions.FirstOrDefault();

        if (best == null)
        {
            if (_session != null) Bind(null);
            return;
        }
        if (ReferenceEquals(best, bound)) return;
        Bind(best);

        bool IsBound(GlobalSystemMediaTransportControlsSession s) =>
            _session != null &&
            (ReferenceEquals(s, _session) ||
             (SafeId(s) == _sessionId && sessions.Count(x => SafeId(x) == _sessionId) == 1));
    }

    private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private GlobalSystemMediaTransportControlsSession? TryGetCurrentSession()
    {
        try { return _manager?.GetCurrentSession(); }
        catch { return null; }
    }

    private void Bind(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;
        _sessionId = session != null ? SafeId(session) : "";
        _propertiesVersion++;
        AppName = FriendlyAppName(_sessionId);
        IsPlaying = false;

        if (session == null)
        {
            Title = Artist = "";
            Cover = null;
            Accent = DefaultAccent;
            Duration = TimeSpan.Zero;
            Changed?.Invoke();
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        UpdatePlayback();
        UpdateTimeline();
        // Changed sera levé une fois titre + pochette chargés (évite un flash « vide »).
        _ = UpdatePropertiesAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _dispatcher.InvokeAsync(() => { _ = UpdatePropertiesAsync(); });

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => _dispatcher.InvokeAsync(() => { UpdatePlayback(); Changed?.Invoke(); });

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => _dispatcher.InvokeAsync(() => { UpdateTimeline(); Changed?.Invoke(); });

    private void UpdatePlayback()
    {
        try
        {
            var info = _session?.GetPlaybackInfo();
            bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing != IsPlaying)
            {
                // Fige la position au moment du changement pour que l'interpolation reparte de là.
                _position = Position;
                _positionStamp = DateTimeOffset.Now;
            }
            IsPlaying = playing;
            CanGoPrevious = info?.Controls.IsPreviousEnabled ?? false;
            CanGoNext = info?.Controls.IsNextEnabled ?? false;
        }
        catch (Exception ex)
        {
            Log.Error("Média : état de lecture", ex);
        }
    }

    private void UpdateTimeline()
    {
        try
        {
            var timeline = _session?.GetTimelineProperties();
            if (timeline == null)
            {
                Duration = TimeSpan.Zero;
                return;
            }
            Duration = timeline.EndTime - timeline.StartTime;
            _position = timeline.Position - timeline.StartTime;
            var stamp = timeline.LastUpdatedTime;
            var now = DateTimeOffset.Now;
            _positionStamp = stamp > now || now - stamp > TimeSpan.FromHours(1) ? now : stamp;
        }
        catch (Exception ex)
        {
            Log.Error("Média : progression", ex);
        }
    }

    private async Task UpdatePropertiesAsync()
    {
        var session = _session;
        if (session == null) return;
        int version = ++_propertiesVersion;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _propertiesVersion || props == null) return;

            BitmapSource? cover = null;
            var accent = DefaultAccent;
            if (props.Thumbnail != null)
                (cover, accent) = await LoadCoverAsync(props.Thumbnail);
            if (version != _propertiesVersion) return;

            Title = props.Title ?? "";
            Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            Cover = cover;
            Accent = accent;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Média : métadonnées", ex);
        }
    }

    private static async Task<(BitmapSource? Cover, Color Accent)> LoadCoverAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            if (buffer.Length == 0) return (null, DefaultAccent);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 240;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return (image, ExtractAccent(image));
        }
        catch (Exception ex)
        {
            Log.Error("Média : pochette", ex);
            return (null, DefaultAccent);
        }
    }

    /// <summary>Couleur dominante « vive » de la pochette (pondérée par la saturation).</summary>
    private static Color ExtractAccent(BitmapSource source)
    {
        var small = new TransformedBitmap(source, new ScaleTransform(16.0 / source.PixelWidth, 16.0 / source.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
            double max = Math.Max(pr, Math.Max(pg, pb));
            double min = Math.Min(pr, Math.Min(pg, pb));
            double saturation = max == 0 ? 0 : (max - min) / max;
            double weight = saturation * saturation * (max / 255) + 0.01;
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }

        var (h, s, v) = ColorTools.ToHsv(r / total, g / total, b / total);
        if (s < 0.12) return DefaultAccent;
        return ColorTools.FromHsv(h, Math.Min(s * 1.15, 0.85), Math.Max(v, 0.9));
    }

    private static string SafeId(GlobalSystemMediaTransportControlsSession session)
    {
        try { return session.SourceAppUserModelId ?? ""; }
        catch { return ""; }
    }

    private static string FriendlyAppName(string appId)
    {
        if (appId.Length == 0) return "";
        var id = appId.ToLowerInvariant();
        if (id.Contains("spotify")) return "Spotify";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("brave")) return "Brave";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("zunemusic") || id.Contains("media.player")) return Loc.T("media.player");

        var name = appId.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        int underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }
}
