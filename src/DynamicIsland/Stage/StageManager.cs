using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>
/// Stage Manager (7G), désactivé par défaut. Sur CHAQUE écran : la fenêtre active de l'écran est placée au centre de la scène (zone
/// de travail moins la colonne), les autres fenêtres de l'écran sont RÉDUITES et montrées en miniatures DWM dans une colonne à
/// gauche, groupées par appli comme le dock. Clic sur une miniature = échange animé. Une nouvelle fenêtre prend le centre.
/// <list type="bullet">
/// <item>Événements de <see cref="WindowTracker"/> seulement (pas de sondage rapide), miniatures DWM (aucune capture d'écran) ;
/// mode coupé = rien ne tourne (ni crochets, ni fenêtres, ni minuterie).</item>
/// <item>Laissées telles quelles : Opaline, fenêtres outil / non réductibles, « toujours au premier plan », plein écran et jeux,
/// applis lancées en administrateur (Windows l'interdit : signalé une fois), applis « Stage Manager : ignorer » des règles.</item>
/// <item>Réversible : chaque fenêtre touchée est notée AVANT (<see cref="StageState"/>, stage-manager.txt) ; mode coupé, module
/// arrêté, fermeture, plantage (gardien) ou lancement suivant = fenêtres encore là rendues à leur place d'avant.</item>
/// <item>Pendant un jeu sur un écran, rien ne bouge sur cet écran et sa colonne est cachée (de même devant un plein écran).</item>
/// </list>
/// </summary>
public static class StageManager
{
    /// <summary>La fenêtre du centre remplit la scène (option « Applis en plein écran », active par défaut).</summary>
    private static bool FillStage => SettingsStore.Current.Stage.FillStage;

    private const uint GA_ROOTOWNER = 3;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly TimeSpan VerifyDelay = TimeSpan.FromMilliseconds(260);

    /// <summary>Fenêtre touchée par nous : sa place d'avant et ce qu'on en a fait.</summary>
    private sealed class Touched
    {
        public int ProcessId;
        public bool WasMaximized;
        public RECT Original;
        public bool Minimized;
        public RECT? Placed;
        public string? Screen;
    }

    private static Func<IslandWindow?>? _island;
    private static bool _started, _active, _swapping;
    private static WindowTracker? _tracker;
    private static DispatcherTimer? _debounce;
    private static IntPtr _pendingCenter;
    private static HashSet<IntPtr> _known = [];
    private static readonly Dictionary<IntPtr, Touched> TouchedWindows = [];
    private static readonly Dictionary<string, IntPtr> Centers = [];
    private static readonly List<IntPtr> Recent = [];
    private static readonly Dictionary<string, StageColumnWindow> Columns = [];
    private static readonly Dictionary<IntPtr, DateTime> OurMinimize = [];
    private static readonly HashSet<string> WarnedElevated = [];
    private static readonly Dictionary<string, (string Name, ImageSource? Icon)> AppLooks = [];
    /// <summary>Écart « place normale (repère de la zone de travail) → écran », par écran, mesuré sur une fenêtre normale.</summary>
    private static readonly Dictionary<string, POINT> WorkspaceOffset = [];

    /// <summary>Mode allumé ou coupé (centre de contrôle, paramètres).</summary>
    public static event Action? Changed;

    public static bool IsOn => _active;

    public static void Start(Func<IslandWindow?> island)
    {
        if (_started) return;
        _started = true;
        _island = island;
        SettingsStore.Changed += Apply;
        Apply();
    }

    /// <summary>Module arrêté (ModuleGuard) : fenêtres rendues, colonnes fermées.</summary>
    public static void Stop()
    {
        if (_started) SettingsStore.Changed -= Apply;
        _started = false;
        Apply();
    }

    /// <summary>Fermeture de l'app ou plantage : les fenêtres réduites par nous reviennent (peut être appelé plusieurs fois).</summary>
    public static void Restore()
    {
        if (_active) Deactivate();
    }

    /// <summary>Centre de contrôle, palette, raccourci : allume / coupe (réglage enregistré).</summary>
    public static void Toggle()
    {
        if (App.SafeMode) return;
        SettingsStore.Update(s => s.Stage.Enabled = !s.Stage.Enabled);
    }

    /// <summary>Fenêtre que NOUS venons de réduire : le dock ne joue pas son effet Génie vers son icône.</summary>
    public static bool MinimizedByStage(IntPtr window) =>
        OurMinimize.TryGetValue(window, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(2);

    private static void Apply()
    {
        bool wanted = _started && SettingsStore.Current.Stage.Enabled && !App.SafeMode && !ModuleGuard.IsDisabled(ModuleGuard.Stage);
        if (wanted && !_active) Activate();
        else if (!wanted && _active) Deactivate();
    }

    // ------------------------------------------------------------------ allumer / couper

    private static void Activate()
    {
        _active = true;
        StageState.RecoverIfNeeded(); // reste d'une session précédente (normalement déjà rendu au démarrage)
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) =>
        {
            _debounce?.Stop();
            Refresh();
        };
        _tracker = new WindowTracker();
        _tracker.Changed += Schedule;
        _tracker.ForegroundChanged += OnForeground;
        GamingMode.Changed += Schedule;
        SystemEvents.DisplaySettingsChanged += OnDisplays;
        _known = _tracker.Windows.Select(w => w.Handle).ToHashSet();
        ArrangeAll();
        Log.Info("Stage Manager : activé");
        Changed?.Invoke();
    }

    private static void Deactivate()
    {
        _active = false;
        _swapping = false;
        _pendingCenter = IntPtr.Zero;
        _debounce?.Stop();
        _debounce = null;
        if (_tracker != null)
        {
            _tracker.Changed -= Schedule;
            _tracker.ForegroundChanged -= OnForeground;
            _tracker.Dispose();
            _tracker = null;
        }
        GamingMode.Changed -= Schedule;
        SystemEvents.DisplaySettingsChanged -= OnDisplays;
        foreach (var column in Columns.Values)
        {
            try
            {
                column.Close();
            }
            catch (Exception ex)
            {
                Log.Error("Stage Manager : fermeture d'une colonne", ex);
            }
        }
        Columns.Clear();
        int restored = StageState.Restore(Entries());
        StageState.Save([]);
        TouchedWindows.Clear();
        Centers.Clear();
        Recent.Clear();
        OurMinimize.Clear();
        _known.Clear();
        StageWindows.ForgetProcesses();
        Log.Info($"Stage Manager : coupé ({restored} fenêtre(s) rendue(s))");
        Changed?.Invoke();
    }

    private static void OnDisplays(object? sender, EventArgs e) => Guarded.OnUi(() =>
    {
        WorkspaceOffset.Clear();
        Schedule();
    }, "Stage Manager : écrans changés");

    private static void Schedule()
    {
        if (_debounce == null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    // ------------------------------------------------------------------ décisions

    private static bool Manageable(AppWindow window)
    {
        var flags = StageWindows.Flags(window);
        var reason = StageLayout.Exclusion(flags);
        if (reason == StageExclusion.Elevated && StageWindows.ProcessName(window) is { } process && WarnedElevated.Add(process))
        {
            Log.Info($"Stage Manager : {process} est lancée en administrateur, laissée à sa place");
            var name = Look(window).Name;
            _island?.Invoke()?.ShowNotice(Icons.Stage, Loc.T("stage.elevated"), duration: TimeSpan.FromSeconds(5), detail: name);
        }
        return reason == StageExclusion.None;
    }

    private static List<ScreenInfo.Monitor> Monitors() => ScreenInfo.All();

    /// <summary>Écran (nom pour Windows) d'une fenêtre : sa plus grande part ; réduite = l'écran où on l'a vue en dernier.</summary>
    private static string? ScreenOf(IntPtr window, List<ScreenInfo.Monitor> monitors)
    {
        if (IsIconic(window))
        {
            if (TouchedWindows.TryGetValue(window, out var touched) && touched.Screen != null && monitors.Any(m => m.Device == touched.Screen))
                return touched.Screen;
            var handle = MonitorFromWindow(window, 2); // réduite : Windows prend sa place d'avant
            return monitors.FirstOrDefault(m => m.Handle == handle).Device;
        }
        if (!GetWindowRect(window, out var rect)) return null;
        int index = StageLayout.ScreenFor(rect, monitors.Select(m => m.Bounds).ToList());
        return index < 0 ? null : monitors[index].Device;
    }

    private static string Key(AppWindow window) => StageLayout.AppKey(window.AppId, window.ExePath, window.Handle);

    private static (string Name, ImageSource? Icon) Look(AppWindow window)
    {
        var key = Key(window);
        if (AppLooks.TryGetValue(key, out var look)) return look;
        try
        {
            var item = Dock.DockItem.FromWindow(window);
            look = (item.Name, item.BaseIcon);
        }
        catch (Exception ex)
        {
            Log.Error("Stage Manager : nom et icône d'une appli", ex);
            look = (window.Title, null);
        }
        if (AppLooks.Count > 100) AppLooks.Clear();
        AppLooks[key] = look;
        return look;
    }

    private static void MarkRecent(IntPtr window)
    {
        Recent.Remove(window);
        Recent.Insert(0, window);
        if (Recent.Count > 100) Recent.RemoveAt(Recent.Count - 1);
    }

    // ------------------------------------------------------------------ mise en place

    /// <summary>Au démarrage du mode : sur chaque écran, une fenêtre au centre et les autres réduites.</summary>
    private static void ArrangeAll()
    {
        if (_tracker == null) return;
        var monitors = Monitors();
        var foreground = Root(GetForegroundWindow());
        var windows = _tracker.Windows.Where(Manageable).ToList();
        foreach (var w in windows) if (!Recent.Contains(w.Handle)) Recent.Add(w.Handle); // ordre d'empilement = ordre d'utilisation
        if (foreground != IntPtr.Zero && windows.Any(w => w.Handle == foreground)) MarkRecent(foreground);

        foreach (var monitor in monitors)
        {
            if (GamingMode.IsOnMonitor(monitor.Handle)) continue;
            var mine = windows.Where(w => !IsIconic(w.Handle) && ScreenOf(w.Handle, monitors) == monitor.Device).ToList();
            if (mine.Count == 0) continue;
            var center = mine.FirstOrDefault(w => w.Handle == foreground) ?? mine[0];
            Centers[monitor.Device] = center.Handle;
            Place(center.Handle, monitor);
            foreach (var other in mine.Where(w => w.Handle != center.Handle)) MinimizeOurs(other.Handle, monitor.Device);
        }
        UpdateColumns();
    }

    /// <summary>Liste des fenêtres changée (ouverte, fermée, réduite…), jeu, écrans : nouvelles fenêtres au centre, colonnes à jour.</summary>
    private static void Refresh()
    {
        if (!_active || _tracker == null || _swapping) return;
        var current = _tracker.Windows;
        var handles = current.Select(w => w.Handle).ToHashSet();
        foreach (var gone in TouchedWindows.Keys.Where(h => !handles.Contains(h) && !IsWindow(h)).ToList()) TouchedWindows.Remove(gone);
        Recent.RemoveAll(h => !handles.Contains(h) && !IsWindow(h));
        foreach (var key in Centers.Where(c => !IsWindow(c.Value) || IsIconic(c.Value)).Select(c => c.Key).ToList()) Centers.Remove(key);
        foreach (var gone in OurMinimize.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(10)).Select(p => p.Key).ToList()) OurMinimize.Remove(gone);

        // Applis passées en « ignorer » (règle), ou devenues plein écran : leurs fenêtres réduites par nous sont rendues.
        var released = TouchedWindows.Where(t => t.Value.Minimized && current.FirstOrDefault(w => w.Handle == t.Key) is { } w && !Manageable(w)).ToList();
        if (released.Count > 0)
        {
            StageState.Restore(released.Select(p => Entry(p.Key, p.Value)));
            foreach (var (h, _) in released) TouchedWindows.Remove(h);
        }

        var monitors = Monitors();
        if (_pendingCenter != IntPtr.Zero && IsWindow(_pendingCenter) && !IsIconic(_pendingCenter))
        {
            var pending = _pendingCenter;
            _pendingCenter = IntPtr.Zero;
            if (current.FirstOrDefault(w => w.Handle == pending) is { } window && Manageable(window)) TakeCenter(window, monitors);
        }
        // Nouvelle fenêtre (pas réduite) : elle prend le centre de son écran, l'ancienne part dans la colonne.
        foreach (var window in current.Where(w => !_known.Contains(w.Handle) && !w.IsMinimized))
            if (Manageable(window)) TakeCenter(window, monitors);
        _known = handles;
        SaveState();
        UpdateColumns();
    }

    private static void OnForeground()
    {
        if (!_active || _tracker == null) return;
        if (_swapping) return; // l'échange choisit lui-même la fenêtre au premier plan
        var root = Root(_tracker.Foreground);
        var window = _tracker.Windows.FirstOrDefault(w => w.Handle == root);
        if (window == null)
        {
            Schedule(); // fenêtre toute neuve : vue au prochain rafraîchissement
            UpdateColumns(); // plein écran / jeu sur un écran : colonne cachée
            return;
        }
        MarkRecent(root);
        if (!Manageable(window))
        {
            UpdateColumns();
            return;
        }
        if (IsIconic(root))
        {
            _pendingCenter = root; // en train d'être rendue (clic sur le dock…) : placée dès qu'elle est là
            Schedule();
            return;
        }
        TakeCenter(window, Monitors());
        UpdateColumns();
    }

    /// <summary>Cette fenêtre devient la fenêtre au centre de son écran ; l'ancienne (encore affichée) est réduite.</summary>
    private static void TakeCenter(AppWindow window, List<ScreenInfo.Monitor> monitors)
    {
        var screen = ScreenOf(window.Handle, monitors);
        if (screen == null) return;
        var monitor = monitors.First(m => m.Device == screen);
        Centers.TryGetValue(screen, out var old);
        if (!StageLayout.TakesCenter(true, window.Handle, old, GamingMode.IsOnMonitor(monitor.Handle))) return;
        Centers[screen] = window.Handle;
        MarkRecent(window.Handle);
        Place(window.Handle, monitor);
        if (old != IntPtr.Zero && IsWindow(old) && !IsIconic(old) && ScreenOf(old, monitors) == screen) MinimizeOurs(old, screen);
    }

    /// <summary>Note la place d'avant d'une fenêtre (une seule fois : c'est celle d'avant Stage Manager).</summary>
    private static Touched Note(IntPtr window, string? screen)
    {
        if (!TouchedWindows.TryGetValue(window, out var touched))
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(window, ref placement);
            GetWindowThreadProcessId(window, out uint pid);
            bool iconic = IsIconic(window);
            touched = new Touched
            {
                ProcessId = (int)pid,
                WasMaximized = iconic ? (placement.flags & WPF_RESTORETOMAXIMIZED) != 0 : IsZoomed(window),
                Original = placement.rcNormalPosition,
            };
            TouchedWindows[window] = touched;
        }
        if (screen != null) touched.Screen = screen;
        return touched;
    }

    private static void MinimizeOurs(IntPtr window, string screen)
    {
        if (IsIconic(window)) return;
        MeasureWorkspace(window, screen);
        var touched = Note(window, screen);
        bool wasMinimized = touched.Minimized;
        touched.Minimized = true;
        if (!SaveState())
        {
            touched.Minimized = wasMinimized; // sans trace sur le disque, on ne réduit rien (on ne pourrait pas la rendre après un plantage)
            return;
        }
        OurMinimize[window] = DateTime.UtcNow;
        WindowActions.Remember(window); // le dock la rendra telle quelle (agrandie ou non, sa place)
        // Réduite SANS activer une autre fenêtre : sinon Windows passerait au premier plan une voisine pas encore réduite, qui
        // prendrait le centre à la place de la bonne.
        ShowWindowAsync(window, SW_SHOWMINNOACTIVE);
    }

    /// <summary>Place la fenêtre au centre de la scène de son écran (taille gardée si elle tient), puis vérifie un instant après.</summary>
    private static void Place(IntPtr window, ScreenInfo.Monitor monitor)
    {
        if (IsIconic(window) || !GetWindowRect(window, out var windowRect)) return;
        MeasureWorkspace(window, monitor.Device);
        var touched = Note(window, monitor.Device);
        touched.Minimized = false;
        var visible = GetVisibleBounds(window);
        bool maximized = IsZoomed(window);
        bool resizable = StageWindows.Resizable(window);
        var stage = StageLayout.StageArea(monitor.WorkArea, monitor.Scale);
        var target = StageLayout.CenterPlace(stage, visible.Right - visible.Left, visible.Bottom - visible.Top, resizable, maximized || FillStage);
        if (!maximized && Near(target, visible, 2))
        {
            touched.Placed = windowRect;
            SaveState();
            return;
        }
        var full = StageLayout.WithBorders(target, windowRect, visible);
        touched.Placed = full;
        if (!SaveState()) return;
        if (maximized) ShowWindowAsync(window, SW_SHOWNOACTIVATE); // plus agrandie : la colonne reste libre
        uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS; // asynchrone : une appli figée ne bloque pas Opaline
        if (!resizable && !maximized) flags |= SWP_NOSIZE;
        SetWindowPos(window, IntPtr.Zero, full.Left, full.Top, full.Right - full.Left, full.Bottom - full.Top, flags);
        Verify(window, monitor);
    }

    /// <summary>Un instant après : la fenêtre a peut-être refusé la taille (ou ses bords invisibles ont changé) → seulement recentrée.</summary>
    private static void Verify(IntPtr window, ScreenInfo.Monitor monitor)
    {
        var timer = new DispatcherTimer { Interval = VerifyDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Guarded.Run(() =>
            {
                if (!_active || !IsWindow(window) || IsIconic(window) || !GetWindowRect(window, out var rect)) return;
                var visible = GetVisibleBounds(window);
                var stage = StageLayout.StageArea(monitor.WorkArea, monitor.Scale);
                var target = StageLayout.CenterPlace(stage, visible.Right - visible.Left, visible.Bottom - visible.Top, false, false);
                int dx = target.Left - visible.Left, dy = target.Top - visible.Top;
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
                {
                    SetWindowPos(window, IntPtr.Zero, rect.Left + dx, rect.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
                    rect = new RECT { Left = rect.Left + dx, Top = rect.Top + dy, Right = rect.Right + dx, Bottom = rect.Bottom + dy };
                }
                if (TouchedWindows.TryGetValue(window, out var touched)) touched.Placed = rect;
                MeasureWorkspace(window, monitor.Device);
                SaveState();
            }, "Stage Manager : vérification de la place");
        };
        timer.Start();
    }

    /// <summary>Écart entre la place normale (repère de la zone de travail) et l'écran, mesuré sur une fenêtre ni réduite ni agrandie.</summary>
    private static void MeasureWorkspace(IntPtr window, string screen)
    {
        if (IsIconic(window) || IsZoomed(window) || !GetWindowRect(window, out var rect)) return;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(window, ref placement)) return;
        WorkspaceOffset[screen] = new POINT { X = rect.Left - placement.rcNormalPosition.Left, Y = rect.Top - placement.rcNormalPosition.Top };
    }

    private static bool Near(RECT a, RECT b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance
        && Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    private static IntPtr Root(IntPtr window) => window == IntPtr.Zero ? IntPtr.Zero : GetAncestor(window, GA_ROOTOWNER);

    // ------------------------------------------------------------------ échange depuis la colonne

    private static void OnChosen(string screen, IntPtr window, RECT slot)
    {
        if (!_active || _swapping || !IsWindow(window)) return;
        var monitors = Monitors();
        if (monitors.FirstOrDefault(m => m.Device == screen) is not { Device: not null } monitor || monitor.Handle == IntPtr.Zero) return;
        MarkRecent(window);
        if (!IsIconic(window))
        {
            WindowActions.Activate(window); // déjà affichée : le premier plan suffit (elle prend le centre)
            return;
        }
        Centers.TryGetValue(screen, out var old);
        if (old == window || old != IntPtr.Zero && (!IsWindow(old) || IsIconic(old))) old = IntPtr.Zero;

        // Place d'arrivée : sa taille normale (agrandie avant = toute la scène).
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(window, ref placement);
        bool maximized = (placement.flags & WPF_RESTORETOMAXIMIZED) != 0;
        var normal = placement.rcNormalPosition;
        var stage = StageLayout.StageArea(monitor.WorkArea, monitor.Scale);
        var target = StageLayout.CenterPlace(stage, normal.Right - normal.Left, normal.Bottom - normal.Top, StageWindows.Resizable(window), maximized || FillStage);

        var touched = Note(window, screen);
        if (old != IntPtr.Zero) MeasureWorkspace(old, screen);
        if (WorkspaceOffset.TryGetValue(screen, out var offset))
        {
            // Rendue directement à sa place au centre (sinon elle apparaîtrait d'abord à son ancienne place).
            placement.rcNormalPosition = new RECT
            {
                Left = target.Left - offset.X, Top = target.Top - offset.Y,
                Right = target.Right - offset.X, Bottom = target.Bottom - offset.Y,
            };
            placement.flags &= ~WPF_RESTORETOMAXIMIZED;
            placement.showCmd = SW_SHOWMINNOACTIVE;
            if (SetWindowPlacement(window, ref placement)) WindowActions.Remember(window); // le dock la rendra elle aussi à cette place
        }
        touched.Placed = target;
        Centers[screen] = window;
        var leavingFrom = old != IntPtr.Zero ? GetVisibleBounds(old) : default;
        var column = Columns.TryGetValue(screen, out var c) ? c.TopSlot : slot;
        if (old != IntPtr.Zero) MinimizeOurs(old, screen);
        _swapping = true;
        StageSwapEffect.Run(window, slot, target, old, leavingFrom, column, () =>
        {
            _swapping = false;
            touched.Minimized = false;
            OurMinimize.Remove(window);
            WindowActions.Activate(window); // rendue (SW_RESTORE) et au premier plan : le clic sur la colonne nous en donne le droit
            SaveState();
            // Un instant après (elle est réapparue) : mise au centre exacte (taille, bords invisibles), rien si elle y est déjà.
            var settle = new DispatcherTimer { Interval = VerifyDelay };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                Guarded.Run(() =>
                {
                    if (_active && IsWindow(window) && !IsIconic(window)) Place(window, monitor);
                    Schedule();
                }, "Stage Manager : fin de l'échange");
            };
            settle.Start();
        });
    }

    // ------------------------------------------------------------------ colonnes

    private static void UpdateColumns()
    {
        if (!_active || _tracker == null) return;
        var monitors = Monitors();
        var windows = _tracker.Windows.Where(Manageable).ToList();
        // Fenêtre du centre glissée sur un autre écran : elle y devient la fenêtre du centre (si la place est libre), sans rien réduire.
        foreach (var (screen, center) in Centers.ToList())
        {
            if (!IsWindow(center) || IsIconic(center) || ScreenOf(center, monitors) is not { } now || now == screen) continue;
            Centers.Remove(screen);
            if (!Centers.TryGetValue(now, out var other) || !IsWindow(other) || IsIconic(other)) Centers[now] = center;
        }
        int Rank(IntPtr h)
        {
            int i = Recent.IndexOf(h);
            return i < 0 ? int.MaxValue : i;
        }
        foreach (var monitor in monitors)
        {
            var column = Columns.TryGetValue(monitor.Device, out var existing) ? existing : null;
            bool hidden = GamingMode.IsOnMonitor(monitor.Handle) || ScreenInfo.IsForegroundFullscreen(monitor.Handle);
            List<AppWindow> mine = hidden ? [] : windows.Where(w => ScreenOf(w.Handle, monitors) == monitor.Device)
                .Select((w, z) => (Window: w, Z: z)).OrderBy(p => Rank(p.Window.Handle)).ThenBy(p => p.Z).Select(p => p.Window).ToList();
            Centers.TryGetValue(monitor.Device, out var center);
            int max = StageLayout.MaxGroups(monitor.WorkArea.Bottom - monitor.WorkArea.Top, monitor.Scale);
            var groups = StageLayout.Groups(mine.Select(w => (w.Handle, Key(w))), center, max);
            var shown = groups.Select(g =>
            {
                var groupWindows = g.Windows.Select(h => mine.First(w => w.Handle == h)).ToList();
                var (name, icon) = Look(groupWindows[0]);
                return new StageColumnGroup(g.Key, name, icon, groupWindows);
            }).ToList();
            if (shown.Count == 0)
            {
                column?.HideColumn();
                continue;
            }
            if (column == null)
            {
                column = new StageColumnWindow();
                var screen = monitor.Device;
                column.WindowChosen += (window, slot) => Guarded.Run(() => OnChosen(screen, window, slot), "Stage Manager : échange");
                column.GroupMenu += (group, target) => Guarded.Run(() => ShowGroupMenu(screen, group, target), "Stage Manager : menu");
                Columns[monitor.Device] = column;
            }
            column.ShowGroups(shown, monitor.WorkArea, monitor.Scale);
        }
        foreach (var gone in Columns.Keys.Where(k => monitors.All(m => m.Device != k)).ToList())
        {
            Columns[gone].Close();
            Columns.Remove(gone);
        }
    }

    /// <summary>Clic droit sur un groupe : ses fenêtres, « ignorer cette appli », couper Stage Manager.</summary>
    private static void ShowGroupMenu(string screen, StageColumnGroup group, FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Right };
        foreach (var window in group.Windows)
        {
            var item = new MenuItem { Header = window.Title.Length > 60 ? window.Title[..60] + "…" : window.Title };
            var handle = window.Handle;
            item.Click += (_, _) => Guarded.Run(() =>
            {
                if (Columns.TryGetValue(screen, out var column)) OnChosen(screen, handle, column.TopSlot);
            }, "Stage Manager : échange");
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var process = StageWindows.ProcessName(group.Windows[0]);
        if (process != null)
        {
            var ignore = new MenuItem { Header = Loc.F("stage.menu.ignore", group.Name) };
            ignore.Click += (_, _) => SettingsStore.Update(s =>
            {
                var clean = AppRule.Clean(process);
                if (AppRule.For(s.AppRules, clean) is { } rule) rule.StageIgnore = true;
                else s.AppRules.Add(new AppRule { Process = clean, Name = group.Name, StageIgnore = true });
            });
            menu.Items.Add(ignore);
        }
        var off = new MenuItem { Header = Loc.T("stage.menu.off") };
        off.Click += (_, _) => Toggle();
        menu.Items.Add(off);
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ état sur le disque

    private static StageEntry Entry(IntPtr handle, Touched t) =>
        new(handle.ToInt64(), t.ProcessId, t.Minimized, t.WasMaximized, t.Original, t.Minimized ? null : t.Placed);

    private static List<StageEntry> Entries() => TouchedWindows.Select(p => Entry(p.Key, p.Value)).ToList();

    private static bool SaveState() => StageState.Save(Entries());

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
