using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Services;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Effet « Génie » de macOS quand une fenêtre est réduite : elle s'écoule dans son icône du dock.
/// Windows ne donne pas les pixels des autres fenêtres : on découpe une miniature DWM de la fenêtre en
/// bandes horizontales (une miniature par bande, chacune avec sa zone source), puis chaque bande est
/// rétrécie et déplacée vers l'icône. L'hôte est une fenêtre sans surface (WS_EX_NOREDIRECTIONBITMAP) :
/// seules les miniatures s'y dessinent, le reste est transparent. Rien n'est injecté dans les autres applis.
/// </summary>
internal sealed class GenieEffect
{
    private const int Strips = 36;
    private const double DurationSeconds = 0.5;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int DWM_TNP_RECTSOURCE = 0x2;
    private const int WPF_RESTORETOMAXIMIZED = 0x2;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly IntPtr _window;
    private readonly HwndSource _host;
    private readonly List<IntPtr> _thumbnails = [];
    private readonly RECT _box;
    private RECT _from;
    private RECT _to;
    private SIZE _source;
    private TimeSpan _start = TimeSpan.Zero;
    private bool _finished;

    private GenieEffect(IntPtr window, RECT from, RECT to)
    {
        _window = window;
        _from = from;
        _to = to;
        // L'hôte couvre tout le trajet : de la fenêtre jusqu'à l'icône.
        _box = new RECT
        {
            Left = Math.Min(from.Left, to.Left) - 2,
            Top = Math.Min(from.Top, to.Top) - 2,
            Right = Math.Max(from.Right, to.Right) + 2,
            Bottom = Math.Max(from.Bottom, to.Bottom) + 2,
        };

        var parameters = new HwndSourceParameters("Dynamic Island — Génie")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP,
            PositionX = _box.Left,
            PositionY = _box.Top,
            Width = _box.Right - _box.Left,
            Height = _box.Bottom - _box.Top,
        };
        _host = new HwndSource(parameters);
    }

    /// <summary>Lance l'effet vers <paramref name="icon"/> (pixels écran). Ne fait rien si la fenêtre ne s'y prête pas.</summary>
    public static void TryStart(IntPtr window, RECT icon)
    {
        GenieEffect? effect = null;
        try
        {
            if (!TryGetRestoredBounds(window, out var from, out bool maximized)) return;
            effect = new GenieEffect(window, from, icon);
            if (!effect.RegisterThumbnails(maximized))
            {
                effect.Finish();
                return;
            }
            SetWindowPos(effect._host.Handle, HWND_TOPMOST, effect._box.Left, effect._box.Top,
                effect._box.Right - effect._box.Left, effect._box.Bottom - effect._box.Top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            effect.Layout(0); // la fenêtre réapparaît tout de suite à sa place, puis s'écoule
            CompositionTarget.Rendering += effect.OnRendering;
        }
        catch (Exception ex)
        {
            Log.Error("Effet Génie", ex);
            effect?.Finish();
        }
    }

    /// <summary>
    /// Position de la fenêtre avant la réduction (la vraie fenêtre est déjà réduite quand on est prévenus).
    /// Une fenêtre agrandie occupe la zone de travail de son écran.
    /// </summary>
    private static bool TryGetRestoredBounds(IntPtr window, out RECT bounds, out bool maximized)
    {
        bounds = default;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(window, ref placement)) return maximized = false;
        maximized = (placement.flags & WPF_RESTORETOMAXIMIZED) != 0;
        if (maximized)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref info)) return false;
            bounds = info.rcWork;
        }
        else
        {
            bounds = placement.rcNormalPosition;
        }
        return bounds.Right - bounds.Left > 40 && bounds.Bottom - bounds.Top > 40;
    }

    private bool RegisterThumbnails(bool maximized)
    {
        for (int i = 0; i < Strips; i++)
        {
            if (DwmRegisterThumbnail(_host.Handle, _window, out var thumbnail) != 0) return false;
            _thumbnails.Add(thumbnail);
        }
        if (DwmQueryThumbnailSourceSize(_thumbnails[0], out _source) != 0 || _source.cx < 40 || _source.cy < 40) return false;

        if (maximized)
        {
            // Agrandie : la fenêtre déborde de la zone de travail de ses bordures invisibles.
            int dx = (_source.cx - (_from.Right - _from.Left)) / 2, dy = (_source.cy - (_from.Bottom - _from.Top)) / 2;
            _from = new RECT { Left = _from.Left - dx, Top = _from.Top - dy, Right = _from.Left - dx + _source.cx, Bottom = _from.Top - dy + _source.cy };
        }
        return true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_finished) return;
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_start == TimeSpan.Zero) _start = now;
        double t = Math.Min(1, (now - _start).TotalSeconds * AppearanceService.Speed / DurationSeconds);

        // Rouverte pendant l'effet (ou fermée) : on s'arrête net.
        if (!IsWindow(_window) || !IsIconic(_window))
        {
            Finish();
            return;
        }
        Layout(t);
        if (t >= 1) Finish();
    }

    /// <summary>
    /// Position des bandes à l'instant <paramref name="t"/> (0 → 1). Les côtés se resserrent vers l'icône
    /// (courbe en S entre le haut de la fenêtre et l'icône), pendant que le contenu glisse vers le bas et
    /// s'empile dans l'icône.
    /// </summary>
    private void Layout(double t)
    {
        double curve = Ease(Math.Clamp(t / 0.45, 0, 1));
        double slide = Ease(Math.Clamp((t - 0.15) / 0.85, 0, 1));

        double left = _from.Left - _box.Left, top = _from.Top - _box.Top;
        double right = _from.Right - _box.Left, height = _from.Bottom - _from.Top;
        double iconLeft = _to.Left - _box.Left, iconTop = _to.Top - _box.Top;
        double iconRight = _to.Right - _box.Left, iconBottom = _to.Bottom - _box.Top;
        double travel = iconBottom - top;
        double bend = Math.Max(1, iconTop - top);
        byte opacity = (byte)Math.Round(255 * Math.Clamp((1 - t) / 0.15, 0, 1));

        for (int i = 0; i < _thumbnails.Count; i++)
        {
            double v0 = (double)i / Strips, v1 = (double)(i + 1) / Strips;
            double y0 = Math.Round(Math.Min(top + v0 * height + slide * travel, iconBottom));
            double y1 = Math.Round(Math.Min(top + v1 * height + slide * travel, iconBottom));

            double u = Math.Clamp(((y0 + y1) / 2 - top) / bend, 0, 1);
            double pinch = u * u * (3 - 2 * u) * curve;
            double x0 = left + (iconLeft - left) * pinch;
            double x1 = right + (iconRight - right) * pinch;

            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT { Left = (int)Math.Round(x0), Top = (int)y0, Right = (int)Math.Round(x1), Bottom = (int)y1 },
                rcSource = new RECT
                {
                    Left = 0,
                    Top = (int)Math.Round(v0 * _source.cy),
                    Right = _source.cx,
                    Bottom = (int)Math.Round(v1 * _source.cy),
                },
                opacity = opacity,
                fVisible = y1 - y0 >= 1 && x1 - x0 >= 1 ? 1 : 0,
                fSourceClientAreaOnly = 0,
            };
            DwmUpdateThumbnailProperties(_thumbnails[i], ref properties);
        }
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        CompositionTarget.Rendering -= OnRendering;
        foreach (var thumbnail in _thumbnails) DwmUnregisterThumbnail(thumbnail);
        _thumbnails.Clear();
        _host.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
}
