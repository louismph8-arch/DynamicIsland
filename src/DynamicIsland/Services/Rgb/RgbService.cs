using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// Éclairage RGB (6D-2, module « rgb », désactivé par défaut) : les appareils Logitech (par G HUB) et ceux d'OpenRGB
/// prennent la couleur d'accent d'Opaline — donc celle du profil actif —, ou la couleur de la pochette quand de la musique
/// joue (option). Pendant une partie, l'éclairage est rendu à G HUB / OpenRGB (option, active par défaut). Coupé, G HUB
/// reprend ses effets. Les appareils sont pilotés sur un fil à part, un appel à la fois, avec un fondu court.
/// </summary>
public static class RgbService
{
    public enum LogitechState { Off, Connected, NotResponding, NotInstalled }

    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);
    private const int FadeSteps = 8;

    private static readonly LogitechLighting Logitech = new();
    private static readonly OpenRgbClient OpenRgb = new();
    private static readonly object Gate = new();
    private static Task _queue = Task.CompletedTask;
    private static Func<MediaService?> _media = () => null;
    private static MediaService? _watchedMedia;
    private static DispatcherTimer? _retry;
    private static bool _started, _active, _forceRetry;
    private static bool _lit; // fil des appareils seulement : une couleur est déjà affichée
    private static Color? _wanted;
    private static Color _shown = Colors.Black;

    /// <summary>État des appareils changé (thread de l'interface).</summary>
    public static event Action? Changed;

    public static LogitechState LogitechStatus { get; private set; }
    /// <summary>Appareils OpenRGB (-1 : OpenRGB introuvable).</summary>
    public static int OpenRgbDevices { get; private set; } = -1;
    /// <summary>Couleur en ce moment sur les appareils (null : rien n'est piloté).</summary>
    public static Color? Current { get; private set; }
    /// <summary>En pause pendant une partie.</summary>
    public static bool Paused { get; private set; }

    private static RgbSettings Settings => SettingsStore.Current.Rgb;

    public static void Start(Func<MediaService?> media)
    {
        if (_started) return;
        _started = true;
        _media = media;
        SettingsStore.Changed += Apply;
        GamingMode.Changed += Apply;
        _retry = new DispatcherTimer { Interval = RetryEvery };
        _retry.Tick += (_, _) =>
        {
            if (_active && (LogitechStatus == LogitechState.NotResponding || OpenRgbDevices < 0)) Retry();
        };
        Apply();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SettingsStore.Changed -= Apply;
        GamingMode.Changed -= Apply;
        if (_watchedMedia != null) _watchedMedia.Changed -= Apply;
        _watchedMedia = null;
        _retry?.Stop();
        _active = false;
        _wanted = null;
        Enqueue(Release);
    }

    /// <summary>Bouton « Réessayer » (G HUB ou OpenRGB lancé après Opaline).</summary>
    public static void Retry()
    {
        _forceRetry = true;
        Apply();
    }

    private static void Apply()
    {
        if (!_started) return;
        WatchMedia();
        var settings = Settings;
        bool enabled = settings.Enabled && !App.SafeMode && !ModuleGuard.IsDisabled(ModuleGuard.Rgb);
        bool paused = enabled && settings.PauseInGames && GamingMode.IsActive;
        bool active = enabled && !paused;
        var color = active ? Target(settings) : (Color?)null;
        if (active == _active && color == _wanted && paused == Paused && !_forceRetry) return;
        bool retry = _forceRetry;
        _forceRetry = false;
        _active = active;
        _wanted = color;
        Paused = paused;
        if (active) _retry?.Start();
        else _retry?.Stop();

        if (!active)
        {
            Enqueue(Release);
            return;
        }
        Enqueue(() =>
        {
            if (retry) OpenRgb.Stop();
            if (!Logitech.IsRunning)
                LogitechStatus = LogitechLighting.SdkPath == null ? LogitechState.NotInstalled
                    : Logitech.Start() ? LogitechState.Connected : LogitechState.NotResponding;
            if (!OpenRgb.IsConnected) OpenRgbDevices = OpenRgb.Start() ? OpenRgb.Devices.Count : -1;
            Fade(color!.Value);
            Publish(color);
        });
    }

    /// <summary>Accent d'Opaline (celui du profil actif), ou la pochette de la musique en cours si l'option est choisie.</summary>
    private static Color Target(RgbSettings settings)
    {
        if (settings.FollowMusic && _media() is { HasSession: true, IsPlaying: true } media && media.Accent != MediaService.DefaultAccent)
            return media.Accent;
        return AppearanceService.AccentStart;
    }

    /// <summary>La musique de l'île est créée avec elle : on s'y abonne dès qu'elle existe.</summary>
    private static void WatchMedia()
    {
        var media = _media();
        if (ReferenceEquals(media, _watchedMedia)) return;
        if (_watchedMedia != null) _watchedMedia.Changed -= Apply;
        _watchedMedia = media;
        if (media != null) media.Changed += Apply;
    }

    /// <summary>Fondu court de la couleur affichée vers la nouvelle (sur le fil des appareils).</summary>
    private static void Fade(Color target)
    {
        var from = _shown;
        bool first = !_lit;
        _lit = true;
        for (int step = 1; step <= (first ? 1 : FadeSteps); step++)
        {
            double t = first ? 1 : (double)step / FadeSteps;
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
            byte r = Mix(from.R, target.R), g = Mix(from.G, target.G), b = Mix(from.B, target.B);
            if (Logitech.IsRunning && !Logitech.SetColor(r, g, b)) LogitechStatus = LogitechState.NotResponding;
            if (OpenRgb.IsConnected && !OpenRgb.SetColor(r, g, b)) OpenRgbDevices = -1;
            if (!first) Thread.Sleep(30);
        }
        _shown = target;
    }

    private static void Release()
    {
        Logitech.Stop();
        OpenRgb.Stop();
        _lit = false;
        LogitechStatus = LogitechState.Off;
        OpenRgbDevices = -1;
        Publish(null);
    }

    private static void Publish(Color? color) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        Current = color;
        Changed?.Invoke();
    });

    /// <summary>Un appel aux appareils à la fois, hors du thread de l'interface ; une erreur est notée, pas propagée.</summary>
    private static void Enqueue(Action work)
    {
        lock (Gate)
        {
            _queue = _queue.ContinueWith(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Log.Error("RVB", ex);
                }
            }, TaskScheduler.Default);
        }
    }
}
