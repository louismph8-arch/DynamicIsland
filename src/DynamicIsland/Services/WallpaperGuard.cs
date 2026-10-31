using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Correctif urgent 6 : le fond d'Opaline (fond du profil ou fond dynamique) reste affiché même si Windows, un thème, Windows
/// Spotlight, un diaporama ou une autre appli le change. Détection instantanée (message WM_SETTINGCHANGE du bureau + clés du
/// registre où Windows note le fond), vérification de secours toutes les 20 s. Option « Garder le fond d'Opaline »
/// (Paramètres → Fond d'écran, active par défaut) ; rien n'est imposé si le profil n'a pas de fond, si Wallpaper Engine tourne
/// ou si le fond animé est affiché (il passe déjà devant). Garde-fou : une appli qui le remet sans arrêt → on arrête 10 min.
/// </summary>
public static class WallpaperGuard
{
    private static readonly DispatcherTimer Soon = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private static readonly DispatcherTimer Fallback = new() { Interval = TimeSpan.FromSeconds(20) };
    private static readonly List<DateTime> RecentFixes = [];
    private static RegistryWatcher? _desktopKey, _wallpapersKey;
    private static DateTime _pausedUntil;
    private static bool _started, _checking;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Soon.Tick += (_, _) =>
        {
            Soon.Stop();
            _ = CheckAsync();
        };
        Fallback.Tick += (_, _) => _ = CheckAsync();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop) Guarded.OnUi(CheckSoon, "Fond d'écran : changement");
        };
        GamingMode.Changed += () =>
        {
            if (!GamingMode.IsActive) CheckSoon(); // attendu pendant la partie
        };
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        Soon.Stop();
        Fallback.Stop();
        _desktopKey?.Dispose();
        _wallpapersKey?.Dispose();
        _desktopKey = _wallpapersKey = null;
    }

    private static bool Wanted =>
        SettingsStore.Current.Wallpaper.Enforce && !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Wallpapers);

    private static void Apply()
    {
        if (!Wanted)
        {
            Stop();
            return;
        }
        if (!Fallback.IsEnabled) Fallback.Start();
        _desktopKey ??= RegistryWatcher.TryStart(Registry.CurrentUser, @"Control Panel\Desktop", () => Guarded.OnUi(CheckSoon, "Fond d'écran : registre"));
        _wallpapersKey ??= RegistryWatcher.TryStart(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers",
            () => Guarded.OnUi(CheckSoon, "Fond d'écran : registre"));
    }

    private static void CheckSoon()
    {
        if (!Wanted) return;
        Soon.Stop();
        Soon.Start();
    }

    /// <summary>Décision seule (vérifiée par l'auto-test) : faut-il remettre notre fond ?</summary>
    internal static bool ShouldReimpose(string? imposed, bool showsImposed, bool engineRunning, bool animated, bool gaming, bool justSet) =>
        imposed != null && !showsImposed && !engineRunning && !animated && !gaming && !justSet;

    private static async Task CheckAsync()
    {
        if (_checking || !Wanted || DateTime.UtcNow < _pausedUntil) return;
        var imposed = WallpaperService.Imposed;
        if (imposed == null) return; // profil sans fond : le choix de Windows est respecté
        _checking = true;
        try
        {
            bool engine = AnimatedWallpaper.IsWallpaperEngineRunning;
            bool shows = await Task.Run(() => WallpaperService.Shows(imposed));
            if (!ShouldReimpose(imposed, shows, engine, SettingsStore.Current.Wallpaper.Animated, GamingMode.IsActive, WallpaperService.JustSet)) return;

            var now = DateTime.UtcNow;
            RecentFixes.RemoveAll(t => now - t > TimeSpan.FromMinutes(2));
            if (RecentFixes.Count >= 5)
            {
                _pausedUntil = now.AddMinutes(10); // une autre appli le change sans arrêt : on ne se bat pas
                Log.Info("Fond d'écran : changé sans arrêt par une autre appli, Opaline arrête de le remettre pendant 10 min");
                return;
            }
            RecentFixes.Add(now);
            Log.Info("Fond d'écran : changé par Windows ou une autre appli, celui d'Opaline est remis");
            WallpaperService.Reimpose(imposed);
        }
        catch (Exception ex)
        {
            Log.Error("Fond d'écran : vérification", ex);
        }
        finally
        {
            _checking = false;
        }
    }
}
