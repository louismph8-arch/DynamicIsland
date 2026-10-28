using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>Dock : modes Fixe / Intelligent / Peek et masquage en plein écran.</summary>
public partial class DockWindow
{
    private double HiddenOffset => DockVisualHeight + DockShape.HiddenExtra; // assez pour cacher aussi le halo
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _hidden;
    private bool _fullscreen;
    private bool _overlapped;
    private int _watchTicks;
    private DateTime _lastInside = DateTime.MinValue;
    /// <summary>Raccourci « Afficher le dock » : visible jusqu'à cette heure (prolongée tant que la souris est dessus).</summary>
    private DateTime _revealUntil = DateTime.MinValue;
    private static readonly TimeSpan RevealTime = TimeSpan.FromSeconds(6);
    /// <summary>7A : caché pour un jeu ou un plein écran sur son écran → plus du tout dessiné par-dessus (après l'animation).</summary>
    private bool _cloaked;
    private DateTime _cloakAt = DateTime.MaxValue;
    private static readonly TimeSpan CloakDelay = TimeSpan.FromMilliseconds(700);

    private void InitializeVisibility()
    {
        _watch.Tick += (_, _) => OnWatchTick();
        Loaded += async (_, _) =>
        {
            if (SnapshotFolder == null) _watch.Start();
            else if (!GlassBenchOnly) await RunSnapshotAsync(SnapshotFolder);
        };
        Closed += (_, _) => _watch.Stop();
    }

    /// <summary>Surveillance légère (curseur, plein écran, fenêtres qui touchent le dock).</summary>
    private void OnWatchTick()
    {
        _watchTicks++;
        if (_watchTicks % 6 == 0)
        {
            _fullscreen = ScreenInfo.IsForegroundFullscreen(_screen.Handle); // plein écran sur l'écran du dock seulement
            TaskbarService.EnforceHidden();
        }
        if (_settings.Mode == DockMode.Smart && _watchTicks % 3 == 0) _overlapped = IsOverlapped();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (!GetCursorPos(out var cursor)) return;

        bool inside = IsCursorOverDock(cursor) || _menuOpen || _dragView != null || (_preview?.ContainsCursor(cursor) ?? false)
                      || _folderWindow?.IsVisible == true;
        if (inside) _lastInside = DateTime.UtcNow;
        bool stayOpen = !_hidden && DateTime.UtcNow - _lastInside < HideDelay;
        bool atEdge = IsCursorAtEdge(cursor);
        bool revealed = DateTime.UtcNow < _revealUntil || _live != null; // raccourci « Afficher le dock », ou on le déplace
        if (revealed && inside && _revealUntil < DateTime.UtcNow + HideDelay) _revealUntil = DateTime.UtcNow + HideDelay;

        // Plein écran (vidéo) ou jeu sur l'écran du dock : il reste caché, même souris au bord de l'écran, sauf s'il a été
        // appelé par le raccourci « Afficher le dock ». Sur un AUTRE écran que le jeu, il reste utilisable.
        bool covered = _fullscreen || GamingMode.IsOnMonitor(_screen.Handle);
        bool visible = revealed || (covered ? false : _settings.Mode switch
        {
            DockMode.Fixed => true,
            DockMode.Smart => !_overlapped || atEdge || stayOpen,
            _ => atEdge || stayOpen, // Peek
        });
        UpdateCloak(!visible && covered);
        SetHidden(!visible);
    }

    /// <summary>Rien par-dessus un jeu ou une vidéo plein écran : la fenêtre (transparente) du dock est masquée par DWM.</summary>
    private void UpdateCloak(bool wanted)
    {
        if (SnapshotFolder != null) return;
        if (!wanted)
        {
            _cloakAt = DateTime.MaxValue;
            if (_cloaked) SetCloaked(_hwnd, _cloaked = false); // avant l'animation d'apparition
            return;
        }
        if (_cloaked) return;
        if (_cloakAt == DateTime.MaxValue) _cloakAt = DateTime.UtcNow + CloakDelay; // le temps qu'il glisse hors de l'écran
        else if (DateTime.UtcNow >= _cloakAt) SetCloaked(_hwnd, _cloaked = true);
    }

    /// <summary>
    /// Raccourci « Afficher le dock » (Ctrl+Alt+D par défaut) : le dock apparaît par-dessus tout, même en jeu ou en
    /// plein écran, pour 6 s et tant que la souris est dessus ; une 2e pression le recache.
    /// </summary>
    public void ToggleReveal()
    {
        if (!IsVisible) return; // dock désactivé
        bool revealed = DateTime.UtcNow < _revealUntil;
        _revealUntil = revealed ? DateTime.MinValue : DateTime.UtcNow + RevealTime;
        if (!revealed && _hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); // devant le jeu
        UpdateVisibility();
    }

    private void OnGamingChanged()
    {
        if (GamingMode.IsActive)
        {
            _folderWindow?.Hide();
            HidePreview();
        }
        UpdateVisibility();
    }

    private void SetHidden(bool hidden)
    {
        if (hidden == _hidden) return;
        _hidden = hidden;
        DockRoot.IsHitTestVisible = !hidden;
        _shift.SetTarget(hidden ? HiddenOffset : 0, hidden ? Springs.DockHide : Springs.DockShow);
        if (hidden)
        {
            _pointerX = null;
            SetHovered(null);
            HidePreview();
        }
        UpdateMagnification();
        StartAnimation();
    }

    private bool IsCursorOverDock(POINT cursor)
    {
        if (_hidden || !DockRoot.IsLoaded) return false;
        var topLeft = DockRoot.PointToScreen(new Point(0, 0));
        var bottomRight = DockRoot.PointToScreen(new Point(DockRoot.ActualWidth, DockRoot.ActualHeight));
        const double Margin = 6;
        double gap = DockShape.FloatingGap * _scale; // l'espace entre le dock flottant et le bord compte aussi
        return cursor.X >= topLeft.X - Margin && cursor.X <= bottomRight.X + Margin
            && cursor.Y >= topLeft.Y - Margin - (AtTop ? gap : 0) && cursor.Y <= bottomRight.Y + Margin + (AtTop ? 0 : gap);
    }

    /// <summary>Souris collée au bord du dock sur son écran (ou sur la barre des tâches si elle est visible).</summary>
    private bool IsCursorAtEdge(POINT cursor)
    {
        var bounds = _screen.Bounds;
        if (cursor.X < bounds.Left || cursor.X >= bounds.Right) return false;
        return AtTop ? cursor.Y >= bounds.Top && cursor.Y <= _edgePx + 2 : cursor.Y >= _edgePx - 2 && cursor.Y <= bounds.Bottom;
    }

    /// <summary>Mode Intelligent : une fenêtre (non réduite) touche-t-elle la zone du dock ?</summary>
    private bool IsOverlapped()
    {
        if (!DockBackground.IsLoaded) return false;
        // Zone du dock quand il est affiché (même s'il est caché en ce moment).
        int left = (int)DockBackground.PointToScreen(new Point(0, 0)).X;
        int right = (int)DockBackground.PointToScreen(new Point(DockBackground.ActualWidth, 0)).X;
        int height = (int)(DockVisualHeight * _scale);
        int top = AtTop ? _edgePx : _edgePx - height;
        int bottom = top + height;

        foreach (var window in _tracker.Windows)
        {
            if (window.IsMinimized || IsIconic(window.Handle)) continue;
            var r = GetVisibleBounds(window.Handle);
            if (r.Left < right && r.Right > left && r.Top < bottom && r.Bottom > top) return true;
        }
        return false;
    }
}
