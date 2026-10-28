using System.IO;
using System.Windows.Threading;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using W = DynamicIsland.Themes.Wallpapers;

namespace DynamicIsland.Services;

/// <summary>
/// Option « Fond dynamique » : le fond de Windows (dessin du pack choisi) prend les couleurs du moment — nuit, aube,
/// matin, jour, fin d'après-midi, coucher de soleil — d'après le lever et le coucher du soleil de la ville de la
/// météo. Les couleurs glissent en continu : une nouvelle image au plus toutes les 10 minutes, seulement si elles ont
/// assez bougé, et Windows passe de l'une à l'autre en fondu. Jamais pendant un jeu. Décocher remet le fond du profil.
/// </summary>
public static class DynamicWallpaperService
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private static Mood? _applied;
    private static WallpaperDesign? _appliedDesign;
    private static DateTime _appliedAt;
    private static bool _rendering, _flip;

    /// <summary>Ambiance du moment changée (le fond animé la suit).</summary>
    public static event Action? MoodChanged;

    public static void Start()
    {
        Timer.Tick += (_, _) => Check();
        SettingsStore.Changed += Check;
        GamingMode.Changed += Check;
        SystemEvents.TimeChanged += (_, _) => Check();
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Check();
        };
        Check();
    }

    /// <summary>Ambiance du moment (celle de la nuit tant que la ville n'est pas trouvée).</summary>
    public static Mood CurrentMood() => CityLocation.Get() is { } place ? MoodAt(DateTime.Now, place.Latitude, place.Longitude) : W.Night;

    /// <summary>Couleurs à cet instant : interpolées entre les moments clés autour du lever et du coucher du soleil.</summary>
    public static Mood MoodAt(DateTime now, double latitude, double longitude)
    {
        var sun = SunCalculator.For(DateOnly.FromDateTime(now), latitude, longitude);
        if (sun.Polar == SunCalculator.Polar.AlwaysDay) return W.Day;
        if (sun.Polar == SunCalculator.Polar.AlwaysNight) return W.Night;
        (DateTime At, Mood Mood)[] keys =
        [
            (sun.Sunrise.AddMinutes(W.DawnStart), W.Night),
            (sun.Sunrise.AddMinutes(W.Sunrise), W.Morning),
            (sun.Sunrise.AddMinutes(W.MorningEnd), W.Day),
            (sun.Sunset.AddMinutes(W.AfternoonEnd), W.Day),
            (sun.Sunset.AddMinutes(W.Dusk), W.Sunset),
            (sun.Sunset.AddMinutes(W.NightStart), W.Night),
        ];
        if (now <= keys[0].At || now >= keys[^1].At) return W.Night;
        for (int i = 1; i < keys.Length; i++)
        {
            if (now > keys[i].At) continue;
            double t = (now - keys[i - 1].At) / (keys[i].At - keys[i - 1].At);
            return Mood.Lerp(keys[i - 1].Mood, keys[i].Mood, t);
        }
        return W.Night;
    }

    /// <summary>« Couleurs du jour en ce moment · coucher de soleil dès 16 h 25 » pour la page Fond d'écran.</summary>
    public static string Describe()
    {
        if (CityLocation.SunToday() is not { } sun) return Loc.T("dynamic.searching");
        if (sun.Polar != SunCalculator.Polar.None) return Loc.T("dynamic.polar");
        var now = DateTime.Now;
        string moment = Loc.T(now < sun.Sunrise.AddMinutes(W.DawnStart) || now >= sun.Sunset.AddMinutes(W.NightStart) ? "dynamic.moment.night"
            : now < sun.Sunrise.AddMinutes(W.MorningEnd) ? "dynamic.moment.morning"
            : now < sun.Sunset.AddMinutes(W.AfternoonEnd) ? "dynamic.moment.day"
            : "dynamic.moment.sunset");
        return Loc.F("dynamic.describe", moment, CityLocation.Hour(sun.Sunrise.AddMinutes(W.DawnStart)),
            CityLocation.Hour(sun.Sunset.AddMinutes(W.Dusk)), CityLocation.Hour(sun.Sunset.AddMinutes(W.NightStart)));
    }

    private static void Check()
    {
        if (SettingsStore.IsReadOnly) return; // --snapshot : Windows n'est jamais touché
        if (ModuleGuard.IsDisabled(ModuleGuard.Wallpapers))
        {
            Timer.Stop(); // arrêté après une erreur : le fond reste tel qu'il est jusqu'au prochain lancement
            return;
        }
        try
        {
            var settings = SettingsStore.Current.Wallpaper;
            if (!settings.Dynamic)
            {
                Timer.Stop();
                if (_applied == null) return;
                _applied = null;
                _appliedDesign = null;
                if (ProfileService.Active is { } profile) WallpaperService.ApplyFor(profile); // fond du profil (ou d'origine)
                return;
            }
            Timer.Start();
            if (_rendering || GamingMode.IsActive || CityLocation.Get() is not { } place) return;

            var mood = MoodAt(DateTime.Now, place.Latitude, place.Longitude);
            bool designChanged = _appliedDesign != settings.Design;
            bool due = DateTime.UtcNow - _appliedAt >= W.DynamicStep && _applied is { } last && Differs(last, mood);
            if (_applied != null && !designChanged && !due) return;
            Render(settings.Design, mood);
        }
        catch (Exception ex)
        {
            Log.Error("Fond dynamique", ex);
        }
    }

    private static void Render(WallpaperDesign design, Mood mood)
    {
        _rendering = true;
        var (bounds, _, _) = ScreenInfo.Primary();
        int width = Math.Max(640, bounds.Right - bounds.Left), height = Math.Max(360, bounds.Bottom - bounds.Top);
        // Deux fichiers en alternance : Windows ne relit pas une image qui garde le même chemin.
        _flip = !_flip;
        var path = Path.Combine(AppPaths.DataFolder, "Fonds", _flip ? "dynamique-a.png" : "dynamique-b.png");
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => WallpaperArt.SavePng(WallpaperArt.Render(design, mood, width, height), path))
            .ContinueWith(task => dispatcher.BeginInvoke(() =>
            {
                _rendering = false;
                if (task.Exception != null)
                {
                    Log.Error("Fond dynamique : image", task.Exception.InnerException ?? task.Exception);
                    return;
                }
                if (!SettingsStore.Current.Wallpaper.Dynamic) return; // coupé pendant le calcul
                WallpaperService.Apply(path);
                _applied = mood;
                _appliedDesign = design;
                _appliedAt = DateTime.UtcNow;
                MoodChanged?.Invoke();
            }));
    }

    private static bool Differs(Mood a, Mood b) =>
        new[] { (a.Deep, b.Deep), (a.Base, b.Base), (a.Accent1, b.Accent1), (a.Accent2, b.Accent2), (a.Glow, b.Glow) }
            .Any(p => Math.Abs(p.Item1.R - p.Item2.R) >= W.MinColorChange || Math.Abs(p.Item1.G - p.Item2.G) >= W.MinColorChange
                      || Math.Abs(p.Item1.B - p.Item2.B) >= W.MinColorChange);
}
