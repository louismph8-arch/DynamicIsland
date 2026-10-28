using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Services.Seasons;
using DynamicIsland.Themes;

namespace DynamicIsland;

/// <summary>
/// Pour le fun (6F) : l'effet de la saison sur l'île (voir Services/Seasons et Controls/SeasonEffect), et quelques surprises
/// cachées (la liste est dans CLAUDE.md, pas dans l'interface).
/// </summary>
public partial class IslandWindow
{
    private readonly List<DateTime> _quickClicks = [];
    private RotateTransform? _roll;
    private TranslateTransform? _shake;

    /// <summary>Saison changée, île montrée / cachée, jeu commencé / fini.</summary>
    private void UpdateSeason()
    {
        SeasonLayer.Show(SeasonService.Current);
        SeasonLayer.Pause(!IsVisible || GamingMode.IsActive || SnapshotFolder != null);
    }

    /// <summary>Surprise demandée (palette, code secret…).</summary>
    internal void Surprise(string id)
    {
        // La palette se referme d'abord (l'île réapparaît à sa place) : on attend un instant.
        var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            switch (id)
            {
                case "konami":
                    SeasonLayer.Burst(5);
                    ShowNotice(Icons.Sparkles, Loc.T("fun.konami"), duration: TimeSpan.FromSeconds(5), detail: Loc.T("fun.konami.detail"));
                    break;
                case "roll":
                    Roll();
                    break;
                case "credits":
                    SeasonLayer.Burst(3);
                    ShowNotice(Icons.Heart, Loc.T("fun.credits.notice"), duration: TimeSpan.FromSeconds(5), detail: Loc.T("fun.credits.detail"));
                    break;
            }
        };
        later.Start();
    }

    /// <summary>« Do a barrel roll » : l'île fait un tour complet.</summary>
    private void Roll()
    {
        EnsureFunTransforms();
        _roll!.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Plein de clics d'affilée sur l'île : elle se tortille (ça chatouille).</summary>
    private void CountQuickClick()
    {
        var now = DateTime.UtcNow;
        _quickClicks.Add(now);
        _quickClicks.RemoveAll(t => now - t > TimeSpan.FromSeconds(2.5));
        if (_quickClicks.Count < 7) return;
        _quickClicks.Clear();
        EnsureFunTransforms();
        var wiggle = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        foreach (var (x, ms) in new[] { (-7.0, 60), (7.0, 140), (-5.0, 220), (5.0, 300), (-2.0, 380), (0.0, 520) })
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        _shake!.BeginAnimation(TranslateTransform.XProperty, wiggle);
        App.ShowSoonFromIsland(() => ShowNotice(Icons.Smile, Loc.T("fun.tickle"), duration: TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(10));
    }

    private void EnsureFunTransforms()
    {
        if (_roll != null) return;
        _roll = new RotateTransform();
        _shake = new TranslateTransform();
        IslandShell.RenderTransformOrigin = new Point(0.5, 0.5);
        IslandShell.RenderTransform = new TransformGroup { Children = { _roll, _shake } };
    }
}
