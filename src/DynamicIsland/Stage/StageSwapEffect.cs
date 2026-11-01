using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>
/// Échange animé de Stage Manager (7G), façon Génie : la miniature de la fenêtre choisie grandit de la colonne jusqu'à sa place au
/// centre pendant que celle de la fenêtre qui part rétrécit vers la colonne. Deux miniatures DWM sur une fenêtre hôte sans surface
/// (WS_EX_NOREDIRECTIONBITMAP : transparente, rien d'autre n'y est dessiné), comme <see cref="Dock.GenieEffect"/>. DWM garde
/// l'image d'une fenêtre réduite : on voit sa dernière image. Rien n'est injecté dans les autres applis.
/// </summary>
internal sealed class StageSwapEffect
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly HwndSource _host;
    private readonly RECT _box;
    private readonly List<(IntPtr Thumbnail, RECT From, RECT To, bool Leaving)> _moves = [];
    private readonly Action _done;
    private TimeSpan _start = TimeSpan.Zero;
    private bool _finished;

    private StageSwapEffect(RECT box, Action done)
    {
        _box = box;
        _done = done;
        _host = new HwndSource(new HwndSourceParameters("Dynamic Island — Stage Manager (échange)")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP,
            PositionX = box.Left,
            PositionY = box.Top,
            Width = box.Right - box.Left,
            Height = box.Bottom - box.Top,
        });
    }

    /// <summary>
    /// Lance l'échange sur l'écran <paramref name="screen"/> (pixels) : <paramref name="incoming"/> de <paramref name="slot"/> vers
    /// <paramref name="target"/>, <paramref name="leaving"/> (peut être nul) de sa place vers <paramref name="column"/>.
    /// <paramref name="done"/> est TOUJOURS appelé (tout de suite si l'effet ne peut pas se faire).
    /// </summary>
    public static void Run(IntPtr incoming, RECT slot, RECT target, IntPtr leaving, RECT leavingFrom, RECT column, Action done)
    {
        StageSwapEffect? effect = null;
        try
        {
            // L'hôte couvre seulement le trajet (il prend les clics pendant moins d'une demi-seconde).
            var screen = Union(slot, target);
            if (leaving != IntPtr.Zero) screen = Union(screen, Union(leavingFrom, column));
            effect = new StageSwapEffect(screen, done);
            if (!effect.Add(incoming, slot, target, false))
            {
                effect.Finish();
                return;
            }
            if (leaving != IntPtr.Zero) effect.Add(leaving, leavingFrom, column, true);
            SetWindowPos(effect._host.Handle, HWND_TOPMOST, screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
            effect.Layout(0);
            CompositionTarget.Rendering += effect.OnRendering;
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : échange animé", ex);
            if (effect != null) effect.Finish();
            else done();
        }
    }

    private bool Add(IntPtr window, RECT from, RECT to, bool leaving)
    {
        if (DwmRegisterThumbnail(_host.Handle, window, out var thumbnail) != 0) return false;
        _moves.Add((thumbnail, from, to, leaving));
        return true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_finished) return;
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_start == TimeSpan.Zero) _start = now;
        double t = Math.Min(1, (now - _start).TotalSeconds * AppearanceService.Speed / Motion.StageSwap.TotalSeconds);
        Layout(t);
        if (t >= 1) Finish();
    }

    private void Layout(double t)
    {
        foreach (var (thumbnail, from, to, leaving) in _moves)
        {
            // Courbe douce ; la fenêtre qui part s'estompe un peu sur la fin (elle « rentre » dans la colonne).
            double k = Ease(t);
            byte opacity = leaving ? (byte)Math.Round(255 * (1 - 0.35 * Math.Clamp((t - 0.5) / 0.5, 0, 1))) : (byte)255;
            var rect = new RECT
            {
                Left = Lerp(from.Left, to.Left, k) - _box.Left,
                Top = Lerp(from.Top, to.Top, k) - _box.Top,
                Right = Lerp(from.Right, to.Right, k) - _box.Left,
                Bottom = Lerp(from.Bottom, to.Bottom, k) - _box.Top,
            };
            var properties = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = rect,
                opacity = opacity,
                fVisible = rect.Right - rect.Left >= 1 && rect.Bottom - rect.Top >= 1 ? 1 : 0,
                fSourceClientAreaOnly = 0,
            };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private static RECT Union(RECT a, RECT b) => new()
    {
        Left = Math.Min(a.Left, b.Left) - 2,
        Top = Math.Min(a.Top, b.Top) - 2,
        Right = Math.Max(a.Right, b.Right) + 2,
        Bottom = Math.Max(a.Bottom, b.Bottom) + 2,
    };

    private static int Lerp(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);

    /// <summary>Départ rapide, arrivée douce (cubique).</summary>
    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        CompositionTarget.Rendering -= OnRendering;
        try
        {
            _done();
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : fin de l'échange", ex);
        }
        // La fenêtre rendue a le temps d'apparaître à sa place avant que la miniature disparaisse (pas de trou d'une image).
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var (thumbnail, _, _, _) in _moves) DwmUnregisterThumbnail(thumbnail);
            _moves.Clear();
            _host.Dispose();
        };
        timer.Start();
    }
}
