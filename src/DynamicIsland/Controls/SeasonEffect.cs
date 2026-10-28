using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Services.Seasons;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Petites particules par-dessus l'île (6F) : neige, fantômes et braises, confettis, cœurs. Légères : une trentaine
/// d'éléments déplacés par des transformations (pas de mise en page), 30 images par seconde, seulement pendant que
/// l'île est visible ; rien pendant un jeu. Au repos (7A) elles ne bougent que pendant que l'île est ouverte, puis
/// <see cref="AwakeFor"/> après : ensuite elles se figent (plus rien n'est redessiné) jusqu'au prochain passage de la
/// souris. <see cref="Burst"/> lance des confettis quelques secondes (surprises).
/// </summary>
public sealed class SeasonEffect : Canvas
{
    /// <summary>Durée de l'animation après l'apparition de la saison ou la fermeture de l'île.</summary>
    private static readonly TimeSpan AwakeFor = TimeSpan.FromSeconds(15);
    /// <summary>~30 images par seconde (la minuterie de Windows arrondit à 15,6 ms : un peu moins que 1/30 s).</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30 * 0.94);

    private static readonly Color[] Confetti =
    [
        Color.FromRgb(0xFF, 0x5E, 0x7E), Color.FromRgb(0xFF, 0xC4, 0x3D), Color.FromRgb(0x4C, 0xC9, 0xF0),
        Color.FromRgb(0x7C, 0x5C, 0xFF), Color.FromRgb(0x30, 0xD1, 0x58),
    ];

    private sealed class Particle
    {
        public required FrameworkElement Element;
        public required TranslateTransform Move;
        public RotateTransform? Spin;
        public double X, Y, SpeedY, Sway, Phase, Turn;
    }

    private readonly List<Particle> _particles = [];
    private readonly Random _random = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew(); // jamais remis à zéro : sert aussi aux échéances
    private readonly DispatcherTimer _frame;
    private Season _season;
    private bool _paused = true, _open;
    private double _last, _burstUntil, _awakeUntil;

    public SeasonEffect()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        // Une minuterie plutôt que CompositionTarget.Rendering : WPF ne redessine que les images où quelque chose bouge.
        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _frame.Tick += (_, _) => OnFrame();
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Saison à montrer (None = rien). L'île appelle aussi <see cref="Pause"/> quand elle est cachée.</summary>
    public void Show(Season season)
    {
        if (season == _season && _particles.Count > 0) return;
        _season = season;
        Rebuild();
        Wake(_open);
    }

    /// <summary>Île cachée, plein écran, jeu : on arrête de dessiner (les particules restent prêtes).</summary>
    public void Pause(bool paused)
    {
        _paused = paused;
        UpdateTimer();
    }

    /// <summary>L'île s'ouvre ou se referme : animation tant qu'elle est ouverte, puis encore un moment.</summary>
    public void Wake(bool open)
    {
        _open = open;
        _awakeUntil = Now + AwakeFor.TotalSeconds;
        UpdateTimer();
    }

    /// <summary>Confettis pendant quelques secondes, quelle que soit la saison.</summary>
    public void Burst(double seconds = 4)
    {
        for (int i = 0; i < 40; i++) Add(ConfettiPiece(), fromTop: true);
        _burstUntil = Now + seconds;
        _paused = false;
        UpdateTimer();
    }

    /// <summary>Rendu --snapshot : avance l'animation sans l'afficher en direct.</summary>
    internal void Prime(double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) Step(1 / 30.0);
    }

    /// <summary>--perf-bench : comme une île restée fermée plus longtemps que <see cref="AwakeFor"/>.</summary>
    internal void Settle()
    {
        _open = false;
        _awakeUntil = 0;
        UpdateTimer();
    }

    private bool Awake => _open || Now < _awakeUntil || Now < _burstUntil;

    private void UpdateTimer()
    {
        bool run = !_paused && _particles.Count > 0 && Awake;
        if (run == _frame.IsEnabled) return;
        if (run)
        {
            _last = Now;
            _frame.Start();
        }
        else
        {
            _frame.Stop(); // les particules restent à leur place : rien à redessiner
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        _particles.Clear();
        int count = _season switch { Season.Christmas => 28, Season.Halloween => 16, Season.NewYear => 26, Season.Valentine => 10, _ => 0 };
        for (int i = 0; i < count; i++) Add(Make(_season), fromTop: false);
        UpdateTimer();
    }

    private FrameworkElement Make(Season season) => season switch
    {
        Season.Christmas => new Ellipse
        {
            Width = 2 + _random.NextDouble() * 2.5,
            Height = 2 + _random.NextDouble() * 2.5,
            Fill = Brushes.White,
            Opacity = 0.55 + _random.NextDouble() * 0.4,
        },
        Season.Halloween => _random.NextDouble() < 0.35
            ? Glyph(Icons.Ghost, 11 + _random.NextDouble() * 5, Color.FromArgb(0xD8, 0xF2, 0xEE, 0xFF))
            : new Ellipse
            {
                Width = 3 + _random.NextDouble() * 1.5,
                Height = 3 + _random.NextDouble() * 1.5,
                Fill = new SolidColorBrush(_random.NextDouble() < 0.6 ? Color.FromRgb(0xFF, 0x8A, 0x1F) : Color.FromRgb(0xA2, 0x5B, 0xFF)),
                Opacity = 0.95,
            },
        Season.Valentine => Glyph(Icons.Heart, 8 + _random.NextDouble() * 5, Color.FromArgb(0xD0, 0xFF, 0x5E, 0x8A)),
        _ => ConfettiPiece(),
    };

    private FrameworkElement ConfettiPiece() => new Rectangle
    {
        Width = 3,
        Height = 6,
        RadiusX = 1,
        RadiusY = 1,
        Fill = new SolidColorBrush(Confetti[_random.Next(Confetti.Length)]),
    };

    private static TextBlock Glyph(string glyph, double size, Color color) => new()
    {
        Text = glyph,
        FontFamily = FontStack.Icons,
        FontSize = size,
        Foreground = new SolidColorBrush(color),
    };

    private void Add(FrameworkElement element, bool fromTop)
    {
        double width = Math.Max(ActualWidth, 124), height = Math.Max(ActualHeight, 36);
        var move = new TranslateTransform();
        var particle = new Particle
        {
            Element = element,
            Move = move,
            X = _random.NextDouble() * width,
            Y = fromTop ? -_random.NextDouble() * 20 : _random.NextDouble() * height,
            SpeedY = element switch
            {
                Rectangle => 28 + _random.NextDouble() * 30,
                TextBlock => -(6 + _random.NextDouble() * 8),       // fantômes et cœurs montent doucement
                _ when _season == Season.Halloween => -(4 + _random.NextDouble() * 10), // braises qui s'élèvent
                _ => 10 + _random.NextDouble() * 16,                 // neige
            },
            Sway = 4 + _random.NextDouble() * 8,
            Phase = _random.NextDouble() * Math.PI * 2,
            Turn = element is Rectangle ? (_random.NextDouble() - 0.5) * 360 : 0,
        };
        if (element is Rectangle)
        {
            particle.Spin = new RotateTransform(_random.NextDouble() * 360, 1.5, 3);
            element.RenderTransform = new TransformGroup { Children = { particle.Spin, move } };
        }
        else
        {
            element.RenderTransform = move;
        }
        move.X = particle.X + Math.Sin(particle.Phase) * particle.Sway; // à sa place tout de suite, même figée
        move.Y = particle.Y;
        Children.Add(element);
        _particles.Add(particle);
    }

    private void OnFrame()
    {
        double now = Now;
        Step(Math.Min(now - _last, 0.1));
        _last = now;
        UpdateTimer(); // l'île est fermée depuis un moment : on fige
    }

    private void Step(double delta)
    {
        double width = Math.Max(ActualWidth, 124), height = Math.Max(ActualHeight, 36);
        bool bursting = Now < _burstUntil;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Phase += delta * 1.6;
            p.Y += p.SpeedY * delta;
            p.Move.X = p.X + Math.Sin(p.Phase) * p.Sway;
            p.Move.Y = p.Y;
            if (p.Spin != null) p.Spin.Angle += p.Turn * delta;

            bool gone = p.SpeedY > 0 ? p.Y > height + 8 : p.Y < -14;
            if (!gone) continue;
            bool seasonal = _season switch
            {
                Season.NewYear => p.Element is Rectangle,
                Season.None => false,
                _ => p.Element is not Rectangle,
            };
            if (seasonal || bursting)
            {
                p.X = _random.NextDouble() * width; // on la remet en jeu, de l'autre côté
                p.Y = p.SpeedY > 0 ? -8 : height + 8;
            }
            else
            {
                Children.Remove(p.Element); // confetti d'une surprise terminée
                _particles.RemoveAt(i);
            }
        }
    }
}
