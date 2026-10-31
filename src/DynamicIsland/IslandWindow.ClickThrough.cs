using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

/// <summary>
/// Correctif urgent 7 : l'île ne bloque les clics QUE sur sa forme visible. Le halo et l'ombre sont dessinés dans la fenêtre
/// (pixels presque transparents mais pas tout à fait) : Windows les comptait comme l'île, et les onglets du navigateur juste
/// en dessous ne recevaient pas les clics. Dès que la souris passe sur ces pixels, la fenêtre devient « transparente aux clics »
/// (WS_EX_TRANSPARENT : tout passe à l'appli en dessous) ; une petite minuterie, qui ne tourne QUE pendant ce temps, rend la
/// main quand la souris revient sur l'île ou s'en va. HTTRANSPARENT (WM_NCHITTEST) ne suffirait pas : il ne fait passer les
/// clics qu'aux fenêtres du même programme.
/// </summary>
public partial class IslandWindow
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private const double ShapeMargin = 3; // DIP autour de la forme qui comptent encore comme l'île
    private const double EdgeRow = 2; // DIP collés au bord de l'écran : la bande du bord ouvre toujours l'île
    private readonly DispatcherTimer _passWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _passThrough;

    private void InitializeClickThrough()
    {
        PreviewMouseMove += (_, e) =>
        {
            if (!_passThrough && CanPassThrough() && !InShape(e.GetPosition(this))) SetPassThrough(true);
        };
        _passWatch.Tick += (_, _) => CheckPassThrough();
    }

    /// <summary>Jamais pendant un déplacement de l'île, un glisser de fichier, une capture de la souris ou un rendu.</summary>
    private bool CanPassThrough() => _hwnd != IntPtr.Zero && SnapshotFolder == null && !_moving && !_draggingOut && Mouse.Captured == null;

    /// <summary>Point (DIP de la fenêtre) sur la forme visible de l'île, ou sur la rangée du bord de l'écran.</summary>
    private bool InShape(Point point)
    {
        if (Island.ActualWidth <= 0 || !Island.IsVisible) return false;
        var shape = Island.TransformToAncestor(this).TransformBounds(new Rect(Island.RenderSize));
        shape.Inflate(ShapeMargin, ShapeMargin);
        if (shape.Contains(point)) return true;
        if (!EdgeBand.IsVisible || EdgeBand.ActualWidth <= 0) return false;
        var band = EdgeBand.TransformToAncestor(this).TransformBounds(new Rect(EdgeBand.RenderSize));
        bool atTop = EdgeBand.VerticalAlignment != VerticalAlignment.Bottom;
        return point.X >= band.Left && point.X <= band.Right
               && (atTop ? point.Y >= band.Top && point.Y - band.Top < EdgeRow : point.Y <= band.Bottom && band.Bottom - point.Y < EdgeRow);
    }

    private void CheckPassThrough()
    {
        if (!_passThrough) return;
        if (!GetCursorPos(out var cursor) || !CanPassThrough())
        {
            SetPassThrough(false);
            return;
        }
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        bool insideWindow = point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        // Revenue sur l'île : elle reprend la souris (survol, clic). Partie de la fenêtre : on remet comme avant.
        if (!insideWindow || InShape(point)) SetPassThrough(false);
    }

    private void SetPassThrough(bool on)
    {
        if (_passThrough == on || _hwnd == IntPtr.Zero) return;
        _passThrough = on;
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, on ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT);
        if (on) _passWatch.Start();
        else _passWatch.Stop();
    }
}
