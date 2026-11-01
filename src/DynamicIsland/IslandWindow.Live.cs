using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Live;
using DynamicIsland.Services;
using DynamicIsland.Services.Remote;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>
/// Île réduite (refonte O5) : pilule minimale (l'heure, + la batterie seulement si le PC en a une) et activités en direct
/// (<see cref="LiveActivities"/>) : une seule principale (la plus prioritaire) avec son anneau et sa valeur, « +1 » pour les autres,
/// pilule qui prend la largeur de son contenu (ressort) et bordure irisée qui respire pendant une activité. Au repos : aucune
/// minuterie de plus, rien ne tourne.
/// </summary>
public partial class IslandWindow
{
    private Size _pillSize = new(IslandShape.Compact.Width, IslandShape.Compact.Height);
    private LiveActivity? _live;
    private int _liveOthers;
    private bool _liveSyncing;
    private bool _liveSample;
    private bool _liveDirty = true;
    private double? _ringDrawn = -1;
    private bool _spinning;
    private Color? _liveColor;
    private bool _liveColorSet;
    private Color _liveSurface;
    private DispatcherTimer? _transferWatch;
    private (string Icon, int Percent)? _battery;
    private bool _batteryChecked;
    private bool _hasBattery;

    private void InitializeLive()
    {
        LiveActivities.Changed += RenderPill;
        ClaudeService.Changed += SyncAssistantActivity;
        WindowsUpdateInstall.Changed += SyncUpdateActivity;
        RemoteTransfers.Changed += OnTransfersChanged;
        IsVisibleChanged += (_, _) => UpdateBreathing();
        UpdateBattery();
        if (_hasBattery) SystemEvents.PowerModeChanged += OnPowerModeChanged; // pas d'abonnement sur un PC fixe
    }

    private void DetachLive()
    {
        LiveActivities.Changed -= RenderPill;
        ClaudeService.Changed -= SyncAssistantActivity;
        WindowsUpdateInstall.Changed -= SyncUpdateActivity;
        RemoteTransfers.Changed -= OnTransfersChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _transferWatch?.Stop();
        _opal.SetBreathing(false);
    }

    // ------------------------------------------------------------------ sources des activités

    /// <summary>Minuteur / Pomodoro, musique et salon vocal → activités, puis la pilule redessinée une seule fois.</summary>
    private void SyncCoreActivities()
    {
        if (_liveSample) return; // --snapshot : activités d'exemple posées à la main
        _liveSyncing = true;
        try
        {
            SyncTimerActivity();
            SyncMediaActivity();
            SyncVoiceActivity();
        }
        finally
        {
            _liveSyncing = false;
        }
    }

    private void SyncTimerActivity()
    {
        if (_timer.State == TimerState.Idle)
        {
            LiveActivities.Remove(LiveIds.Timer);
            return;
        }
        bool finished = _timer.State == TimerState.Finished;
        var color = (Views.CalendarTimerView.KindBrush(_timer.Kind) as SolidColorBrush)?.Color;
        // Progression arrondie à 1/120 de tour : l'anneau n'est redessiné que quand ça se voit (pas 5 fois par seconde).
        double progress = finished ? 1 : Math.Round(_timer.Progress * 120) / 120;
        LiveActivities.Set(new LiveActivity(LiveIds.Timer, Icons.Timer, progress,
            finished ? Loc.T("timer.done") : TimerService.Format(_timer.Remaining), color,
            finished ? LivePriority.Alert : LivePriority.Timer)
        {
            Breathes = !finished, // fin du minuteur : l'alerte orange passe devant
        });
    }

    private void SyncMediaActivity()
    {
        if (_media.HasSession && _media.IsPlaying)
            LiveActivities.Set(new LiveActivity(LiveIds.Music, Icons.Music, null, "", _media.Accent, LivePriority.Music)
            {
                Style = LiveStyle.Music, Breathes = false,
            });
        else LiveActivities.Remove(LiveIds.Music);
    }

    private void SyncVoiceActivity()
    {
        if (InVoice)
            LiveActivities.Set(new LiveActivity(LiveIds.Voice, Icons.Headphones, null,
                string.Join(",", DiscordVoice.Members.OrderByDescending(m => m.Speaking).Take(3).Select(m => $"{m.Id}{(m.Speaking ? "*" : "")}")),
                null, LivePriority.Voice)
            {
                Style = LiveStyle.Voice, Breathes = false, // la valeur ne sert qu'à voir que les avatars ont changé (pas affichée)
            });
        else LiveActivities.Remove(LiveIds.Voice);
    }

    /// <summary>Réponse de l'assistant IA en cours : anneau indéterminé, nom de l'assistant.</summary>
    private void SyncAssistantActivity()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(SyncAssistantActivity); return; }
        if (ClaudeService.IsBusy)
            LiveActivities.Set(new LiveActivity(LiveIds.Assistant, Icons.Sparkles, null, ClaudeService.AssistantName, null, LivePriority.Assistant));
        else LiveActivities.Remove(LiveIds.Assistant);
    }

    /// <summary>Installation Windows Update par Opaline (étapes « 2/5 » : téléchargement puis installation).</summary>
    private void SyncUpdateActivity()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(SyncUpdateActivity); return; }
        if (WindowsUpdateInstall.Running)
        {
            var (progress, value) = WindowsUpdateInstall.Status;
            LiveActivities.Set(new LiveActivity(LiveIds.Update, Icons.WindowsUpdate, progress, value, null, LivePriority.Update));
        }
        else LiveActivities.Remove(LiveIds.Update);
    }

    private void OnTransfersChanged() => Dispatcher.BeginInvoke(SyncTransferActivity);

    /// <summary>Fichiers téléphone ↔ PC : avancement global en %. Surveillé (5 s) seulement tant qu'un transfert existe.</summary>
    private void SyncTransferActivity()
    {
        var transfers = RemoteTransfers.Snapshot();
        if (RemoteTransfers.Overall(transfers) is { } overall)
        {
            int percent = (int)Math.Floor(overall.Progress * 100);
            LiveActivities.Set(new LiveActivity(LiveIds.Transfer, overall.Incoming ? Icons.Download : Icons.Send, percent / 100.0,
                Loc.F("format.percent", percent), null, LivePriority.Transfer));
            if (_transferWatch == null)
            {
                _transferWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; // téléphone parti en plein envoi
                _transferWatch.Tick += (_, _) => SyncTransferActivity();
            }
            _transferWatch.Start();
            return;
        }
        _transferWatch?.Stop();
        LiveActivities.Remove(LiveIds.Transfer);
    }

    // ------------------------------------------------------------------ dessin de la pilule

    /// <summary>Activité principale, bords gauche / droit, « +1 », largeur, égaliseur, anneau qui tourne, respiration.</summary>
    private void RenderPill()
    {
        if (_liveSyncing) { _liveDirty = true; return; } // UpdateCompact redessine une seule fois à la fin
        _liveDirty = false;
        var (main, others) = LiveActivities.Pick();
        _live = main;
        _liveOthers = others;

        if (main != null)
        {
            CompactArt.Visibility = main.Style == LiveStyle.Music ? Visibility.Visible : Visibility.Collapsed;
            VoiceAvatars.Visibility = main.Style == LiveStyle.Voice ? Visibility.Visible : Visibility.Collapsed;
            LiveRing.Visibility = main.Style == LiveStyle.Ring ? Visibility.Visible : Visibility.Collapsed;
            Bars.Visibility = main.Style == LiveStyle.Music ? Visibility.Visible : Visibility.Collapsed;
            LiveValue.Visibility = main.Style == LiveStyle.Ring && main.Value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (main.Style == LiveStyle.Voice) UpdateVoiceAvatars();
            if (main.Style == LiveStyle.Ring)
            {
                LiveIcon.Text = main.Icon;
                LiveValue.Text = main.Value;
                ApplyLiveColor(main.Color);
                if (_ringDrawn != main.Progress || LiveArc.Data == null)
                {
                    _ringDrawn = main.Progress; // nouvel arc seulement s'il a bougé
                    LiveArc.Data = LivePill.Ring(main.Progress ?? Kinetics.LiveSpinArc, IslandShape.LiveRing, Stroke.Focus);
                }
            }
            LiveMoreText.Text = LivePill.MoreText(others);
            LiveMore.Visibility = others > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        FadeOnce(LiveLeading, main != null ? 1 : 0);
        FadeOnce(LiveTrailing, main != null && (main.Style != LiveStyle.Voice || others > 0) ? 1 : 0);

        LayoutPill();
        UpdateLiveMotion();
    }

    /// <summary>Couleur de l'anneau, de son icône et de la valeur : celle de l'activité, sinon l'accent (valeur en texte principal).</summary>
    private void ApplyLiveColor(Color? color)
    {
        var surface = AppearanceService.SurfaceBase;
        if (_liveColorSet && _liveColor == color && _liveSurface == surface) return;
        _liveColorSet = true;
        _liveColor = color;
        _liveSurface = surface;
        if (color is { } c)
        {
            // Lisible sur la pilule (O2) : l'orange du minuteur s'assombrit sur l'opale blanche (anneau ≥ 3, chiffres ≥ 4,5).
            var ring = Paint.Frozen(Design.OpalColor.Readable(c, surface, 3));
            LiveArc.Stroke = ring;
            LiveIcon.Foreground = ring;
            LiveValue.Foreground = Paint.Frozen(Design.OpalColor.Readable(c, surface, Design.OpalColor.TextContrast));
        }
        else
        {
            LiveArc.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            LiveIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
            LiveValue.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Paint.TextPrimary");
        }
    }

    /// <summary>Largeur de la pilule d'après son contenu mesuré (heure au centre), animée par le ressort si elle change.</summary>
    private void LayoutPill()
    {
        LiveLeading.Margin = new Thickness(IslandShape.PillPad, 0, 0, 0);
        LiveTrailing.Margin = new Thickness(0, 0, IslandShape.PillPad, 0);
        var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
        CompactCenter.Measure(any);
        double leading = 0, trailing = 0;
        if (_live != null)
        {
            LiveLeading.Measure(any);
            LiveTrailing.Measure(any);
            leading = LiveLeading.DesiredSize.Width - LiveLeading.Margin.Left;
            trailing = LiveTrailing.Visibility == Visibility.Visible && (_live.Style != LiveStyle.Voice || _liveOthers > 0)
                ? LiveTrailing.DesiredSize.Width - LiveTrailing.Margin.Right : 0;
        }
        _pillSize = new Size(LivePill.Width(CompactCenter.DesiredSize.Width, leading, trailing), IslandShape.Compact.Height);
        SettlePill();
    }

    /// <summary>La pilule va vers sa taille (seulement si la cible a changé : rien ne se relance pour rien).</summary>
    private void SettlePill()
    {
        if (_expanded || _noticeVisible || _hudVisible) return; // l'annonce et le volume ont leur propre taille
        var size = CompactTargetSize();
        if (size.Width == _width.Target && size.Height == _height.Target && _radius.Target == CompactRadius) return;
        AnimateTo(size, CompactRadius, Springs.Settle);
    }

    /// <summary>Égaliseur (musique), arc qui tourne (indéterminée) et respiration : seulement quand on voit la pilule.</summary>
    private void UpdateLiveMotion()
    {
        bool pillShown = !_expanded && !_hudVisible && !_noticeVisible && !_gaming && !_hiddenForFullscreen;
        SetBarsRunning(_live?.Style == LiveStyle.Music && pillShown);
        SetSpinning(_live is { Style: LiveStyle.Ring, Progress: null } && pillShown && !MotionPrefs.ReduceMotion && SnapshotFolder == null);
        UpdateBreathing();
    }

    /// <summary>Anneau indéterminé : l'arc tourne doucement (≈ 20 images/s), sinon il reste en haut.</summary>
    private void SetSpinning(bool spin)
    {
        if (spin == _spinning) return;
        _spinning = spin;
        if (!spin)
        {
            LiveArcTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            LiveArcTurn.Angle = 0;
            return;
        }
        var turn = new DoubleAnimation(0, 360, Kinetics.LiveSpin) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(turn, Kinetics.LiveFrameRate);
        LiveArcTurn.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    /// <summary>La bordure irisée respire pendant une activité (pas la musique ni le vocal), jamais au repos.</summary>
    private void UpdateBreathing()
    {
        bool shown = IsVisible && !_cloaked && !_hiddenForFullscreen && !_hudVisible && !_noticeVisible && SnapshotFolder == null;
        _opal.SetBreathing(LiveBreath.ShouldRun(_live?.Breathes == true, MotionPrefs.ReduceMotion, _gaming || GamingMode.IsActive,
            Design.OpalTheme.EffectsOn, _expanded, shown, _alerting));
    }

    // ------------------------------------------------------------------ batterie

    /// <summary>Batterie de la pilule : lue au démarrage ; ensuite, seulement si le PC en a une (une fois par minute, à l'heure).</summary>
    private void UpdateBattery()
    {
        if (_batteryChecked && !_hasBattery) return; // PC fixe : plus jamais relu
        _batteryChecked = true;
        (string Icon, int Percent)? battery = null;
        if (BatteryStatus.Read() is { } power) battery = LivePill.Battery(power.Flag, power.Percent, power.AcLine);
        _hasBattery = battery != null;
        if (battery == _battery) return;
        _battery = battery;
        CompactBattery.Visibility = battery != null ? Visibility.Visible : Visibility.Collapsed;
        if (battery is { } b)
        {
            BatteryIcon.Text = b.Icon;
            BatteryText.Text = Loc.F("format.percent", b.Percent);
        }
        if (IsLoaded) LayoutPill();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange) Dispatcher.BeginInvoke(UpdateBattery);
    }

    /// <summary>--snapshot : pilule avec une batterie d'exemple (ce PC n'en a peut-être pas).</summary>
    internal void ShowSampleBattery(int? percent)
    {
        _batteryChecked = true;
        _hasBattery = false; // jamais relue pendant le rendu
        _battery = percent is int p ? (Icons.BatteryMedium, p) : null;
        CompactBattery.Visibility = percent != null ? Visibility.Visible : Visibility.Collapsed;
        if (percent is int value)
        {
            BatteryIcon.Text = Icons.BatteryMedium;
            BatteryText.Text = Loc.F("format.percent", value);
        }
        LayoutPill();
    }
}

/// <summary>État d'alimentation de Windows (GetSystemPowerStatus) : lecture seule.</summary>
internal static class BatteryStatus
{
    public static (byte Flag, byte Percent, byte AcLine)? Read() =>
        GetSystemPowerStatus(out var power) ? (power.BatteryFlag, power.BatteryLifePercent, power.ACLineStatus) : null;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
}
