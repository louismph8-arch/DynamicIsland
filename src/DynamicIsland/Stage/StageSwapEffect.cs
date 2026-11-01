using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Ce qu'il faut pour un échange animé (pixels écran).</summary>
/// <param name="From">Image de la fenêtre choisie dans la colonne (au format de la fenêtre, comme la colonne la dessine).</param>
/// <param name="To">Place FINALE de la fenêtre choisie (fenêtre entière : agrandie = zone de travail + bord invisible).</param>
/// <param name="LeavingFrom">Fenêtre qui part : sa place actuelle (fenêtre entière, GetWindowRect).</param>
/// <param name="LeavingTo">Là où elle file (sa miniature dans la colonne, ou le bord de l'écran si la colonne se cache).</param>
/// <param name="ColumnVisible">La colonne reste affichée : la fenêtre qui part y reste « en retrait » au lieu de disparaître.</param>
internal readonly record struct StageSwapRequest(
    IntPtr Incoming, RECT From, RECT To, IntPtr Leaving, RECT LeavingFrom, RECT LeavingTo, bool ColumnVisible, double Scale, Insets Borders);

/// <summary>
/// Échange animé de Stage Manager (7G), façon macOS : la miniature de la fenêtre choisie grandit et glisse de la colonne jusqu'à sa
/// place finale exacte, pendant que celle de la fenêtre qui part rétrécit vers la colonne en s'estompant un peu. Deux miniatures DWM
/// sur une fenêtre hôte sans surface (WS_EX_NOREDIRECTIONBITMAP : transparente), comme <see cref="Dock.GenieEffect"/>, plus une
/// ombre légère (fenêtre WPF transparente aux clics juste dessous). DWM garde l'image d'une fenêtre réduite.
/// Phases : animation (ressort amorti, ~360 ms) → la vraie fenêtre est rendue SOUS l'image (rappel <c>reveal</c>), l'image se cale
/// sur sa vraie place → fondu de ~100 ms → tout disparaît (rappel <c>done</c>, toujours appelé une fois). Rien n'est injecté dans
/// les autres applis, rien ne prend le focus, les clics traversent.
/// </summary>
internal sealed class StageSwapEffect
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const uint DWM_TNP_RECTSOURCE = 0x00000002;

    private enum Phase { Animate, Reveal, Fade, Closed }

    private static StageSwapEffect? _current;

    private readonly StageSwapRequest _request;
    private readonly HwndSource _host;
    private readonly RECT _box;
    private readonly Action _reveal;
    private readonly Action _done;
    private IntPtr _incomingThumb, _leavingThumb;
    private ShadowWindow? _shadow;
    private Phase _phase = Phase.Animate;
    private TimeSpan _start = TimeSpan.Zero, _phaseStart;
    private int _readyFrames;
    private RECT _incomingRect, _leavingRect;
    private byte _incomingOpacity = 255, _leavingOpacity = 255;
    /// <summary>Garde-fou : si le rendu WPF s'arrête (ou une erreur), l'échange se termine quand même : images DWM retirées.</summary>
    private System.Windows.Threading.DispatcherTimer? _safety;
    private double _shadowLevel;

    /// <summary>Un échange est en cours (un nouveau clic le termine d'abord : <see cref="CompleteCurrent"/>).</summary>
    public static bool Running => _current != null;

    /// <summary>Termine TOUT DE SUITE l'échange en cours (fenêtre rendue, images retirées, <c>done</c> appelé).</summary>
    public static void CompleteCurrent() => _current?.Complete();

    private StageSwapEffect(StageSwapRequest request, RECT box, Action reveal, Action done)
    {
        _request = request;
        _box = box;
        _reveal = reveal;
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
        // Les clics traversent vers les fenêtres du même fil dessous (la colonne : un 2e clic termine l'échange et en lance un autre).
        _host.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg != WM_NCHITTEST) return IntPtr.Zero;
            handled = true;
            return HTTRANSPARENT;
        });
    }

    /// <summary>
    /// Pose les images animées (première image affichée avant le retour : l'appelant peut alors réduire la fenêtre qui part) et
    /// lance l'animation. Faux = impossible (rien n'est affiché, ni <paramref name="reveal"/> ni <paramref name="done"/> appelés :
    /// l'appelant fait l'échange sans animation).
    /// </summary>
    public static bool TryStart(StageSwapRequest request, Action reveal, Action done)
    {
        CompleteCurrent();
        StageSwapEffect? effect = null;
        try
        {
            var box = Union(Union(request.From, request.To), request.Leaving != IntPtr.Zero ? Union(request.LeavingFrom, request.LeavingTo) : request.To);
            box = Inflate(box, (int)Math.Ceiling((StageShape.SwapShadowBlur + StageShape.SwapShadowDepth) * request.Scale));
            effect = new StageSwapEffect(request, box, reveal, done);
            // La fenêtre qui part d'abord : DWM dessine la dernière miniature enregistrée par-dessus (l'arrivante passe devant).
            if (request.Leaving != IntPtr.Zero && DwmRegisterThumbnail(effect._host.Handle, request.Leaving, out effect._leavingThumb) != 0)
                effect._leavingThumb = IntPtr.Zero; // sans elle : l'ancienne se réduit simplement sous l'animation
            if (DwmRegisterThumbnail(effect._host.Handle, request.Incoming, out effect._incomingThumb) != 0)
            {
                effect._incomingThumb = IntPtr.Zero;
                effect.Dispose();
                return false;
            }
            try
            {
                effect._shadow = new ShadowWindow(box, request.Scale);
            }
            catch (Exception ex)
            {
                Log.Error("Stage Manager : ombre de l'échange", ex); // l'échange se fait sans ombre
                effect._shadow = null;
            }
            effect.Layout(0);
            SetWindowPos(effect._host.Handle, HWND_TOPMOST, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (effect._shadow != null)
                SetWindowPos(effect._shadow.Handle, effect._host.Handle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); // juste sous l'hôte
            DwmFlush(); // la première image est à l'écran avant que la fenêtre qui part soit réduite (pas de trou)
            _current = effect;
            CompositionTarget.Rendering += effect.OnRendering;
            var safety = effect._safety = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            safety.Tick += (_, _) => effect.Complete();
            safety.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : échange animé", ex);
            effect?.Dispose();
            if (_current == effect) _current = null;
            return false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try
        {
            Step(((RenderingEventArgs)e).RenderingTime);
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : image de l'échange", ex);
            Complete();
        }
    }

    private void Step(TimeSpan now)
    {
        if (_start == TimeSpan.Zero) _start = now;
        double speed = Math.Max(0.1, AppearanceService.Speed);
        switch (_phase)
        {
            case Phase.Animate:
                double t = Math.Min(1, (now - _start).TotalSeconds * speed / Motion.StageSwap.TotalSeconds);
                Layout(t);
                if (t < 1) return;
                _phase = Phase.Reveal;
                _phaseStart = now;
                Reveal();
                return;
            case Phase.Reveal:
                // La vraie fenêtre réapparaît SOUS l'image : l'image (désormais vivante) se cale sur sa vraie place, puis s'efface.
                if (!IsIconic(_request.Incoming) && GetWindowRect(_request.Incoming, out var real))
                {
                    _incomingRect = real;
                    Apply();
                    _readyFrames++;
                }
                if (_readyFrames >= StageSwapMotion.RevealFrames || now - _phaseStart >= StageSwapMotion.RevealTimeout)
                {
                    // L'ombre s'en va AVANT le fondu : son rectangle sombre est au-dessus de la vraie fenêtre (qui a sa propre ombre).
                    CloseShadow();
                    _phase = Phase.Fade;
                    _phaseStart = now;
                }
                return;
            case Phase.Fade:
                double f = (now - _phaseStart).TotalSeconds * speed / Motion.StageSwapFade.TotalSeconds;
                _incomingOpacity = StageSwapMotion.FadeOut(255, f);
                _leavingOpacity = StageSwapMotion.FadeOut(StageSwapMotion.LeavingOpacity(1, _request.ColumnVisible), f);
                Apply();
                if (f >= 1) Close();
                return;
        }
    }

    /// <summary>Rectangles et opacités de l'instant <paramref name="t"/> de l'animation.</summary>
    private void Layout(double t)
    {
        _incomingRect = StageSwapMotion.Frame(_request.From, _request.To, t);
        _leavingRect = StageSwapMotion.Frame(_request.LeavingFrom, _request.LeavingTo, t);
        _shadowLevel = StageSwapMotion.Progress(t); // l'ombre « monte » avec la fenêtre qui grandit
        _incomingOpacity = 255;
        _leavingOpacity = StageSwapMotion.LeavingOpacity(t, _request.ColumnVisible);
        Apply();
    }

    private void Apply()
    {
        if (_leavingThumb != IntPtr.Zero) Show(_leavingThumb, _leavingRect, _leavingOpacity);
        if (_incomingThumb != IntPtr.Zero) Show(_incomingThumb, _incomingRect, _incomingOpacity);
        _shadow?.Follow(Visible(_incomingRect), _shadowLevel);
    }

    /// <summary>Partie visible de l'image (sans les bords invisibles de la fenêtre) : là où va l'ombre.</summary>
    private RECT Visible(RECT full)
    {
        double k = (_request.To.Right - _request.To.Left) <= 0 ? 1 : (double)(full.Right - full.Left) / (_request.To.Right - _request.To.Left);
        var b = _request.Borders;
        return new RECT
        {
            Left = full.Left + (int)Math.Round(b.Left * k), Top = full.Top + (int)Math.Round(b.Top * k),
            Right = full.Right - (int)Math.Round(b.Right * k), Bottom = full.Bottom - (int)Math.Round(b.Bottom * k),
        };
    }

    private void Show(IntPtr thumbnail, RECT rect, byte opacity)
    {
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT
            {
                Left = rect.Left - _box.Left, Top = rect.Top - _box.Top, Right = rect.Right - _box.Left, Bottom = rect.Bottom - _box.Top,
            },
            opacity = opacity,
            fVisible = rect.Right - rect.Left >= 1 && rect.Bottom - rect.Top >= 1 && opacity > 0 ? 1 : 0,
            fSourceClientAreaOnly = 0,
        };
        DwmUpdateThumbnailProperties(thumbnail, ref properties);
    }

    private void Reveal()
    {
        try
        {
            _reveal();
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : fenêtre rendue après l'échange", ex);
        }
    }

    /// <summary>Fin immédiate (nouveau clic, mode coupé, erreur) : la fenêtre est rendue si elle ne l'est pas encore, sans fondu.</summary>
    private void Complete()
    {
        if (_phase == Phase.Closed) return;
        if (_phase == Phase.Animate)
        {
            _phase = Phase.Reveal;
            Reveal();
        }
        Close();
    }

    private void Close()
    {
        if (_phase == Phase.Closed) return;
        _phase = Phase.Closed;
        _safety?.Stop();
        CompositionTarget.Rendering -= OnRendering;
        if (_current == this) _current = null;
        Dispose();
        try
        {
            _done();
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : fin de l'échange", ex);
        }
    }

    private void Dispose()
    {
        _safety?.Stop();
        try
        {
            if (_incomingThumb != IntPtr.Zero) DwmUnregisterThumbnail(_incomingThumb);
            if (_leavingThumb != IntPtr.Zero) DwmUnregisterThumbnail(_leavingThumb);
            _incomingThumb = _leavingThumb = IntPtr.Zero;
            CloseShadow();
        }
        finally
        {
            _host.Dispose(); // la fenêtre hôte (et donc ses miniatures) disparaît quoi qu'il arrive
        }
    }

    private void CloseShadow()
    {
        try
        {
            _shadow?.Close();
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : ombre de l'échange", ex);
        }
        _shadow = null;
    }

    private static RECT Union(RECT a, RECT b) => new()
    {
        Left = Math.Min(a.Left, b.Left), Top = Math.Min(a.Top, b.Top), Right = Math.Max(a.Right, b.Right), Bottom = Math.Max(a.Bottom, b.Bottom),
    };

    private static RECT Inflate(RECT r, int by) => new() { Left = r.Left - by, Top = r.Top - by, Right = r.Right + by, Bottom = r.Bottom + by };

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>
    /// Rendu --snapshot d'une image intermédiaire (t = 0,3 : ~65 % du trajet) de l'échange sur un écran 1920 × 1080 simulé : fenêtres DESSINÉES (pas
    /// de miniatures DWM), mêmes calculs que l'animation (<see cref="StageSwapMotion"/>), ombre comprise. Aucune vraie fenêtre.
    /// </summary>
    internal static async Task RenderSnapshotAsync(string path, double t = 0.3)
    {
        const int W = 1920, H = 1080;
        var canvas = new Canvas { Width = W, Height = H, Background = new LinearGradientBrush(Swatch.Indigo, Swatch.Preview, 60) };
        RECT Slot(int i) => StageSwapMotion.Fit(new RECT { Left = 24, Top = 300 + i * 150, Right = 24 + 168, Bottom = 400 + i * 150 }, 16.0 / 9);
        var final = new RECT { Left = 240, Top = 40, Right = 1880, Bottom = 1000 };
        var leavingFrom = new RECT { Left = 300, Top = 90, Right = 1820, Bottom = 950 };

        FrameworkElement FakeWindow(RECT r, Color accent, double opacity, bool shadow)
        {
            double w = r.Right - r.Left, h = r.Bottom - r.Top;
            var grid = new Grid { Width = w, Height = h };
            grid.Children.Add(new Border { Background = new LinearGradientBrush(accent, Swatch.Preview, 90) });
            grid.Children.Add(new Border { Height = Math.Max(4, h * 0.06), VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Swatch.InkA(0x90)) });
            var lines = new StackPanel { Margin = new Thickness(w * 0.06, h * 0.14, 0, 0) };
            for (int i = 0; i < 5; i++)
                lines.Children.Add(new Border { Width = w * (0.55 - i * 0.07), Height = Math.Max(2, h * 0.035), Margin = new Thickness(0, 0, 0, h * 0.035), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Swatch.WhiteA(0x40)) });
            grid.Children.Add(lines);
            var shape = new SquircleBorder { Child = grid, CornerRadius = Corners.S, Opacity = opacity, Width = w, Height = h };
            if (shadow)
                shape.Effect = new DropShadowEffect
                {
                    BlurRadius = StageShape.SwapShadowBlur, ShadowDepth = StageShape.SwapShadowDepth, Direction = 270,
                    Opacity = StageShape.SwapShadowOpacity * StageSwapMotion.Progress(t), Color = Swatch.Black,
                };
            Canvas.SetLeft(shape, r.Left);
            Canvas.SetTop(shape, r.Top);
            return shape;
        }

        // Colonne : la miniature cliquée (2e) est vide (son image s'envole), les autres restent.
        canvas.Children.Add(FakeWindow(Slot(0), Swatch.Green, 1, false));
        var empty = Slot(1);
        var placeholder = new SquircleBorder { Width = empty.Right - empty.Left, Height = empty.Bottom - empty.Top, CornerRadius = Corners.S, Background = Paint.Card };
        Canvas.SetLeft(placeholder, empty.Left);
        Canvas.SetTop(placeholder, empty.Top);
        canvas.Children.Add(placeholder);
        canvas.Children.Add(FakeWindow(Slot(2), Swatch.Orange, 1, false));
        // Fenêtre qui part (vers la place de la choisie), puis la choisie par-dessus avec son ombre.
        canvas.Children.Add(FakeWindow(StageSwapMotion.Frame(leavingFrom, empty, t), Swatch.Green, StageSwapMotion.LeavingOpacity(t, true) / 255.0, false));
        canvas.Children.Add(FakeWindow(StageSwapMotion.Frame(Slot(1), final, t), Swatch.Indigo, 1, true));

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -30000,
            Top = 0,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = new Viewbox { Width = W / 2.0, Height = H / 2.0, Child = canvas },
            Title = "Dynamic Island — Stage Manager (rendu de l'échange)",
        };
        window.Show();
        window.UpdateLayout();
        await SnapshotRenderer.SaveAsync(window, path, new Rect(0, 0, W / 2.0, H / 2.0));
        window.Close();
    }

    /// <summary>
    /// Ombre légère sous la fenêtre qui arrive : fenêtre WPF transparente (layered) aux clics (WS_EX_TRANSPARENT), jamais active,
    /// de la taille du trajet ; seul un rectangle flouté s'y déplace.
    /// </summary>
    private sealed class ShadowWindow : Window
    {
        private readonly RECT _box;
        private readonly double _scale;
        private readonly SquircleBorder _shape;

        public IntPtr Handle { get; }

        public ShadowWindow(RECT box, double scale)
        {
            _box = box;
            _scale = scale <= 0 ? 1 : scale;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Title = "Dynamic Island — Stage Manager (ombre)";
            _shape = new SquircleBorder
            {
                CornerRadius = Corners.M,
                Background = Paint.Ink,
                Effect = new DropShadowEffect
                {
                    BlurRadius = StageShape.SwapShadowBlur,
                    ShadowDepth = StageShape.SwapShadowDepth,
                    Direction = 270,
                    Opacity = StageShape.SwapShadowOpacity,
                    Color = Swatch.Black,
                    RenderingBias = RenderingBias.Performance,
                },
                IsHitTestVisible = false,
            };
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(_shape);
            Content = canvas;
            Width = (box.Right - box.Left) / _scale;
            Height = (box.Bottom - box.Top) / _scale;
            Left = -30000; // posée en pixels juste après (écrans d'échelles différentes)
            var helper = new WindowInteropHelper(this);
            Handle = helper.EnsureHandle();
            SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
            SetWindowPos(Handle, HWND_TOPMOST, box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top, SWP_NOACTIVATE);
            Show();
        }

        /// <summary>Rectangle (pixels écran) et opacité de l'ombre.</summary>
        public void Follow(RECT visible, double opacity)
        {
            double w = (visible.Right - visible.Left) / _scale, h = (visible.Bottom - visible.Top) / _scale;
            _shape.Width = Math.Max(0, w);
            _shape.Height = Math.Max(0, h);
            Canvas.SetLeft(_shape, (visible.Left - _box.Left) / _scale);
            Canvas.SetTop(_shape, (visible.Top - _box.Top) / _scale);
            _shape.Opacity = Math.Clamp(opacity, 0, 1);
        }
    }
}
