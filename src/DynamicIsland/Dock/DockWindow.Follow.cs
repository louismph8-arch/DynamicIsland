using System.Windows;
using System.Windows.Media.Animation;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Dock : option « suivre l'écran de la souris » (comme l'île). La souris qui reste sur un autre écran y amène le dock, au
/// même bord ; l'écran des réglages (<see cref="DockSettings.Screen"/>) n'est jamais modifié. En mode Fixe, l'espace
/// réservé est libéré sur l'ancien écran puis réservé sur le nouveau (ApplySettings, comme un glisser vers un autre écran).
/// </summary>
public partial class DockWindow
{
    /// <summary>Écran suivi (nom pour Windows, null = le principal).</summary>
    private sealed record FollowedScreen(string? Screen);

    /// <summary>Vérification toutes les 5 passes de la surveillance (80 ms) = 400 ms, comme l'île.</summary>
    private const int FollowTicks = 5;
    private readonly DockFollow _followDecision = new();
    /// <summary>Écran des réglages quand le dock a commencé à suivre : s'il change (paramètres), on arrête de suivre.</summary>
    private string? _followHome;

    private void FollowMouse()
    {
        if (!IsVisible || SnapshotFolder != null || _appBar == null || !_settings.Enabled || !GetCursorPos(out var cursor)) return;
        bool busy = _live != null || _movePress != null || _menuOpen || _dragView != null
                    || _folderWindow?.IsVisible == true || _preview?.IsVisible == true || _downloadsWindow?.IsVisible == true
                    || IsCursorOverDock(cursor);
        var target = _followDecision.Next(cursor, ScreenInfo.All(), _screen.Device,
            monitor => GamingMode.IsOnMonitor(monitor.Handle), busy, DateTime.UtcNow);
        if (target is { } screen) FollowTo(ScreenInfo.SettingName(screen));
    }

    /// <summary>Le dock passe sur cet écran (même bord) : nouvelle réservation, puis il remonte du bord avec son ressort.</summary>
    private void FollowTo(string? screen)
    {
        if (_follow == null) _followHome = _settings.Screen;
        _follow = screen == _settings.Screen ? null : new FollowedScreen(screen); // revenu sur l'écran des réglages
        _previewDelay.Stop();
        HidePreview();
        HideLabel();
        _pointerX = null;
        SetHovered(null);
        ApplySettings(); // Release sur l'ancien écran (autre écran dans la clé), ReserveSpace + PositionWindow sur le nouveau
        Appear();
    }

    /// <summary>Apparition sur le nouvel écran : part de sa place cachée (sous le bord) et revient en ressort, en fondu.</summary>
    private void Appear()
    {
        if (_hidden) return; // caché (Peek, plein écran…) : il apparaîtra normalement
        _shift.SetTarget(HiddenOffset, Springs.DockShow);
        _shift.Snap();
        _shift.SetTarget(0, Springs.DockShow);
        // FillBehavior.Stop : à la fin, l'opacité revient à sa valeur locale (1), que le glisser du dock peut encore changer.
        DockRoot.Opacity = 1;
        DockRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
        StartAnimation();
    }

    /// <summary>Écran choisi à la main (menu, paramètres, glisser) : le dock ne garde plus l'écran suivi.</summary>
    private void StopFollowing()
    {
        _follow = null;
        _followDecision.Reset();
    }

    /// <summary>Réglages changés : option coupée, ou écran des réglages changé ailleurs → retour à l'écran des réglages.</summary>
    private void CheckFollowSettings()
    {
        if (_follow == null) return;
        if (_settings.FollowMouse && _settings.Screen == _followHome) return;
        StopFollowing(); // la suite d'OnSettingsChanged (ApplySettings si la clé a changé) remet le dock à sa place
    }
}
