using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Option « Thème clair/sombre automatique » : Windows passe en clair au lever du soleil et en sombre au coucher,
/// dans la ville de la météo (<see cref="CityLocation"/>, Montréal par défaut). Le thème n'est
/// changé qu'au passage jour/nuit : un choix fait à la main dans la journée reste jusqu'au prochain. Jamais
/// pendant un jeu (mode Gaming) : ce sera fait à la sortie. Réversible : le thème d'origine est sauvé avant le
/// premier changement (<see cref="WindowsBackup"/>, section « Theme ») et remis quand l'option est décochée.
/// </summary>
public static class AutoThemeService
{
    private const string Section = "Theme";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    internal static readonly (string Key, string Name)[] Values =
        [(PersonalizeKey, "AppsUseLightTheme"), (PersonalizeKey, "SystemUsesLightTheme")];

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private static bool? _lastDay;      // dernier passage jour/nuit vu (null = pas encore)

    public static void Start()
    {
        Timer.Tick += (_, _) => Check();
        SettingsStore.Changed += Check;
        GamingMode.Changed += Check;
        SystemEvents.TimeChanged += (_, _) => Check();
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Check(); // sortie de veille : le soleil a pu se lever entre-temps
        };
        Check();
    }

    private static void Check()
    {
        if (SettingsStore.IsReadOnly) return; // --snapshot : Windows n'est jamais touché
        if (ModuleGuard.IsDisabled(ModuleGuard.Colors))
        {
            Timer.Stop();
            return;
        }
        try
        {
            if (!SettingsStore.Current.Appearance.AutoTheme)
            {
                Timer.Stop();
                _lastDay = null;
                if (WindowsBackup.Restore(Section)) WindowsBackup.BroadcastColors();
                return;
            }
            Timer.Start();
            if (CityLocation.Get() is not { } place) return; // coordonnées pas encore connues
            bool day = SunCalculator.IsDay(DateTime.Now, place.Latitude, place.Longitude);
            if (_lastDay == day || GamingMode.IsActive) return;

            WindowsBackup.SaveIfMissing(Section, Values);
            if (ThemeService.IsDarkMode() == day) ThemeService.SetDarkMode(!day); // seulement s'il faut changer
            _lastDay = day;
        }
        catch (Exception ex)
        {
            Log.Error("Thème automatique", ex);
        }
    }

    /// <summary>« Clair de 7 h 23 à 17 h 56 (Montréal) » pour la page Apparence.</summary>
    public static string Describe()
    {
        var widgets = SettingsStore.Current.Widgets;
        if (CityLocation.Get() is not { } place) return Loc.F("sun.searching", widgets.City);
        var sun = SunCalculator.For(DateOnly.FromDateTime(DateTime.Now), place.Latitude, place.Longitude);
        string where = Loc.F("sun.where", place.Name);
        return sun.Polar switch
        {
            SunCalculator.Polar.AlwaysDay => Loc.F("sun.always_day", where),
            SunCalculator.Polar.AlwaysNight => Loc.F("sun.always_night", where),
            _ => Loc.F("sun.today", CityLocation.Hour(sun.Sunrise), CityLocation.Hour(sun.Sunset), where),
        };
    }
}
