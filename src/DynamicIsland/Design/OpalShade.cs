using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Design;

/// <summary>
/// Couleurs AFFICHÉES d'Opaline (refonte O2) : accent, fond des surfaces et thème sombre / « opale blanche ». Quand le réglage,
/// le fond d'écran (couleurs automatiques) ou l'heure (thème Auto) changent, on passe des couleurs affichées aux nouvelles en
/// ~1,2 s (interpolation OKLCH, une seule transition à la fois, minuterie arrêtée à la fin). À chaque image : brosses du thème
/// (<see cref="Paint.Themed"/>, modifiées en place), ressources dynamiques (« Paint.Nom », IslandBrush, DockBrush, AccentBrush…)
/// et surfaces opalines (<see cref="OpalTheme"/>). Le contraste est vérifié sur la surface réelle et corrigé au besoin.
/// Au repos : aucune minuterie, sauf une seule calée sur le prochain lever / coucher du soleil en mode Auto.
/// </summary>
public static class OpalShade
{
    /// <summary>Un état de couleurs (cible ou affiché).</summary>
    public readonly record struct State(Color AccentStart, Color AccentEnd, Color SurfaceBase, double Light);

    private static State? _shown;
    private static State _from, _to;
    private static DateTime _started;
    private static TimeSpan _duration;
    private static DispatcherTimer? _tick;
    private static DispatcherTimer? _sunTimer;
    private static bool _watching;
    private static bool _frozenReported;

    /// <summary>Couleurs affichées en ce moment (pendant une transition : entre l'ancienne et la nouvelle).</summary>
    public static State Shown => _shown ?? Target();

    /// <summary>0 = verre sombre, 1 = opale blanche (valeurs entre les deux pendant le passage de l'un à l'autre).</summary>
    public static double Light => Shown.Light;

    /// <summary>Thème clair visé (réglage + heure + Windows) : ce que les fenêtres doivent montrer une fois la transition finie.</summary>
    public static bool IsLight => Target().Light >= 0.5;

    /// <summary>Une image de transition a été appliquée (ou une nouvelle couleur sans transition).</summary>
    public static event Action? Changed;

    /// <summary>Thème sombre / clair visé a changé (paramètres : Mica clair ou sombre, acrylique du dock…).</summary>
    public static event Action? ThemeChanged;

    /// <summary>Couleur effective d'un rôle (déjà corrigée pour la lisibilité).</summary>
    public static Color Ink(InkRole role) => ((SolidColorBrush)Paint.Themed[(int)role]).Color;

    /// <summary>Brosse figée de la couleur actuelle d'un rôle (dessins mis en cache, stylos figés…).</summary>
    public static Brush Frozen(InkRole role) => Paint.Frozen(Ink(role));

    // ------------------------------------------------------------------ cible

    /// <summary>Thème clair à cet instant d'après le réglage (Auto = soleil de la ville de la météo, Comme Windows = registre).</summary>
    public static bool LightNow()
    {
        var mode = SettingsStore.Current.Appearance.OpalTheme;
        if (mode is OpalThemeMode.Dark or OpalThemeMode.Light) return mode == OpalThemeMode.Light;
        SunCalculator.SunTimes? sun = null;
        if (mode == OpalThemeMode.Auto)
        {
            try { sun = CityLocation.SunToday(); } catch (Exception) { }
        }
        return OpalColor.IsLight(mode, DateTime.Now, sun, WindowsLight());
    }

    private static bool WindowsLight()
    {
        try { return !ThemeService.IsDarkMode(); }
        catch (Exception) { return false; }
    }

    private static State Target()
    {
        bool light = LightNow();
        return new State(AppearanceService.AccentStart, AppearanceService.AccentEnd, AppearanceService.SurfaceFor(light), light ? 1 : 0);
    }

    // ------------------------------------------------------------------ transition

    /// <summary>
    /// Réglages changés (AppearanceService.Apply) : nouvelle cible. Même cible = rien. Sinon transition douce depuis les couleurs
    /// affichées (même au milieu d'une autre transition : elle repart d'où elle en est), ou saut direct au tout premier appel,
    /// en --snapshot et quand rien n'est encore affiché.
    /// </summary>
    public static void Retarget()
    {
        Watch();
        var target = Target();
        bool themeChanged = _shown is not { } before || (before.Light >= 0.5) != (target.Light >= 0.5) || (_tick?.IsEnabled == true && (_to.Light >= 0.5) != (target.Light >= 0.5));
        if (_shown is { } shown && Same(shown, target) && _tick?.IsEnabled != true)
        {
            Publish(shown, force: false);
            return;
        }
        if (_tick?.IsEnabled == true && Same(_to, target)) return; // déjà en route vers cette cible
        bool instant = _shown == null || SettingsStore.IsReadOnly || Application.Current == null;
        if (instant)
        {
            Stop();
            Publish(target, force: true);
        }
        else
        {
            _from = Shown;
            _to = target;
            _started = DateTime.UtcNow;
            _duration = TimeSpan.FromMilliseconds(Opal.ColorTransition.TotalMilliseconds / Math.Clamp(AppearanceService.Speed, 0.5, 2));
            _tick ??= CreateTick();
            _tick.Start();
            Step();
        }
        if (themeChanged) ThemeChanged?.Invoke();
        ScheduleSun();
    }

    private static DispatcherTimer CreateTick()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Opal.ColorTick };
        timer.Tick += (_, _) => Guarded.Run(Step, "Couleurs : transition");
        return timer;
    }

    private static void Step()
    {
        double t = _duration.TotalMilliseconds <= 0 ? 1 : (DateTime.UtcNow - _started).TotalMilliseconds / _duration.TotalMilliseconds;
        if (t >= 1)
        {
            Stop();
            Publish(_to, force: true);
            return;
        }
        Publish(Between(_from, _to, OpalColor.Ease(t)), force: true);
    }

    private static void Stop() => _tick?.Stop();

    /// <summary>État entre deux autres (couleurs en OKLCH, thème en ligne droite).</summary>
    public static State Between(State a, State b, double t) => new(
        OpalColor.Mix(a.AccentStart, b.AccentStart, t), OpalColor.Mix(a.AccentEnd, b.AccentEnd, t),
        OpalColor.Mix(a.SurfaceBase, b.SurfaceBase, t), a.Light + (b.Light - a.Light) * t);

    private static bool Same(State a, State b) =>
        a.AccentStart == b.AccentStart && a.AccentEnd == b.AccentEnd && a.SurfaceBase == b.SurfaceBase && Math.Abs(a.Light - b.Light) < 1e-6;

    // ------------------------------------------------------------------ application

    private static (State State, double Opacity, double Dock)? _published;

    private static void Publish(State state, bool force)
    {
        var look = SettingsStore.Current.Appearance;
        if (!force && _published is { } p && Same(p.State, state) && p.Opacity == look.Opacity && p.Dock == look.DockOpacity)
        {
            OpalTheme.Apply();
            return;
        }
        _shown = state;
        _published = (state, look.Opacity, look.DockOpacity);

        // Brosses du thème : palette sombre → claire, puis lisibilité vérifiée sur la surface de référence.
        var reference = ReferenceSurface(state, look.Opacity);
        var colors = Palette(state.Light, reference);
        for (int i = 0; i < colors.Length; i++)
        {
            var brush = Paint.Themed[i];
            if (brush.IsFrozen)
            {
                if (!_frozenReported) Log.Info($"Couleurs : brosse du thème figée ({(InkRole)i}) — elle ne suivra plus le thème");
                _frozenReported = true;
                continue;
            }
            if (brush.Color != colors[i]) brush.Color = colors[i];
        }

        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            foreach (var role in Enum.GetValues<InkRole>()) resources[Paint.Key(role)] = Paint.Frozen(colors[(int)role]);
            // Noms historiques (5A), au cas où un code les chercherait encore
            resources["TextPrimary"] = resources[Paint.Key(InkRole.TextPrimary)];
            resources["TextSecondary"] = resources[Paint.Key(InkRole.TextSecondary)];
            resources["TextTertiary"] = resources[Paint.Key(InkRole.TextTertiary)];
            resources["TextFaint"] = resources[Paint.Key(InkRole.TextFaint)];
            resources["SurfaceBrush"] = resources[Paint.Key(InkRole.Surface)];
            resources["SurfaceHoverBrush"] = resources[Paint.Key(InkRole.SurfaceHover)];
            resources["DividerBrush"] = resources[Paint.Key(InkRole.Divider)];

            resources[AppearanceService.IslandBrushKey] = Paint.Frozen(Swatch.WithAlpha(state.SurfaceBase, (byte)Math.Round(look.Opacity * 255)));
            // Alpha 1 minimum : un pixel 100 % transparent laisserait passer les clics à travers la barre.
            resources[AppearanceService.DockBrushKey] = Paint.Frozen(Swatch.WithAlpha(state.SurfaceBase, (byte)Math.Max(1, Math.Round(look.DockOpacity * 255))));
            var mid = OpalColor.Mix(state.AccentStart, state.AccentEnd, 0.5);
            resources[AppearanceService.AccentBrushKey] = Paint.Frozen(OpalColor.Readable(mid, reference, OpalColor.IconContrast));
            resources[AppearanceService.AccentGradientKey] = Paint.Gradient(state.AccentStart, state.AccentEnd);
        }
        OpalTheme.Apply();
        Changed?.Invoke();
    }

    /// <summary>
    /// Surface sur laquelle le texte est vérifié : le fond de l'île / du dock (réglage « Opacité », au moins le voile des cartes)
    /// posé sur le pire décor possible (blanc derrière du verre sombre, noir derrière de l'opale blanche).
    /// </summary>
    public static Color ReferenceSurface(State state, double opacity)
    {
        var worst = state.Light >= 0.5 ? Colors.Black : Colors.White;
        double alpha = Math.Max(opacity, Opal.PanelSmokeMin);
        return OpalTheme.Over(OpalPalette.WithOpacity(state.SurfaceBase, alpha), worst);
    }

    /// <summary>
    /// Palette du thème à <paramref name="light"/> (0 sombre → 1 clair), puis textes et icônes rendus lisibles sur
    /// <paramref name="reference"/> : texte principal et secondaire ≥ 4,5, texte tertiaire (légendes, icônes discrètes) ≥ 3.
    /// </summary>
    public static Color[] Palette(double light, Color reference)
    {
        var colors = new Color[InkPalette.Dark.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = light <= 0 ? InkPalette.Dark[i] : light >= 1 ? InkPalette.Light[i] : OpalColor.Mix(InkPalette.Dark[i], InkPalette.Light[i], light);
        // Pendant la transition (couleurs entre les deux), on ne corrige pas : le texte passe par le gris, c'est voulu et court.
        if (light <= 0 || light >= 1)
        {
            colors[(int)InkRole.TextPrimary] = OpalColor.Readable(colors[(int)InkRole.TextPrimary], reference, OpalColor.TextContrast);
            colors[(int)InkRole.TextSecondary] = OpalColor.Readable(colors[(int)InkRole.TextSecondary], reference, OpalColor.TextContrast);
            colors[(int)InkRole.TextMuted] = OpalColor.Readable(colors[(int)InkRole.TextMuted], reference, OpalColor.TextContrast);
            colors[(int)InkRole.TextTertiary] = OpalColor.Readable(colors[(int)InkRole.TextTertiary], reference, OpalColor.IconContrast);
        }
        return colors;
    }

    // ------------------------------------------------------------------ thème Auto / Comme Windows

    /// <summary>Événements de Windows (pas de minuterie) : thème des applis changé, heure changée, sortie de veille.</summary>
    private static void Watch()
    {
        if (_watching || SettingsStore.IsReadOnly) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            // « ImmersiveColorSet » (thème des applis) arrive en catégorie General.
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
                && SettingsStore.Current.Appearance.OpalTheme == OpalThemeMode.Windows)
                Guarded.OnUi(Retarget, "Couleurs : thème de Windows");
        };
        SystemEvents.TimeChanged += (_, _) => Guarded.OnUi(Retarget, "Couleurs : heure changée");
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Guarded.OnUi(Retarget, "Couleurs : sortie de veille");
        };
    }

    /// <summary>Mode Auto : UNE minuterie calée sur le prochain lever / coucher du soleil (ou minuit), rien d'autre.</summary>
    private static void ScheduleSun()
    {
        if (SettingsStore.IsReadOnly || Application.Current == null) return;
        if (SettingsStore.Current.Appearance.OpalTheme != OpalThemeMode.Auto)
        {
            _sunTimer?.Stop();
            return;
        }
        SunCalculator.SunTimes? sun = null;
        try { sun = CityLocation.SunToday(); } catch (Exception) { }
        var now = DateTime.Now;
        var next = sun == null ? now.AddMinutes(30) : OpalColor.NextSwitch(now, sun); // coordonnées pas encore trouvées : on réessaie
        var wait = next - now + TimeSpan.FromSeconds(2);
        if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(5);
        if (_sunTimer == null)
        {
            _sunTimer = new DispatcherTimer();
            _sunTimer.Tick += (_, _) =>
            {
                _sunTimer.Stop();
                Guarded.Run(Retarget, "Couleurs : lever / coucher du soleil");
            };
        }
        _sunTimer.Stop();
        _sunTimer.Interval = wait;
        _sunTimer.Start();
    }

    /// <summary>--self-test : prochaine bascule prévue (null = pas de minuterie).</summary>
    internal static TimeSpan? SunTimerInterval => _sunTimer?.IsEnabled == true ? _sunTimer.Interval : null;
}
