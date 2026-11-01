using System.Windows;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Animation;

/// <summary>
/// Accessibilité du mouvement (refonte O3) : « Réduire les animations » et « Réduire la transparence », chacune « comme Windows »
/// (défaut : « Effets d'animation » = SPI_GETCLIENTAREAANIMATION, « Effets de transparence » = EnableTransparency), toujours, ou
/// jamais. Lecture seule de Windows : rien n'y est jamais écrit. <see cref="Rich"/> = les mouvements décoratifs (cascade,
/// inclinaison, glissement, outil qui s'ouvre) ont le droit de jouer : pas d'animations réduites, pas de partie en cours.
/// </summary>
public static class MotionPrefs
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool? _windowsTransparency;
    private static bool _watching;

    /// <summary>Un réglage de Windows a changé (animations ou transparence) : déjà appliqué au verre opalin.</summary>
    public static event Action? WindowsChanged;

    /// <summary>« Effets d'animation » de Windows (Paramètres → Accessibilité → Effets visuels).</summary>
    public static bool WindowsAnimations
    {
        get
        {
            Watch();
            try { return SystemParameters.ClientAreaAnimation; }
            catch (Exception) { return true; }
        }
    }

    /// <summary>« Effets de transparence » de Windows.</summary>
    public static bool WindowsTransparency
    {
        get
        {
            Watch();
            return _windowsTransparency ??= ReadTransparency();
        }
    }

    private static AppearanceSettings? Look
    {
        get
        {
            try { return SettingsStore.Current?.Appearance; }
            catch (Exception) { return null; }
        }
    }

    public static bool ReduceMotion => Resolve(Look?.ReduceMotion ?? AccessibilityChoice.Windows, !WindowsAnimations);
    public static bool ReduceTransparency => Resolve(Look?.ReduceTransparency ?? AccessibilityChoice.Windows, !WindowsTransparency);

    /// <summary>Choix enregistré + ce que demande Windows → option active ou non (calcul pur, --self-test).</summary>
    public static bool Resolve(AccessibilityChoice choice, bool windowsWantsReduce) => choice switch
    {
        AccessibilityChoice.On => true,
        AccessibilityChoice.Off => false,
        _ => windowsWantsReduce,
    };

    /// <summary>Mouvements décoratifs permis : pas d'animations réduites, pas de mode Gaming.</summary>
    public static bool Rich => !ReduceMotion && !GamingMode.IsActive;

    /// <summary>Animations réduites : tout ressort devient court et sans rebond (fondu de forme plutôt que rebond).</summary>
    public static SpringSpec Tame(SpringSpec spec) => ReduceMotion ? Springs.Calm : spec;

    private static bool ReadTransparency()
    {
        try { return Registry.GetValue(PersonalizeKey, "EnableTransparency", 1) is not 0; }
        catch (Exception) { return true; }
    }

    private static void Watch()
    {
        if (_watching) return;
        _watching = true;
        try { SystemEvents.UserPreferenceChanged += OnPreferenceChanged; }
        catch (Exception) { } // pas de fil de messages (outil en ligne de commande) : on relit au besoin
    }

    private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        bool before = _windowsTransparency ?? ReadTransparency();
        _windowsTransparency = null;
        bool changed = before != WindowsTransparency || e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility;
        if (!changed) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Design.OpalTheme.Apply(); // la signature du verre contient la transparence réduite : refait seulement si elle a bougé
                WindowsChanged?.Invoke();
            }
            catch (Exception ex) { Log.Error("Accessibilité : réglage de Windows", ex); }
        });
    }
}
