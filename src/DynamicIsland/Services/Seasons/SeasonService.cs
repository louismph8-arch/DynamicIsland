using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services.Seasons;

public enum Season { None, Halloween, Christmas, NewYear, Valentine }

/// <summary>
/// Thèmes saisonniers (6F, désactivables dans Paramètres → Apparence) : la saison du jour donne un petit effet animé sur
/// l'île (fantômes et braises pour Halloween, neige pour Noël, confettis au Nouvel An, cœurs à la Saint-Valentin).
/// « Aperçu » montre une saison pendant une minute, pour l'essayer n'importe quand.
/// </summary>
public static class SeasonService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PreviewFor = TimeSpan.FromMinutes(1);

    private static DispatcherTimer? _timer;
    private static Season? _preview;
    private static DateTime _previewUntil;

    /// <summary>Saison affichée (thread de l'interface).</summary>
    public static Season Current { get; private set; }

    public static event Action? Changed;

    /// <summary>Halloween du 24 octobre au 1er novembre, Noël du 1er au 26 décembre, Nouvel An le 31 et le 1er, Saint-Valentin le 13 et le 14 février.</summary>
    public static Season ForDate(DateTime date) => (date.Month, date.Day) switch
    {
        (10, >= 24) or (11, 1) => Season.Halloween,
        (12, >= 1 and <= 26) => Season.Christmas,
        (12, 31) or (1, 1) => Season.NewYear,
        (2, 13 or 14) => Season.Valentine,
        _ => Season.None,
    };

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        SettingsStore.Changed += Update;
        Update();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        SettingsStore.Changed -= Update;
        _preview = null;
        Set(Season.None);
    }

    /// <summary>Bouton « Aperçu » : cette saison pendant une minute (même hors saison, même si les thèmes sont coupés).</summary>
    public static void Preview(Season season)
    {
        _preview = season;
        _previewUntil = DateTime.UtcNow + PreviewFor;
        if (_timer != null) _timer.Interval = TimeSpan.FromSeconds(5);
        Update();
    }

    private static void Update()
    {
        if (_preview != null && DateTime.UtcNow > _previewUntil)
        {
            _preview = null;
            if (_timer != null) _timer.Interval = CheckEvery;
        }
        Set(_preview ?? (SettingsStore.Current.Appearance.Seasons ? ForDate(DateTime.Now) : Season.None));
    }

    private static void Set(Season season)
    {
        if (season == Current) return;
        Current = season;
        Changed?.Invoke();
    }
}
