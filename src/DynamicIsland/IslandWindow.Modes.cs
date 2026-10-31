using System.Windows;
using System.Windows.Input;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

/// <summary>
/// Priorité 2, points 13 et 14 : quand l'île se cache. Modes Fixe / Intelligent (une appli juste en dessous, ex. navigateur
/// agrandi) / Peek (cachée, la souris au bord de l'écran la fait venir), règles par application, vidéo plein écran (même si une
/// autre fenêtre a le focus), raccourci « Cacher / afficher l'île ». Tout passe par <see cref="CheckFullscreen"/> (minuterie
/// rapide seulement quand un mode ou une règle le demande).
/// </summary>
public partial class IslandWindow
{
    private enum HideReason { None, Fullscreen, Mode, Rule, User }

    private HideReason _hideReason;
    private bool _userHidden;
    private DateTime _revealUntil, _lastRevealed;
    private static readonly TimeSpan RevealGrace = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan HotkeyReveal = TimeSpan.FromSeconds(6);

    /// <summary>Raccourci « Cacher / afficher l'île » : cachée toute seule → montrée un moment ; visible → cachée jusqu'au prochain appui.</summary>
    public void ToggleByUser()
    {
        if (_userHidden) _userHidden = false;
        else if (_hiddenForFullscreen) _revealUntil = DateTime.UtcNow + HotkeyReveal;
        else
        {
            _userHidden = true;
            Collapse();
        }
        CheckFullscreen();
    }

    /// <summary>Une annonce arrive alors que l'île est cachée par son mode ou une règle : elle se montre le temps de l'annonce.</summary>
    private void RevealForNotice(TimeSpan? duration)
    {
        if (!_hiddenForFullscreen || _hideReason is not (HideReason.Mode or HideReason.Rule)) return;
        _revealUntil = DateTime.UtcNow + (duration ?? TimeSpan.FromSeconds(4)) + TimeSpan.FromSeconds(1);
        CheckFullscreen();
    }

    /// <summary>Pourquoi l'île doit être cachée maintenant (None = visible).</summary>
    private HideReason HideDecision()
    {
        var settings = SettingsStore.Current.Island;
        if (_userHidden) return HideReason.User;
        var now = DateTime.UtcNow;
        bool forced = now < _revealUntil;
        bool revealed = forced || Revealed(now);

        // La fenêtre juste sous l'île (pas seulement celle qui a le focus) : vidéo plein écran, navigateur agrandi.
        IntPtr under = IntPtr.Zero;
        bool underFullscreen = false;
        if ((settings.Mode == IslandMode.Smart || settings.HideInFullscreen || AppRuleService.HasRules) && PillCenterOnScreen() is { } center)
            under = AppRuleService.WindowUnder(_hwnd, center.X, center.Y, out underFullscreen);
        var rule = AppRuleService.Combine(AppRuleService.ForForeground(dock: false), AppRuleService.ForWindow(under, dock: false));
        if (rule == AppBehavior.Show) return HideReason.None;

        if (settings.HideInFullscreen && !_gaming && !forced
            && (ScreenInfo.IsForegroundFullscreen(_monitor) || underFullscreen && Interop.NativeMethods.MonitorFromWindow(under, 2) == _monitor))
            return HideReason.Fullscreen;
        if (revealed) return HideReason.None;
        if (rule == AppBehavior.Hide) return HideReason.Rule;
        return settings.Mode switch
        {
            IslandMode.Smart when under != IntPtr.Zero => HideReason.Mode,
            IslandMode.Peek => HideReason.Mode,
            _ => HideReason.None,
        };
    }

    /// <summary>
    /// L'utilisateur se sert de l'île : ouverte, déplacée, menu, souris dessus (si elle est visible) ou au bord de l'écran juste
    /// au-dessus d'elle. Reste vrai un court instant après (pas de clignotement).
    /// </summary>
    private bool Revealed(DateTime now)
    {
        bool active = _expanded || _moving || _draggingOut || Mouse.Captured != null || HoverRoot.ContextMenu?.IsOpen == true
                      || (!_hiddenForFullscreen && IsCursorOverIsland()) || CursorAtEdge();
        if (active) _lastRevealed = now;
        return now - _lastRevealed < RevealGrace;
    }

    /// <summary>Souris collée au bord de l'écran (haut, ou bas si l'île y est), dans la largeur de l'île.</summary>
    private bool CursorAtEdge()
    {
        if (!GetCursorPos(out var cursor) || ScreenInfo.Describe(_monitor) is not { } screen || PresentationSource.FromVisual(HoverRoot) == null) return false;
        var left = HoverRoot.PointToScreen(new Point(0, 0)).X;
        var right = HoverRoot.PointToScreen(new Point(HoverRoot.ActualWidth, 0)).X;
        double margin = 40 * screen.Scale;
        if (cursor.X < left - margin || cursor.X > right + margin) return false;
        bool bottom = SettingsStore.Current.Island.Placement.Vertical == VerticalAnchor.Bottom;
        return bottom ? cursor.Y >= screen.Bounds.Bottom - 2 && cursor.Y <= screen.Bounds.Bottom : cursor.Y >= screen.Bounds.Top && cursor.Y <= screen.Bounds.Top + 2;
    }

    /// <summary>Centre de la pilule sur l'écran (pixels), même île cachée.</summary>
    private POINT? PillCenterOnScreen()
    {
        if (PresentationSource.FromVisual(Island) == null || Island.ActualWidth <= 0) return null;
        var center = Island.PointToScreen(new Point(Island.ActualWidth / 2, Island.ActualHeight / 2));
        return new POINT { X = (int)center.X, Y = (int)center.Y };
    }

    /// <summary>Minuterie rapide (bord de l'écran, appli en dessous) seulement si un mode, une règle ou une révélation l'exige.</summary>
    private void AdjustModeWatch()
    {
        bool fast = SettingsStore.Current.Island.Mode != IslandMode.Fixed || AppRuleService.HasRules || DateTime.UtcNow < _revealUntil;
        var interval = TimeSpan.FromMilliseconds(fast ? 200 : 1000);
        if (_fullscreenWatch.Interval != interval) _fullscreenWatch.Interval = interval;
    }
}
