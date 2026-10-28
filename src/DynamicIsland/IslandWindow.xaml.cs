using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland;

/// <summary>
/// Pilule de l'île au repos, d'où sort la palette (unités WPF) : son arrondi, si elle est en bas de son écran (la palette
/// s'ouvre alors vers le haut) et les limites de cet écran.
/// </summary>
public readonly record struct IslandAnchor(Rect Bounds, double Radius, bool Upward, Rect Screen);

public partial class IslandWindow : Window
{
    private enum Tab
    {
        Music,
        Calendar,
        Control,
        Widgets,
        Shelf,
        Voice,
        Claude,
        Clocks,
        Health,
        Friends,
    }

    /// <summary>Arrondis selon le réglage « Arrondi des coins » (100 % = pilule).</summary>
    private static double CompactRadius => AppearanceService.Radius(Radius.IslandCompact);
    private static double ExpandedRadius => AppearanceService.Radius(Radius.IslandExpanded);

    private static readonly Color AlertColor = Swatch.Orange;
    private static readonly IEasingFunction EaseOut = CreateEaseOut();

    private readonly MediaService _media;
    private readonly TimerService _timer = new();
    private readonly VolumeService _volume = new();
    private readonly BrightnessService _brightness = new();

    private readonly Spring _width = new(IslandShape.Compact.Width);
    private readonly Spring _height = new(IslandShape.Compact.Height);
    private readonly Spring _radius = new(CompactRadius);
    private readonly SolidColorBrush _rimBrush = new(AppearanceService.RimColor());
    private readonly SolidColorBrush _barsBrush = new(MediaService.DefaultAccent);
    private readonly Dictionary<UIElement, double> _fadeTargets = [];

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _fullscreenWatch = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _privacyWatch = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _volumeWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(7);
    private readonly DispatcherTimer _noticeTimer = new() { Interval = NoticeDuration };
    private readonly DispatcherTimer _meterTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _followWatch = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly double[] _barLevels = new double[4];
    private double _meterClock;
    private (double Level, bool Muted)? _lastVolume;
    private bool _hudVisible;
    private bool _noticeVisible;
    private Size _noticeSize;
    private Action? _noticeAction;
    /// <summary>Vrai pendant qu'on glisse des fichiers de l'étagère vers l'extérieur.</summary>
    private bool _draggingOut;
    /// <summary>Fichiers de l'annonce affichée (glissables), et point d'appui en attendant de savoir si c'est un glisser.</summary>
    private IReadOnlyList<string>? _noticeFiles;
    private Point? _noticePress;
    /// <summary>Écran où se trouve l'île (celui de sa place enregistrée, sauf option « suivre la souris »).</summary>
    private IntPtr _monitor;
    /// <summary>Place appliquée (<see cref="IslandPlacement.Signature"/>) : on ne replace l'île que si elle change.</summary>
    private string? _placement;
    /// <summary>Appui gauche sur l'île (pixels) : s'il glisse de plus de <see cref="MoveThreshold"/>, l'île se déplace.</summary>
    private POINT? _press;
    /// <summary>Appui sur la pilule réduite : relâcher sans bouger l'ouvre (le clic habituel).</summary>
    private bool _pressOnCompact;
    private bool _moving;
    /// <summary>Pendant un déplacement : centre de la pilule − curseur, en pixels.</summary>
    private Vector _grab;
    private const int MoveThreshold = 6;
    /// <summary>Question à Claude posée depuis la palette : réponse attendue (annonce si l'île est refermée à la fin).</summary>
    private bool _claudeAsked;
    /// <summary>L'île reste ouverte jusqu'à cette heure pendant / juste après une réponse de Claude (souris ailleurs).</summary>
    private DateTime _claudePinnedUntil = DateTime.MinValue;
    private static readonly TimeSpan ClaudeReadTime = TimeSpan.FromSeconds(8);

    private IntPtr _hwnd;
    private Tab _tab = Tab.Music;
    private bool _expanded;
    private bool _animating;
    private bool _hiddenForFullscreen;
    private bool _gaming;
    private bool _barsRunning;
    private bool _alerting;
    private bool _syncingTabs;
    private TimeSpan _lastFrame;
    private string _clockText = "";
    private BitmapSource? _compactCover;
    private readonly LiquidGlass _glass;

    /// <summary>Musique en cours (partagée avec le widget Musique du bureau).</summary>
    internal MediaService Media => _media;
    internal TimerService Timer => _timer;
    internal BrightnessService Brightness => _brightness;

    public IslandWindow()
    {
        InitializeComponent();
        _media = new MediaService(Dispatcher);

        Island.BorderBrush = _rimBrush;
        _glass = new LiquidGlass(Island, () => Island.CornerRadius.TopLeft);
        foreach (Rectangle bar in Bars.Children) bar.Fill = _barsBrush;

        MusicTab.Attach(_media);
        CalendarTab.Attach(_timer);
        ControlTab.Attach(_volume, _brightness);

        _media.Changed += UpdateCompact;
        _timer.Changed += UpdateCompact;
        _timer.Changed += UpdateCloak; // fin du minuteur en jeu : le trait réapparaît pour le dire
        _timer.Finished += OnTimerFinished;

        _clock.Tick += (_, _) => UpdateClock();
        _expandDelay.Tick += (_, _) =>
        {
            _expandDelay.Stop();
            if (IsCursorOverIsland()) Expand();
        };
        _collapseDelay.Tick += (_, _) => OnCollapseTick();
        _fullscreenWatch.Tick += (_, _) => CheckFullscreen();
        _privacyWatch.Tick += (_, _) => UpdatePrivacyDots();
        _volumeWatch.Tick += (_, _) => CheckVolume();
        _hudTimer.Tick += (_, _) => HideVolumeHud();
        _noticeTimer.Tick += (_, _) => HideNotice();
        _meterTimer.Tick += (_, _) => UpdateMeterBars();
        _followWatch.Tick += (_, _) => FollowMouse();
        _timer.FocusEnded += () => SystemSounds.Asterisk.Play(); // fin de la concentration : la pause démarre
        Notice.PreviewMouseMove += Notice_PreviewMouseMove;
        Notice.PreviewMouseLeftButtonUp += Notice_PreviewMouseLeftButtonUp;
        ShelfTab.DraggingOut += dragging =>
        {
            _draggingOut = dragging;
            if (!dragging) _collapseDelay.Start(); // l'île se refermera si la souris est partie
        };
        SettingsStore.Changed += ApplySettings;
        Services.Friends.FriendsService.Changed += UpdateFriendsTab; // un ami de plus : l'onglet « Amis » apparaît
        Services.Seasons.SeasonService.Changed += UpdateSeason;
        IsVisibleChanged += (_, _) => UpdateSeason();
        GamingMode.Changed += OnGamingChanged;
        DiscordVoice.Changed += UpdateCompact;
        ClaudeService.Changed += OnClaudeChanged;
        ClaudeTab.AskRequested += () => Palette.PaletteWindow.Toggle(this, "? "); // la barre de saisie sort de l'île
        ApplySettings();

        SelectTab(Tab.Music, animate: false);
        ApplyGeometry();
        UpdateClock();
        UpdateCompact();
        Loaded += OnLoaded;
    }

    /// <summary>Réglages de l'île (fenêtre de paramètres), appliqués en direct.</summary>
    private void ApplySettings()
    {
        // Apparence : halo néon, liseré, arrondi (le fond suit tout seul via DynamicResource).
        UpdateGlow();
        AnimateTo(new Size(_width.Target, _height.Target), _expanded ? ExpandedRadius : CompactRadius, Springs.Snap);

        var settings = SettingsStore.Current.Island;
        _expandDelay.Interval = TimeSpan.FromMilliseconds(Math.Max(settings.ExpandDelayMs, 1));
        _collapseDelay.Interval = TimeSpan.FromMilliseconds(settings.CollapseDelayMs);
        if (!settings.HideInFullscreen && _hiddenForFullscreen) CheckFullscreen();
        UpdateCloak(); // option « trait discret pendant les jeux »

        if (_barsRunning)
        {
            // Visualiseur activé / désactivé : on relance les barres dans le bon mode.
            SetBarsRunning(false);
            UpdateCompact();
        }
        if (!settings.PrivacyDots) UpdatePrivacyDots();
        bool claude = SettingsStore.Current.Claude.Enabled;
        ClaudeTabButton.Visibility = claude ? Visibility.Visible : Visibility.Collapsed;
        if (!claude && _tab == Tab.Claude) SelectTab(Tab.Music, animate: _expanded);
        bool clocks = settings.WorldClocks.Count > 0; // onglet seulement s'il y a des villes
        ClocksTabButton.Visibility = clocks ? Visibility.Visible : Visibility.Collapsed;
        if (!clocks && _tab == Tab.Clocks) SelectTab(Tab.Music, animate: _expanded);
        ClocksTab.Refresh();
        UpdateFriendsTab();
        bool health = SettingsStore.Current.Health.Tab;
        HealthTabButton.Visibility = health ? Visibility.Visible : Visibility.Collapsed;
        if (!health && _tab == Tab.Health) SelectTab(Tab.Music, animate: _expanded);
        if (SnapshotFolder == null && IsLoaded)
        {
            if (settings.FollowMouse) _followWatch.Start();
            else _followWatch.Stop();
            if (!_moving) ApplyPlacement();
        }
    }

    /// <summary>Place enregistrée changée (île déplacée, « Remettre en haut au centre », option « suivre la souris ») : l'île y va.</summary>
    private void ApplyPlacement()
    {
        var settings = SettingsStore.Current.Island;
        var monitor = settings.FollowMouse ? _monitor : ScreenInfo.Find(settings.Placement.Screen).Handle;
        if (settings.Placement.Signature == _placement && monitor == _monitor) return;
        if (monitor != _monitor) MoveToMonitor(monitor);
        else PositionOnMonitor(monitor);
    }

    // ------------------------------------------------------------------ fenêtre

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Fenêtre « outil » : ni barre des tâches ni Alt+Tab, et elle ne vole jamais le focus.
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);

        if (SnapshotFolder != null)
        {
            Left = -20000; // rendu hors écran
            var place = SettingsStore.Current.Island.Placement;
            ApplyAnchors(place.Horizontal, place.Vertical); // l'île s'ouvre du côté de sa place (réglages de test)
            return;
        }
        _monitor = ScreenInfo.Find(SettingsStore.Current.Island.Placement.Screen).Handle;
        PositionOnMonitor(_monitor);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!_moving) Dispatcher.BeginInvoke(() => PositionOnMonitor(_monitor)); // pendant un déplacement, la souris mène
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SettingsStore.Changed -= ApplySettings;
        GamingMode.Changed -= OnGamingChanged;
        DiscordVoice.Changed -= UpdateCompact;
        ClaudeService.Changed -= OnClaudeChanged;
        if (_animating) CompositionTarget.Rendering -= OnRendering;
        foreach (var timer in new[] { _privacyWatch, _volumeWatch, _hudTimer, _noticeTimer, _meterTimer, _followWatch }) timer.Stop();
        _brightness.Dispose();
        _glass.Detach();
        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        if (SnapshotFolder == null)
        {
            PlayIntro();
            _fullscreenWatch.Start();
            _privacyWatch.Start();
            _volumeWatch.Start();
            if (SettingsStore.Current.Island.FollowMouse) _followWatch.Start();
            UpdatePrivacyDots();
        }
        try
        {
            await _media.InitializeAsync();
            await _brightness.InitializeAsync();
            if (SnapshotFolder != null && !GlassBenchOnly) await RunSnapshotAsync(SnapshotFolder);
        }
        catch (Exception ex)
        {
            Log.Error("Initialisation des services", ex);
        }
    }

    /// <summary>Écrans changés (branchés, débranchés, résolution) : l'île retourne sur l'écran de sa place (ou le principal).</summary>
    /// <summary>
    /// Arrivée de l'île (5H), au lancement seulement : un point lumineux grossit en bulle, la bulle s'étire en pilule (ressorts),
    /// puis l'heure apparaît. Moins d'une seconde ; rien en jeu ni si une appli plein écran est devant.
    /// </summary>
    private async void PlayIntro()
    {
        if (GamingMode.IsActive || ScreenInfo.IsForegroundFullscreen(_monitor)) return;
        double dot = IslandShape.IntroDot, height = IslandShape.Compact.Height;
        foreach (var (spring, value) in new[] { (_width, dot), (_height, dot), (_radius, dot / 2) })
        {
            spring.SetTarget(value, Springs.Snap);
            spring.Snap();
        }
        ApplyGeometry();
        CompactView.BeginAnimation(OpacityProperty, null);
        CompactView.Opacity = 0;
        HoverRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Quick));

        AnimateTo(new Size(height, height), height / 2, Springs.IntroBubble);
        await Task.Delay(Motion.IntroStretch);
        if (_expanded) return; // déjà survolée : l'île s'ouvre normalement
        AnimateTo(CompactTargetSize(), CompactRadius, Springs.IntroStretch);
        Fade(CompactView, 1, Motion.Slow, Motion.DelayLonger);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => MoveToMonitor(ScreenInfo.Find(SettingsStore.Current.Island.Placement.Screen).Handle));

    /// <summary>
    /// Place la fenêtre sur l'écran donné d'après la place enregistrée (bord, décalages) ; une place qui ne tient plus
    /// sur l'écran (résolution plus petite) est ramenée dedans sans être réécrite.
    /// </summary>
    private void PositionOnMonitor(IntPtr monitor)
    {
        if (ScreenInfo.Describe(monitor) is not { } screen) return;
        var place = SettingsStore.Current.Island.Placement;
        _placement = place.Signature;
        ApplyAnchors(place.Horizontal, place.Vertical);
        var origin = WindowOrigin(place, screen, new Size(Width, Height));
        Left = origin.X;
        Top = origin.Y;
    }

    /// <summary>
    /// Coin haut-gauche de la fenêtre de l'île (unités WPF) pour cette place sur cet écran ; une place qui ne tient plus
    /// (résolution plus petite) est ramenée dedans. Calcul pur, vérifié par <c>--self-test</c> à plusieurs échelles.
    /// </summary>
    internal static Point WindowOrigin(IslandPlacement place, ScreenInfo.Monitor screen, Size window)
    {
        double scale = screen.Scale;
        double left = screen.Bounds.Left / scale, top = screen.Bounds.Top / scale;
        double width = screen.WidthPx / scale, height = screen.HeightPx / scale;
        var pill = IslandShape.Compact;
        double y = Math.Clamp(place.Y, 0, Math.Max(0, height / 2 - pill.Height / 2));
        double x = place.Horizontal == HorizontalAnchor.Center
            ? Math.Clamp(place.X, -Math.Max(0, width / 2 - IslandShape.MaxExpandedWidth / 2), Math.Max(0, width / 2 - IslandShape.MaxExpandedWidth / 2))
            : Math.Clamp(place.X, 0, Math.Max(0, width - IslandShape.MaxExpandedWidth));

        double originX = place.Horizontal switch
        {
            HorizontalAnchor.Left => left + x - IslandShape.EdgeMargin,
            HorizontalAnchor.Right => left + width - x + IslandShape.EdgeMargin - window.Width,
            _ => left + width / 2 + x - window.Width / 2,
        };
        double originY = place.Vertical == VerticalAnchor.Bottom
            ? top + height - y + IslandShape.TopMargin - window.Height
            : top + y - IslandShape.TopMargin;
        return new Point(originX, originY);
    }

    /// <summary>Centre de la pilule réduite (unités WPF) quand la fenêtre de l'île est en <paramref name="origin"/>.</summary>
    internal static Point PillCenter(IslandPlacement place, Point origin, Size window)
    {
        var size = IslandShape.Compact;
        double x = place.Horizontal switch
        {
            HorizontalAnchor.Left => origin.X + IslandShape.EdgeMargin + size.Width / 2,
            HorizontalAnchor.Right => origin.X + window.Width - IslandShape.EdgeMargin - size.Width / 2,
            _ => origin.X + window.Width / 2,
        };
        double y = place.Vertical == VerticalAnchor.Bottom
            ? origin.Y + window.Height - IslandShape.TopMargin - size.Height / 2
            : origin.Y + IslandShape.TopMargin + size.Height / 2;
        return new Point(x, y);
    }

    /// <summary>
    /// Côté d'où l'île grandit : collée en haut de sa fenêtre (elle s'ouvre vers le bas) ou en bas (vers le haut), au
    /// centre ou contre le bord gauche / droit (elle s'ouvre vers la droite / la gauche).
    /// </summary>
    private void ApplyAnchors(HorizontalAnchor horizontal, VerticalAnchor vertical)
    {
        bool bottom = vertical == VerticalAnchor.Bottom;
        var alignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        HoverRoot.HorizontalAlignment = horizontal switch
        {
            HorizontalAnchor.Left => HorizontalAlignment.Left,
            HorizontalAnchor.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        HoverRoot.Margin = new Thickness(horizontal == HorizontalAnchor.Left ? IslandShape.EdgeMargin : 0, 0,
            horizontal == HorizontalAnchor.Right ? IslandShape.EdgeMargin : 0, 0);
        HoverRoot.VerticalAlignment = alignment;
        EdgeBand.VerticalAlignment = alignment;
        IslandShell.VerticalAlignment = alignment;
        Island.VerticalAlignment = alignment;
        Island.Margin = bottom ? new Thickness(0, 0, 0, IslandShape.TopMargin) : new Thickness(0, IslandShape.TopMargin, 0, 0);
        ExpandedView.RenderTransformOrigin = new Point(0.5, bottom ? 1 : 0);
    }

    private void ReassertTopmost() =>
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // ------------------------------------------------------------------ survol

    private void HoverRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseDelay.Stop();
        _claudePinnedUntil = DateTime.MinValue; // la souris est là : l'île reprend son comportement habituel
        if (!_expanded && !_hiddenForFullscreen && !_gaming && !_moving) _expandDelay.Start();
    }

    private void HoverRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        _expandDelay.Stop();
        if (_expanded) _collapseDelay.Start();
    }

    private void HoverRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CountQuickClick();
        if (_expanded) return;
        if (_noticeVisible)
        {
            e.Handled = true;
            if (_noticeFiles != null)
            {
                // Annonce avec des fichiers : on attend de savoir si c'est un clic ou un glisser.
                _noticePress = e.GetPosition(this);
                Notice.CaptureMouse();
                return;
            }
            // Clic sur une annonce : son action (ex. ouvrir la page des mises à jour).
            var action = _noticeAction;
            HideNotice();
            action?.Invoke();
            return;
        }
        _expandDelay.Stop();
        if (CanMove && BeginPress(onCompact: true))
        {
            e.Handled = true; // ouverture au relâcher, ou déplacement si la souris glisse
            return;
        }
        Expand();
    }

    private void OnCollapseTick()
    {
        // On ne referme pas pendant un glisser de slider, avec le menu ouvert, ou si la
        // souris est en fait toujours sur l'île (MouseLeave parasite).
        if (_draggingOut || IsCursorOverIsland() || Mouse.Captured != null || HoverRoot.ContextMenu?.IsOpen == true) return;
        if (DateTime.UtcNow < _claudePinnedUntil) return; // Claude répond (ou vient de répondre) : on laisse lire
        _collapseDelay.Stop();
        Collapse();
    }

    private bool IsCursorOverIsland()
    {
        if (!GetCursorPos(out var cursor) || PresentationSource.FromVisual(HoverRoot) == null) return false;
        var topLeft = HoverRoot.PointToScreen(new Point(0, 0));
        var bottomRight = HoverRoot.PointToScreen(new Point(HoverRoot.ActualWidth, HoverRoot.ActualHeight));
        const double Tolerance = 4;
        return cursor.X >= topLeft.X - Tolerance && cursor.X <= bottomRight.X + Tolerance
            && cursor.Y >= topLeft.Y - Tolerance && cursor.Y <= bottomRight.Y + Tolerance;
    }

    // ------------------------------------------------------------------ déplacement (5I)

    private bool CanMove => !SettingsStore.Current.Island.LockPosition && !_gaming && !_hiddenForFullscreen && SnapshotFolder == null;

    /// <summary>Appui sur une partie « vide » de l'île ouverte : boutons, curseurs, onglets… gardent leurs clics (déjà traités).</summary>
    private void HoverRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded || !CanMove || IsWithin(e.OriginalSource as DependencyObject, ShelfTab)) return; // l'étagère a son propre glisser
        if (BeginPress(onCompact: false)) e.Handled = true;
    }

    private bool BeginPress(bool onCompact)
    {
        if (!GetCursorPos(out var cursor) || !HoverRoot.CaptureMouse()) return false;
        _press = cursor;
        _pressOnCompact = onCompact;
        return true;
    }

    private void HoverRoot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } press || !GetCursorPos(out var cursor)) return;
        if (!_moving)
        {
            if (Math.Abs(cursor.X - press.X) < MoveThreshold && Math.Abs(cursor.Y - press.Y) < MoveThreshold) return;
            StartMove(press);
        }
        MoveWith(cursor);
    }

    private void HoverRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_press == null) return;
        bool click = !_moving && _pressOnCompact;
        HoverRoot.ReleaseMouseCapture(); // → LostMouseCapture : fin du déplacement
        e.Handled = true;
        if (click) Expand();
    }

    private void HoverRoot_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_press == null || e.OriginalSource != HoverRoot) return; // un curseur de l'île qui lâche la souris
        _press = null;
        if (_moving) EndMove();
    }

    /// <summary>La pilule passe sous le curseur (l'île ouverte rentre) et suit la souris, d'un écran à l'autre.</summary>
    private void StartMove(POINT press)
    {
        _moving = true;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        var pill = PillCenterPx();
        _grab = _pressOnCompact ? new Vector(pill.X - press.X, pill.Y - press.Y) : new Vector(0, 0);
        Collapse();
        ApplyAnchors(HorizontalAnchor.Center, VerticalAnchor.Top); // pendant le glisser, la pilule est au centre de sa fenêtre
        ReassertTopmost();
    }

    private void MoveWith(POINT cursor)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = (cursor.X + _grab.X) / scale - Width / 2;
        Top = (cursor.Y + _grab.Y) / scale - IslandShape.TopMargin - IslandShape.Compact.Height / 2;
    }

    /// <summary>Lâchée : la place (écran, bord le plus proche, aimantations) est enregistrée et l'île s'y range.</summary>
    private void EndMove()
    {
        _moving = false;
        if (!GetCursorPos(out var cursor)) cursor = default;
        var center = new POINT { X = (int)Math.Round(cursor.X + _grab.X), Y = (int)Math.Round(cursor.Y + _grab.Y) };
        var screen = ScreenInfo.At(center);
        var place = PlacementFor(screen, center);
        _monitor = screen.Handle;
        SettingsStore.Update(s => s.Island.Placement = place);
        PositionOnMonitor(_monitor); // même si la place n'a pas changé : le glisser avait recentré la pilule dans sa fenêtre
    }

    /// <summary>Centre de la pilule réduite, en pixels (d'après la fenêtre et le côté d'où l'île grandit).</summary>
    private Point PillCenterPx()
    {
        var center = PillCenter(SettingsStore.Current.Island.Placement, new Point(Left, Top), new Size(Width, Height));
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new Point(center.X * scale, center.Y * scale);
    }

    /// <summary>
    /// Place d'une pilule lâchée en <paramref name="center"/> (pixels) : bord haut ou bas le plus proche, aimantée au
    /// bord de l'écran ou de la zone de travail (dock, barre de menus) et au centre ; près d'un bord gauche / droit, elle
    /// part de ce bord pour que l'île ouverte tienne dans l'écran.
    /// </summary>
    internal static IslandPlacement PlacementFor(ScreenInfo.Monitor screen, POINT center)
    {
        double scale = screen.Scale;
        double width = screen.WidthPx / scale, height = screen.HeightPx / scale;
        double x = (center.X - screen.Bounds.Left) / scale, y = (center.Y - screen.Bounds.Top) / scale;
        var pill = IslandShape.Compact;
        var place = new IslandPlacement { Screen = ScreenInfo.SettingName(screen) };

        bool bottom = y > height / 2;
        double gap = bottom ? height - y - pill.Height / 2 : y - pill.Height / 2;
        double workGap = (bottom ? screen.Bounds.Bottom - screen.WorkArea.Bottom : screen.WorkArea.Top - screen.Bounds.Top) / scale;
        place.Vertical = bottom ? VerticalAnchor.Bottom : VerticalAnchor.Top;
        place.Y = Math.Round(Snap(Math.Max(0, gap), IslandShape.TopMargin, workGap + IslandShape.TopMargin));

        double half = IslandShape.MaxExpandedWidth / 2;
        double offset = x - width / 2;
        if (Math.Abs(offset) < IslandShape.SnapDistance)
        {
            place.Horizontal = HorizontalAnchor.Center;
        }
        else if (x < half)
        {
            place.Horizontal = HorizontalAnchor.Left;
            place.X = Math.Round(Snap(Math.Max(0, x - pill.Width / 2), IslandShape.TopMargin));
        }
        else if (width - x < half)
        {
            place.Horizontal = HorizontalAnchor.Right;
            place.X = Math.Round(Snap(Math.Max(0, width - x - pill.Width / 2), IslandShape.TopMargin));
        }
        else
        {
            place.Horizontal = HorizontalAnchor.Center;
            place.X = Math.Round(offset);
        }
        return place;
    }

    /// <summary>La valeur, ou la cible la plus proche si elle est à moins de <see cref="IslandShape.SnapDistance"/>.</summary>
    private static double Snap(double value, params double[] targets)
    {
        foreach (var target in targets.OrderBy(t => Math.Abs(t - value)))
            if (Math.Abs(target - value) < IslandShape.SnapDistance) return target;
        return value;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        while (source != null)
        {
            if (source == ancestor) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void IslandMenu_Opened(object sender, RoutedEventArgs e)
    {
        ResetPlaceItem.IsEnabled = !SettingsStore.Current.Island.Placement.IsDefault;
        LockPlaceItem.IsChecked = SettingsStore.Current.Island.LockPosition;
    }

    private void ResetPlace_Click(object sender, RoutedEventArgs e) => ResetPlacement();

    /// <summary>« Remettre l'île en haut au centre » (menu de l'île, paramètres) : la place d'avant la 5I.</summary>
    public static void ResetPlacement() => SettingsStore.Update(s => s.Island.Placement = new IslandPlacement());

    private void LockPlace_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Island.LockPosition = LockPlaceItem.IsChecked);

    private void IslandSettings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Open(SettingsPage.Island);

    // ------------------------------------------------------------------ étagère : déposer des fichiers

    private static string[]? DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    /// <summary>Des fichiers arrivent au-dessus de l'île : elle s'ouvre sur l'étagère.</summary>
    private void HoverRoot_DragEnter(object sender, DragEventArgs e)
    {
        if (_draggingOut || _hiddenForFullscreen || _gaming || DroppedFiles(e) == null) return;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        Expand();
        if (_tab != Tab.Shelf) SelectTab(Tab.Shelf);
        ShelfTab.ShowDropHint(true);
    }

    private void HoverRoot_DragOver(object sender, DragEventArgs e)
    {
        // On garde une référence (un lien), pas une copie.
        e.Effects = !_draggingOut && DroppedFiles(e) != null ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void HoverRoot_DragLeave(object sender, DragEventArgs e)
    {
        ShelfTab.ShowDropHint(false);
        if (_expanded) _collapseDelay.Start(); // le minuteur vérifie la vraie position du curseur
    }

    private void HoverRoot_Drop(object sender, DragEventArgs e)
    {
        ShelfTab.ShowDropHint(false);
        if (_draggingOut || DroppedFiles(e) is not { Length: > 0 } files) return;
        ShelfService.Add(files);
        e.Handled = true;
        _collapseDelay.Start();
    }

    // ------------------------------------------------------------------ ouverture / fermeture

    private void Expand()
    {
        if (_expanded || _hiddenForFullscreen || _gaming || _moving) return;
        _expanded = true;
        ReassertTopmost();
        if (_noticeVisible)
        {
            _noticeTimer.Stop();
            _noticeVisible = false;
            Fade(Notice, 0, Motion.Fast);
        }
        if (_hudVisible)
        {
            _hudTimer.Stop();
            _hudVisible = false;
            Fade(VolumeHud, 0, Motion.Fast);
        }

        if (_timer.State == TimerState.Finished)
        {
            SelectTab(Tab.Calendar, animate: false);
            _timer.Acknowledge();
        }
        CalendarTab.ShowToday();
        if (_tab == Tab.Control) ControlTab.Refresh();
        MusicTab.IsActive = _tab == Tab.Music;
        WidgetsTab.IsActive = _tab == Tab.Widgets;
        VoiceTab.IsActive = _tab == Tab.Voice;
        ClaudeTab.IsActive = _tab == Tab.Claude;
        ClocksTab.IsActive = _tab == Tab.Clocks;
        FriendsTab.IsActive = _tab == Tab.Friends;
        HealthTab.IsActive = _tab == Tab.Health && SnapshotFolder == null;
        SetBarsRunning(false);
        SeasonLayer.Wake(open: true); // 7A : les particules ne bougent que quand on regarde l'île

        CompactView.IsHitTestVisible = false;
        ExpandedView.IsHitTestVisible = true;
        Fade(CompactView, 0, Motion.Fast);
        Fade(ExpandedView, 1, Motion.Slow, Motion.Delay);
        ScaleExpanded(1, Motion.Scale);
        AnimateTo(ExpandedSize(_tab), ExpandedRadius, Springs.Open);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        MusicTab.IsActive = false;
        WidgetsTab.IsActive = false;
        VoiceTab.IsActive = false;
        ClaudeTab.IsActive = false;
        ClocksTab.IsActive = false;
        FriendsTab.IsActive = false;
        HealthTab.IsActive = false;
        SeasonLayer.Wake(open: false); // encore un moment, puis elles se figent

        CompactView.IsHitTestVisible = true;
        ExpandedView.IsHitTestVisible = false;
        Fade(ExpandedView, 0, Motion.Quick);
        ScaleExpanded(Interaction.CollapsedContentScale, Motion.Medium);
        Fade(CompactView, 1, Motion.Medium, Motion.DelayLonger);
        UpdateCompact();
        AnimateTo(CompactTargetSize(), CompactRadius, Springs.Close);
    }

    /// <summary>
    /// Taille de l'île ouverte sur cet onglet, élargie (sans dépasser la plus large) si la rangée d'onglets — 8 avec les
    /// horloges, nom de l'onglet actif compris — toucherait l'heure en haut à droite.
    /// </summary>
    private Size ExpandedSize(Tab tab)
    {
        var size = SizeFor(tab);
        var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
        // Le nom de l'onglet actif vient d'apparaître (déclencheur du modèle) : on force une mesure fraîche de toute la rangée.
        InvalidateMeasureTree(TabStrip);
        TabStrip.Measure(any);
        HeaderRight.Visibility = Visibility.Visible;
        HeaderRight.Measure(any);
        double needed = TabStrip.DesiredSize.Width + HeaderRight.DesiredSize.Width + 2 * Space.S8 + Space.S5;
        // Beaucoup d'onglets (santé, amis…) : si même l'île la plus large ne suffit pas, l'heure s'efface (elle est dans la pilule fermée).
        if (needed > IslandShape.MaxExpandedWidth) HeaderRight.Visibility = Visibility.Collapsed;
        return new Size(Math.Min(IslandShape.MaxExpandedWidth, Math.Max(size.Width, needed)), size.Height);
    }

    private static void InvalidateMeasureTree(DependencyObject element)
    {
        if (element is UIElement ui) ui.InvalidateMeasure();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) InvalidateMeasureTree(VisualTreeHelper.GetChild(element, i));
    }

    private static Size SizeFor(Tab tab) => tab switch
    {
        Tab.Calendar => IslandShape.Calendar,
        Tab.Control => IslandShape.Control, // + pastilles des profils
        Tab.Widgets or Tab.Shelf or Tab.Voice or Tab.Clocks or Tab.Friends => IslandShape.Panel,
        Tab.Claude => IslandShape.Claude,
        Tab.Health => IslandShape.Health,
        _ => IslandShape.Music,
    };

    private Size CompactTargetSize() =>
        _gaming ? IslandShape.Gaming
        : _noticeVisible ? _noticeSize
        : _hudVisible ? IslandShape.Hud
        : (_media.HasSession && _media.IsPlaying) || _timer.State != TimerState.Idle || InVoice ? IslandShape.CompactWide
        : IslandShape.Compact;

    // ------------------------------------------------------------------ onglets

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingTabs) return;
        if (sender == MusicTabButton) SelectTab(Tab.Music);
        else if (sender == CalendarTabButton) SelectTab(Tab.Calendar);
        else if (sender == ControlTabButton) SelectTab(Tab.Control);
        else if (sender == WidgetsTabButton) SelectTab(Tab.Widgets);
        else if (sender == ShelfTabButton) SelectTab(Tab.Shelf);
        else if (sender == VoiceTabButton) SelectTab(Tab.Voice);
        else if (sender == ClaudeTabButton) SelectTab(Tab.Claude);
        else if (sender == ClocksTabButton) SelectTab(Tab.Clocks);
        else if (sender == FriendsTabButton) SelectTab(Tab.Friends);
        else if (sender == HealthTabButton) SelectTab(Tab.Health);
    }

    private void SelectTab(Tab tab, bool animate = true)
    {
        _tab = tab;
        _syncingTabs = true;
        MusicTabButton.IsChecked = tab == Tab.Music;
        CalendarTabButton.IsChecked = tab == Tab.Calendar;
        ControlTabButton.IsChecked = tab == Tab.Control;
        WidgetsTabButton.IsChecked = tab == Tab.Widgets;
        ShelfTabButton.IsChecked = tab == Tab.Shelf;
        VoiceTabButton.IsChecked = tab == Tab.Voice;
        ClaudeTabButton.IsChecked = tab == Tab.Claude;
        ClocksTabButton.IsChecked = tab == Tab.Clocks;
        FriendsTabButton.IsChecked = tab == Tab.Friends;
        HealthTabButton.IsChecked = tab == Tab.Health;
        _syncingTabs = false;

        foreach (var (view, viewTab) in new (FrameworkElement, Tab)[]
                 { (MusicTab, Tab.Music), (CalendarTab, Tab.Calendar), (ControlTab, Tab.Control), (WidgetsTab, Tab.Widgets), (ShelfTab, Tab.Shelf), (VoiceTab, Tab.Voice), (ClaudeTab, Tab.Claude), (ClocksTab, Tab.Clocks), (FriendsTab, Tab.Friends), (HealthTab, Tab.Health) })
        {
            bool visible = viewTab == tab;
            view.IsHitTestVisible = visible;
            if (animate)
            {
                Fade(view, visible ? 1 : 0, visible ? Motion.Medium : Motion.Fast, visible ? Motion.Delay : TimeSpan.Zero);
            }
            else
            {
                view.BeginAnimation(OpacityProperty, null);
                view.Opacity = visible ? 1 : 0;
            }
        }

        if (tab == Tab.Control) ControlTab.Refresh();
        MusicTab.IsActive = _expanded && tab == Tab.Music;
        WidgetsTab.IsActive = _expanded && tab == Tab.Widgets;
        VoiceTab.IsActive = _expanded && tab == Tab.Voice;
        ClaudeTab.IsActive = _expanded && tab == Tab.Claude;
        ClocksTab.IsActive = _expanded && tab == Tab.Clocks;
        FriendsTab.IsActive = _expanded && tab == Tab.Friends;
        HealthTab.IsActive = _expanded && tab == Tab.Health && SnapshotFolder == null; // rendus : valeurs d'exemple
        if (_expanded) AnimateTo(ExpandedSize(tab), ExpandedRadius, Springs.Resize);
    }

    /// <summary>Onglet « Amis » (6D-4) : seulement si la fonction est activée et qu'il y a au moins un ami.</summary>
    private void UpdateFriendsTab()
    {
        bool friends = SnapshotFolder == null && SettingsStore.Current.Friends.Enabled && Services.Friends.FriendsService.Lists.Friends.Count > 0
                       || SnapshotFolder != null && FriendsTabButton.Visibility == Visibility.Visible;
        FriendsTabButton.Visibility = friends ? Visibility.Visible : Visibility.Collapsed;
        if (!friends && _tab == Tab.Friends) SelectTab(Tab.Music, animate: _expanded);
    }

    // ------------------------------------------------------------------ mini Claude (5J)

    /// <summary>Question posée dans la palette : l'île s'ouvre sur l'onglet Claude et reste ouverte pendant la réponse.</summary>
    public void AskClaude(string question)
    {
        ClaudeService.Ask(question);
        _claudeAsked = true;
        _claudePinnedUntil = DateTime.MaxValue;
        OpenClaude();
    }

    private void OpenClaude()
    {
        if (_tab != Tab.Claude) SelectTab(Tab.Claude, animate: _expanded);
        Expand();
    }

    /// <summary>Réponse terminée : encore quelques secondes ouverte pour la lire, ou une annonce si l'île s'est refermée.</summary>
    private void OnClaudeChanged()
    {
        if (!_claudeAsked || ClaudeService.IsBusy) return;
        _claudeAsked = false;
        if (_expanded)
        {
            if (_claudePinnedUntil == DateTime.MaxValue) _claudePinnedUntil = DateTime.UtcNow + ClaudeReadTime;
            if (!IsCursorOverIsland()) _collapseDelay.Start();
        }
        else
        {
            _claudePinnedUntil = DateTime.MinValue;
            ShowNotice(Icons.Sparkles, Loc.T("claude.answered"), OpenClaude);
        }
    }

    // ------------------------------------------------------------------ vue compacte

    private void UpdateCompact()
    {
        bool playing = _media.HasSession && _media.IsPlaying;
        bool timerShown = _timer.State != TimerState.Idle;

        if (!ReferenceEquals(_compactCover, _media.Cover))
        {
            _compactCover = _media.Cover;
            CompactArt.Background = _media.Cover != null
                ? new ImageBrush(_media.Cover) { Stretch = Stretch.UniformToFill }
                : Paint.SurfaceHover;
        }
        _barsBrush.Color = _media.Accent;

        bool voice = InVoice;
        FadeOnce(CompactArt, playing && !voice ? 1 : 0); // en vocal, les avatars prennent la place de la pochette
        FadeOnce(VoiceAvatars, voice ? 1 : 0);
        if (voice) UpdateVoiceAvatars();
        FadeOnce(Bars, playing && !timerShown ? 1 : 0);
        FadeOnce(CompactTimer, timerShown ? 1 : 0);
        CompactTimerText.Text = _timer.State == TimerState.Finished ? Loc.T("timer.done") : TimerService.Format(_timer.Remaining);
        if (timerShown)
        {
            var kindBrush = Views.CalendarTimerView.KindBrush(_timer.Kind);
            TimerRing.Stroke = kindBrush;
            CompactTimerText.Foreground = kindBrush;
            TimerRing.Data = RingGeometry(_timer.State == TimerState.Finished ? 1 : _timer.Progress);
        }
        SetBarsRunning(playing && !timerShown && !_expanded && !_hudVisible && !_noticeVisible && !_gaming);

        if (_alerting && _timer.State != TimerState.Finished) StopAlert();
        UpdateGlow();
        if (!_expanded) AnimateTo(CompactTargetSize(), CompactRadius, Springs.Settle);
    }

    private static bool InVoice => DiscordVoice.InVoice && SettingsStore.Current.Discord.Enabled;

    /// <summary>Jusqu'à 3 avatars qui se chevauchent, ceux qui parlent d'abord.</summary>
    private void UpdateVoiceAvatars()
    {
        VoiceAvatars.Children.Clear();
        foreach (var member in DiscordVoice.Members.OrderByDescending(m => m.Speaking).Take(3))
        {
            var avatar = Views.VoiceView.AvatarCircle(member, Sizes.CompactAvatar);
            if (VoiceAvatars.Children.Count > 0) avatar.Margin = new Thickness(-Space.S3, 0, 0, 0); // avatars superposés
            VoiceAvatars.Children.Add(avatar);
        }
    }

    private void SetBarsRunning(bool run)
    {
        if (run == _barsRunning) return;
        _barsRunning = run;

        // Visualiseur : les barres suivent le vumètre de la sortie audio ; sinon, simple animation décorative.
        bool meter = run && SettingsStore.Current.Island.Visualizer && SnapshotFolder == null;
        if (meter) _meterTimer.Start();
        else _meterTimer.Stop();

        double[] periods = [0.45, 0.6, 0.38, 0.52];
        int i = 0;
        foreach (Rectangle bar in Bars.Children)
        {
            var scale = (ScaleTransform)bar.RenderTransform;
            if (run && !meter)
            {
                var animation = new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(periods[i % periods.Length]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 90),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(animation, Motion.DecorativeFrameRate); // suffisant et plus léger pour une fenêtre transparente
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            i++;
        }
    }

    private void UpdateClock()
    {
        var text = DateTime.Now.ToString("HH:mm");
        if (text == _clockText) return;
        _clockText = text;
        CompactClock.Text = text;
        ExpandedClock.Text = text;
    }

    // ------------------------------------------------------------------ fin du minuteur

    private void OnTimerFinished()
    {
        _alerting = true;
        var pulse = new ColorAnimation(AlertColor, Motion.Pulse)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _rimBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
        IslandShadow.BeginAnimation(DropShadowEffect.ColorProperty, pulse);
        _ = RingAsync();
    }

    private async Task RingAsync()
    {
        for (int i = 0; i < 3 && _timer.State == TimerState.Finished; i++)
        {
            SystemSounds.Exclamation.Play();
            await Task.Delay(1500);
        }
    }

    private void StopAlert()
    {
        _alerting = false;
        _rimBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        IslandShadow.BeginAnimation(DropShadowEffect.ColorProperty, null);
        UpdateGlow();
    }

    // ------------------------------------------------------------------ halo, visualiseur, confidentialité, volume

    /// <summary>Halo et liseré : couleur d'accent, ou couleur de la pochette pendant la musique (option).</summary>
    private void UpdateGlow()
    {
        if (_alerting) return;
        bool album = SettingsStore.Current.Island.AlbumGlow && _media.HasSession && _media.IsPlaying
                     && _media.Cover != null && _media.Accent != MediaService.DefaultAccent;
        Color? color = album ? _media.Accent : null;
        _rimBrush.Color = AppearanceService.RimColor(color);
        AppearanceService.ApplyGlow(IslandShadow, Shadows.Island, color);
    }

    /// <summary>Barres du visualiseur à ~30 i/s : niveau du vumètre + léger décalage par barre.</summary>
    private void UpdateMeterBars()
    {
        double level = Math.Pow(Math.Clamp(_volume.Peak(), 0, 1), 0.6); // plus proche de la perception
        _meterClock += _meterTimer.Interval.TotalSeconds;
        int i = 0;
        foreach (Rectangle bar in Bars.Children)
        {
            double wobble = 0.6 + 0.4 * Math.Sin(_meterClock * (7 + i * 2.3) + i * 1.7);
            double target = Math.Clamp(0.18 + 0.82 * level * wobble, 0.18, 1);
            _barLevels[i] += (target - _barLevels[i]) * (target > _barLevels[i] ? 0.6 : 0.25); // monte vite, redescend doucement
            ((ScaleTransform)bar.RenderTransform).ScaleY = _barLevels[i];
            i++;
        }
    }

    /// <summary>Points vert (webcam) / orange (micro) dans l'île réduite et dans l'en-tête de l'île ouverte.</summary>
    private void UpdatePrivacyDots()
    {
        if (_cloaked) return; // 7A : île invisible (jeu, plein écran) : pas de lecture du registre ni des processus
        bool enabled = SettingsStore.Current.Island.PrivacyDots;
        var camera = enabled && PrivacyService.IsCameraInUse() ? Visibility.Visible : Visibility.Collapsed;
        var microphone = enabled && PrivacyService.IsMicrophoneInUse() ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = ExpandedCameraDot.Visibility = camera;
        MicDot.Visibility = ExpandedMicDot.Visibility = microphone;
    }

    /// <summary>Volume changé ailleurs (touches du clavier, autre appli) : on l'affiche dans l'île.</summary>
    private void CheckVolume()
    {
        if (!SettingsStore.Current.Island.VolumeHud || _hiddenForFullscreen || _gaming || !IsVisible)
        {
            _lastVolume = null;
            return;
        }
        if (!_volume.TryRead(out double level, out bool muted)) return;
        if (_lastVolume is { } last && (Math.Abs(last.Level - level) > 0.004 || last.Muted != muted)
            && DateTime.UtcNow - _volume.LastOwnChange > TimeSpan.FromMilliseconds(600) && !_expanded && !_noticeVisible)
            ShowVolumeHud(level, muted);
        _lastVolume = (level, muted);
    }

    private void ShowVolumeHud(double level, bool muted)
    {
        HudIcon.Text = muted || level < 0.005 ? Icons.VolumeMute : level < 0.34 ? Icons.VolumeLow : level < 0.67 ? Icons.VolumeMedium : Icons.VolumeHigh;
        HudScale.ScaleX = muted ? 0 : level;
        HudText.Text = muted ? Loc.T("volume.muted") : $"{Math.Round(level * 100)}";
        if (!_hudVisible)
        {
            _hudVisible = true;
            SetBarsRunning(false);
            Fade(CompactView, 0, Motion.Fast);
            Fade(VolumeHud, 1, Motion.Short, Motion.DelayShort);
            AnimateTo(IslandShape.Hud, CompactRadius, Springs.Hud);
        }
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    private void HideVolumeHud()
    {
        _hudTimer.Stop();
        if (!_hudVisible) return;
        _hudVisible = false;
        Fade(VolumeHud, 0, Motion.Quick);
        Fade(CompactView, 1, Motion.Base, Motion.DelayLong);
        UpdateCompact();
    }

    /// <summary>
    /// Annonce de quelques secondes dans l'île réduite (mise à jour disponible, thème installé…) ; un clic
    /// lance <paramref name="onClick"/>. Rien pendant le mode Gaming, le plein écran ou si l'île est ouverte.
    /// </summary>
    /// <param name="duration">Durée d'affichage (7 s par défaut ; plus court pour une info passagère, comme la langue du clavier).</param>
    /// <param name="detail">2e ligne (appareil connecté : « Connecté · batterie 80 % ») : l'annonce est alors plus haute.</param>
    /// <param name="files">Fichiers qu'on peut faire glisser depuis l'annonce vers un dossier ou une appli (téléchargement terminé).</param>
    public bool ShowNotice(string glyph, string text, Action? onClick = null, TimeSpan? duration = null, string? detail = null,
        IReadOnlyList<string>? files = null)
    {
        if (_gaming || _hiddenForFullscreen || _expanded || !IsVisible) return false; // jamais pendant qu'on s'en sert
        NoticeIcon.Text = glyph;
        NoticeIcon.FontSize = detail != null ? IconSize.XXL : IconSize.M;
        NoticeText.Text = text;
        NoticeDetail.Text = detail ?? "";
        NoticeDetail.Visibility = detail != null ? Visibility.Visible : Visibility.Collapsed;
        _noticeAction = onClick;
        _noticeFiles = files is { Count: > 0 } ? files : null;
        Notice.Cursor = _noticeFiles != null || onClick != null ? Cursors.Hand : null;
        NoticeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        NoticeDetail.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = Math.Max(NoticeText.DesiredSize.Width, detail != null ? NoticeDetail.DesiredSize.Width : 0);
        double chrome = Notice.Margin.Left + Notice.Margin.Right + NoticeIcon.FontSize + Space.S5 + 2 * Space.S5;
        _noticeSize = new Size(Math.Clamp(textWidth + chrome, IslandShape.NoticeMinWidth, IslandShape.NoticeMaxWidth),
            detail != null ? IslandShape.NoticeTallHeight : IslandShape.Compact.Height);
        if (_hudVisible)
        {
            _hudTimer.Stop();
            _hudVisible = false;
            Fade(VolumeHud, 0, Motion.Fast);
        }
        _noticeVisible = true;
        SetBarsRunning(false);
        Fade(CompactView, 0, Motion.Fast);
        Fade(Notice, 1, Motion.Swift, Motion.Delay);
        Notice.IsHitTestVisible = true;
        ReassertTopmost();
        AnimateTo(_noticeSize, CompactRadius * _noticeSize.Height / IslandShape.Compact.Height, Springs.Pop);
        _noticeTimer.Stop();
        _noticeTimer.Interval = duration ?? NoticeDuration;
        _noticeTimer.Start();
        return true;
    }

    /// <summary>Glisser depuis une annonce de fichiers (téléchargement terminé) : le fichier part vers un dossier, une appli…</summary>
    private void Notice_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_noticePress is not { } start || _noticeFiles is not { } files || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _noticePress = null;
        Notice.ReleaseMouseCapture();
        _noticeTimer.Stop(); // l'annonce reste pendant le glisser
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(Notice, new DataObject(DataFormats.FileDrop, files.ToArray()), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _draggingOut = false;
            HideNotice();
        }
    }

    private void Notice_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_noticePress == null) return;
        _noticePress = null;
        Notice.ReleaseMouseCapture();
        e.Handled = true;
        var action = _noticeAction;
        HideNotice();
        action?.Invoke();
    }

    private void HideNotice()
    {
        _noticeTimer.Stop();
        if (!_noticeVisible) return;
        _noticeVisible = false;
        Notice.IsHitTestVisible = false;
        Fade(Notice, 0, Motion.Quick);
        if (!_expanded) Fade(CompactView, 1, Motion.Base, Motion.DelayLong);
        UpdateCompact();
    }

    /// <summary>Arc de cercle (anneau de progression) dans un carré de 16 px, en partant du haut.</summary>
    private static Geometry RingGeometry(double progress)
    {
        const double Center = 8, Radius = 7;
        if (progress >= 0.999) return new EllipseGeometry(new Point(Center, Center), Radius, Radius);
        double angle = Math.Max(progress, 0.001) * 2 * Math.PI;
        var start = new Point(Center, Center - Radius);
        var end = new Point(Center + Radius * Math.Sin(angle), Center - Radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(Radius, Radius), 0, progress > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry { Figures = { figure } };
        geometry.Freeze();
        return geometry;
    }

    // ------------------------------------------------------------------ écran suivi

    /// <summary>Option « l'île suit l'écran de la souris » (l'île ne bouge pas quand elle est ouverte).</summary>
    private void FollowMouse()
    {
        if (_expanded || _moving || !GetCursorPos(out var cursor)) return;
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        if (monitor != _monitor) MoveToMonitor(monitor);
    }

    private void MoveToMonitor(IntPtr monitor)
    {
        _monitor = monitor;
        PositionOnMonitor(monitor);
        if (!_hiddenForFullscreen)
            HoverRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long) { EasingFunction = EaseOut });
    }

    // ------------------------------------------------------------------ palette de commandes

    /// <summary>
    /// Pilule de l'île au repos (position et taille en unités WPF de l'écran), d'où sort la palette, et son écran ;
    /// null si l'île n'est pas affichée.
    /// </summary>
    public IslandAnchor? PaletteAnchor()
    {
        if (!IsVisible || _hiddenForFullscreen || _moving) return null;
        var size = CompactTargetSize();
        var place = SettingsStore.Current.Island.Placement;
        double x = place.Horizontal switch
        {
            HorizontalAnchor.Left => Left + IslandShape.EdgeMargin,
            HorizontalAnchor.Right => Left + Width - IslandShape.EdgeMargin - size.Width,
            _ => Left + (Width - size.Width) / 2,
        };
        bool bottom = place.Vertical == VerticalAnchor.Bottom;
        double y = bottom ? Top + Height - IslandShape.TopMargin - size.Height : Top + IslandShape.TopMargin;
        var screen = ScreenInfo.Describe(_monitor) ?? ScreenInfo.Find(null);
        var bounds = new Rect(screen.Bounds.Left / screen.Scale, screen.Bounds.Top / screen.Scale, screen.WidthPx / screen.Scale, screen.HeightPx / screen.Scale);
        return new IslandAnchor(new Rect(x, y, size.Width, size.Height), CompactRadius, bottom, bounds);
    }

    /// <summary>La palette sort de l'île : la pilule s'efface tant qu'elle est ouverte, puis revient à sa place.</summary>
    public void SetPaletteOpen(bool open)
    {
        if (open)
        {
            _expandDelay.Stop();
            Collapse();
            HoverRoot.BeginAnimation(OpacityProperty, null);
            HoverRoot.Opacity = 0;
            HoverRoot.IsHitTestVisible = false;
        }
        else
        {
            HoverRoot.IsHitTestVisible = !_hiddenForFullscreen && !_gaming;
            if (!_hiddenForFullscreen) Fade(HoverRoot, 1, Motion.Brisk);
        }
    }

    /// <summary>« timer 10 min » dans la palette.</summary>
    public void StartTimer(TimeSpan duration) => _timer.StartWith(duration);

    public void StartPomodoro()
    {
        var settings = SettingsStore.Current.Island;
        _timer.StartPomodoro(TimeSpan.FromMinutes(settings.PomodoroFocusMinutes), TimeSpan.FromMinutes(settings.PomodoroBreakMinutes));
    }

    // ------------------------------------------------------------------ plein écran

    /// <summary>
    /// Cache l'île quand une appli plein écran (vidéo…) occupe son écran. En mode Gaming, elle reste
    /// visible sous forme de trait (voir <see cref="ApplyGaming"/>).
    /// </summary>
    private void CheckFullscreen()
    {
        if (SnapshotFolder != null) return; // rendu hors écran : la fenêtre au premier plan de l'utilisateur ne compte pas
        bool fullscreen = SettingsStore.Current.Island.HideInFullscreen && !_gaming && ScreenInfo.IsForegroundFullscreen(_monitor);
        if (fullscreen == _hiddenForFullscreen) return;
        _hiddenForFullscreen = fullscreen;

        if (fullscreen)
        {
            _expandDelay.Stop();
            Collapse();
            Fade(HoverRoot, 0, Motion.Base);
            HoverRoot.IsHitTestVisible = false;
        }
        else
        {
            Fade(HoverRoot, 1, Motion.Long);
            HoverRoot.IsHitTestVisible = !_gaming;
        }
        UpdateCloak();
    }

    private void OnGamingChanged()
    {
        ApplyGaming(GamingMode.IsActive);
        UpdateSeason(); // pas d'animation pendant un jeu
    }

    /// <summary>
    /// Mode Gaming : l'île se réduit à un trait en haut de l'écran, sans survol ni volume (les clics passent
    /// au jeu). La fin du minuteur reste visible (le trait clignote en orange).
    /// </summary>
    private void ApplyGaming(bool gaming)
    {
        if (gaming == _gaming) return;
        _gaming = gaming;
        if (gaming)
        {
            _expandDelay.Stop();
            Collapse();
            HideVolumeHud();
            Fade(CompactView, 0, Motion.Brisk);
            HoverRoot.IsHitTestVisible = false;
        }
        else
        {
            Fade(CompactView, 1, Motion.Medium, Motion.DelayLong);
            HoverRoot.IsHitTestVisible = !_hiddenForFullscreen;
        }
        CheckFullscreen();
        UpdateCompact();
        UpdateCloak();
    }

    private bool _cloaked;
    private DispatcherTimer? _cloakDelay;

    /// <summary>
    /// 7A : en jeu (sauf option « trait discret » ou fin de minuteur à montrer) et en plein écran, l'île n'est plus du tout
    /// dessinée par-dessus (DWM la masque après le fondu) ; elle revient tout de suite à la fin.
    /// </summary>
    private void UpdateCloak()
    {
        if (SnapshotFolder != null || _hwnd == IntPtr.Zero) return;
        bool alert = _timer.State == TimerState.Finished;
        bool cloak = _hiddenForFullscreen || (_gaming && !SettingsStore.Current.Island.GameLine && !alert);
        if (!cloak)
        {
            _cloakDelay?.Stop();
            if (_cloaked) Interop.DockNative.SetCloaked(_hwnd, _cloaked = false);
            return;
        }
        if (_cloaked || _cloakDelay?.IsEnabled == true) return;
        _cloakDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; // après le fondu
        _cloakDelay.Tick -= OnCloakDelay;
        _cloakDelay.Tick += OnCloakDelay;
        _cloakDelay.Start();
    }

    private void OnCloakDelay(object? sender, EventArgs e)
    {
        _cloakDelay!.Stop();
        bool cloak = _hiddenForFullscreen || (_gaming && !SettingsStore.Current.Island.GameLine && _timer.State != TimerState.Finished);
        if (cloak && !_cloaked) Interop.DockNative.SetCloaked(_hwnd, _cloaked = true);
    }

    // ------------------------------------------------------------------ animation ressort

    private void AnimateTo(Size size, double radius, SpringSpec spring)
    {
        _width.SetTarget(size.Width, spring);
        _height.SetTarget(size.Height, spring);
        _radius.SetTarget(radius, spring);
        if (_animating || (_width.IsSettled && _height.IsSettled && _radius.IsSettled)) return;

        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return; // Rendering peut être levé plusieurs fois par image
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;

        dt *= AppearanceService.Speed; // réglage « Vitesse des animations »
        bool settled = _width.Step(dt) & _height.Step(dt) & _radius.Step(dt);
        ApplyGeometry();
        if (!settled) return;

        CompositionTarget.Rendering -= OnRendering;
        _animating = false;
    }

    private void ApplyGeometry()
    {
        double width = Math.Max(_width.Value, 4);
        double height = Math.Max(_height.Value, 4); // 6 px en mode Gaming
        double radius = Math.Clamp(_radius.Value, 0, Math.Min(width, height) / 2);

        Island.Width = width;
        Island.Height = height;
        Island.CornerRadius = new CornerRadius(radius);
        _glass.Refresh(); // le verre suit l'arrondi (la taille, elle, passe par SizeChanged)

        ExpandedView.Width = Math.Max(width - 2, IslandShape.MinExpandedInnerWidth);
    }

    // ------------------------------------------------------------------ utilitaires

    private static void Fade(UIElement element, double to, TimeSpan duration, TimeSpan? delay = null)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, duration)
        {
            BeginTime = delay ?? TimeSpan.Zero,
            EasingFunction = EaseOut,
            SpeedRatio = AppearanceService.Speed,
        });
    }

    /// <summary>Comme <see cref="Fade"/>, mais ne relance rien si la cible est déjà la bonne.</summary>
    private void FadeOnce(UIElement element, double to)
    {
        if (_fadeTargets.TryGetValue(element, out double current) && current == to) return;
        _fadeTargets[element] = to;
        Fade(element, to, Motion.Base);
    }

    private void ScaleExpanded(double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation(to, duration) { EasingFunction = EaseOut, SpeedRatio = AppearanceService.Speed };
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static IEasingFunction CreateEaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
