using System.IO;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Animation;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland;

/// <summary>
/// Mouvement de l'île (refonte O3) : pages qui glissent d'un onglet à l'autre, « outil » ouvert depuis une tuile (MorphOpen,
/// branché pour l'instant sur les tuiles CPU / RAM / GPU des Widgets → onglet Santé ; les vues détaillées d'O8 s'en serviront),
/// et rendus figés des étapes intermédiaires pour --snapshot. L'ouverture liquide est dans <see cref="MorphTo"/>, la cascade
/// dans Animation/Cascade.
/// </summary>
public partial class IslandWindow
{
    private double _heightLag;
    private MorphOpen? _morph;
    private Tab _morphFrom, _morphTo;

    /// <summary>Vue (« page ») d'un onglet.</summary>
    private FrameworkElement ViewOf(Tab tab) => tab switch
    {
        Tab.Calendar => CalendarTab,
        Tab.Control => ControlTab,
        Tab.Widgets => WidgetsTab,
        Tab.Shelf => ShelfTab,
        Tab.Voice => VoiceTab,
        Tab.Claude => ClaudeTab,
        Tab.Clocks => ClocksTab,
        Tab.Friends => FriendsTab,
        Tab.Health => HealthTab,
        Tab.Mixer => MixerTab,
        Tab.Clipboard => ClipboardTab,
        Tab.Notes => NotesTab,
        Tab.Translate => TranslateTab,
        _ => MusicTab,
    };

    private int TabIndexOf(Tab tab) => TabStrip.Children.IndexOf(TabButtons().First(t => t.Tab == tab).Button);

    /// <summary>
    /// Onglet changé : la nouvelle page glisse depuis le côté où l'on va (en colonne : de haut en bas). Retour sur l'onglet d'où
    /// un outil est parti : l'outil rentre dans sa tuile au lieu de glisser.
    /// </summary>
    private void TurnPage(Tab previous, Tab tab, bool animate, bool slide)
    {
        if (_morph is { IsOpen: true } morph && previous == _morphTo)
        {
            _morph = null;
            if (animate && _expanded && tab == _morphFrom)
            {
                morph.Close(() => { });
                return;
            }
            morph.Finish();
        }
        if (!animate || !slide || !_expanded) return;
        int direction = Math.Sign(TabIndexOf(tab) - TabIndexOf(previous));
        PageTransition.Play(ViewOf(previous), ViewOf(tab), direction, SettingsStore.Current.Island.VerticalTabs, TabPages);
    }

    /// <summary>« Ouvrir un outil » : la tuile s'agrandit jusqu'à remplir l'île, l'onglet <paramref name="target"/> apparaît dessous.</summary>
    private void OpenTool(Tab target, FrameworkElement tile)
    {
        if (!_expanded || _tab == target) return;
        if (TabButtons().First(t => t.Tab == target).Button.Visibility != Visibility.Visible) return;
        _morph?.Finish();
        var from = _tab;
        _morph = MorphOpen.Open(tile, TabPages, Corners.XL, () =>
        {
            SelectTab(target, animate: true, slide: false);
            Cascade.Play(ViewOf(target), reverse: false); // le contenu de l'outil arrive sous la tuile qui s'agrandit
        });
        _morphFrom = from;
        _morphTo = target;
    }

    /// <summary>Molette au bout de la rangée d'onglets : petit rebond élastique (rien à faire défiler de plus).</summary>
    private void TabStripOverscroll(double delta, bool vertical) => Elastic.Kick(TabStrip, delta > 0 ? 1 : -1, vertical);

    // ------------------------------------------------------------------ rendus --snapshot (O3)

    /// <summary>
    /// --snapshot : île figée au milieu de son ouverture (<paramref name="seconds"/> après le clic) : forme, fondu, cascade.
    /// Les horloges réelles sont arrêtées le temps du rendu (sinon l'image avancerait toute seule).
    /// </summary>
    private void FreezeOpening(double seconds)
    {
        if (_animating)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
        for (double t = 0; t < seconds - 1e-9; t += 1 / 60.0) StepGeometry(Math.Min(1 / 60.0, seconds - t));
        ApplyGeometry();
        MotionClock.Advance(seconds);
        MotionClock.Frozen = true;
        double fade = Math.Clamp((seconds - Motion.Delay.TotalSeconds) / Motion.Slow.TotalSeconds, 0, 1);
        double grow = Math.Clamp(seconds / Motion.Scale.TotalSeconds, 0, 1);
        static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);
        foreach (var (element, value) in new[] { (ExpandedView, EaseOutCubic(fade)), ((UIElement)CompactView, 0.0) })
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = value;
        }
        ExpandedScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ExpandedScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        ExpandedScale.ScaleX = ExpandedScale.ScaleY = 0.92 + 0.08 * EaseOutCubic(grow);
        _opal.HideSweep();
    }

    /// <summary>--snapshot : cascade à ~40 %, puis tuiles inclinée et enfoncée (onglet Widgets), sur des réglages en mémoire.</summary>
    private async Task SnapshotMotionAsync(string folder)
    {
        Collapse();
        FinishAnimations();
        SelectTab(Tab.Widgets, animate: false);
        Expand();
        FreezeOpening(0.2);
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "1d-cascade-40.png"));
        MotionClock.Frozen = false;
        FinishAnimations();
        _opal.HideSweep();

        // Survol : la météo penche vers la souris (en haut à droite), reflet sous le curseur ; appui : la tuile CPU s'enfonce.
        var weather = WidgetsTab.WeatherTile;
        var cpu = WidgetsTab.CpuTile;
        TileMotion.ShowPose(weather, new Point(weather.ActualWidth * 0.85, weather.ActualHeight * 0.2), pressed: false);
        TileMotion.ShowPose(cpu, new Point(cpu.ActualWidth * 0.3, cpu.ActualHeight * 0.6), pressed: true);
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "1e-tuiles-inclinee-enfoncee.png"));
        TileMotion.ClearPose(weather);
        TileMotion.ClearPose(cpu);
        FinishAnimations();
    }
}
