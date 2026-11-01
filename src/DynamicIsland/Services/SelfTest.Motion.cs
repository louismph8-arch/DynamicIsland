using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicIsland.Animation;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du mouvement (refonte O3) : décalages de la cascade, courbes qui finissent exactement à leur cible sans dériver,
/// inclinaison et élastique bornés, réglages d'accessibilité liés à Windows (lecture seule), animations réduites = courtes et
/// sans rebond. Calculs purs : aucune fenêtre, rien n'est écrit.
/// </summary>
internal static partial class SelfTest
{
    private static void MotionO3(List<string> lines)
    {
        // Cascade : 25–40 ms entre deux éléments, toujours croissant, plafonné (une longue liste ne traîne pas) ; fermeture plus
        // rapide et dans l'autre sens (le dernier part tout de suite).
        int max = Kinetics.CascadeMaxItems;
        var delays = Enumerable.Range(0, max + 8).Select(i => Cascade.DelayFor(i, max + 8, reverse: false)).ToArray();
        double stagger = delays[1] - delays[0];
        bool rising = Enumerable.Range(1, max - 1).All(i => delays[i] > delays[i - 1]);
        bool capped = delays.Skip(max - 1).All(d => Math.Abs(d - delays[max - 1]) < 1e-9);
        var back = Enumerable.Range(0, 6).Select(i => Cascade.DelayFor(i, 6, reverse: true)).ToArray();
        double backStagger = back[0] - back[1];
        Check(lines, stagger >= 0.025 && stagger <= 0.040 && rising && capped && back[5] == 0 && backStagger > 0 && backStagger < stagger,
            $"cascade : {stagger * 1000:0} ms entre deux éléments (25–40), plafonnée à {max} éléments, fermeture inverse en {backStagger * 1000:0} ms");

        // Courbes : chaque ressort du mouvement arrive EXACTEMENT à sa cible, puis n'en bouge plus (pas de dérive).
        var specs = new (string Name, SpringSpec Spec)[]
        {
            ("cascade", Springs.CascadeIn), ("cascade (fermeture)", Springs.CascadeOut), ("largeur", Springs.MorphWidth),
            ("hauteur", Springs.MorphHeight), ("coins", Springs.MorphRadius), ("page", Springs.Page), ("page qui part", Springs.PageExit),
            ("inclinaison", Springs.Tilt), ("appui", Springs.PressDown), ("rebond", Springs.PressRelease), ("élastique", Springs.Elastic),
            ("outil", Springs.MorphOpen), ("animations réduites", Springs.Calm),
        };
        var failed = new List<string>();
        double worstSettle = 0;
        foreach (var (name, spec) in specs)
        {
            var (settle, peak) = Simulate(spec, 0, 100);
            var spring = new Spring(0);
            spring.SetTarget(100, spec.Stiffness, spec.Damping);
            for (int i = 0; i < 600; i++) spring.Step(1 / 60.0);
            for (int i = 0; i < 600; i++) spring.Step(1 / 60.0); // 10 s de plus : rien ne doit bouger
            if (double.IsNaN(settle) || spring.Value != 100 || !spring.IsSettled) failed.Add(name);
            worstSettle = Math.Max(worstSettle, double.IsNaN(settle) ? 99 : settle);
        }
        var start = Cascade.PoseAt(0);
        var end = Cascade.PoseAt(1);
        var (_, cascadePeak) = Simulate(Springs.CascadeIn, 0, 100);
        double peakScale = Cascade.PoseAt(cascadePeak / 100).Scale;
        var (fromPicture, fromGhost) = MorphOpen.FadesAt(0);
        var (toPicture, toGhost) = MorphOpen.FadesAt(1);
        Check(lines, failed.Count == 0 && worstSettle < 2.5
                     && end == new Cascade.Pose(1, 0, 0, 1) && start.Scale == Kinetics.CascadeScale && start.Blur == Kinetics.CascadeBlur && start.Opacity == 0
                     && peakScale <= 1.02 && fromPicture == 1 && fromGhost == 1 && toPicture == 0 && toGhost == 0,
            failed.Count == 0
                ? $"courbes : {specs.Length} ressorts finissent exactement à leur cible (le plus lent en {worstSettle:0.00} s) et n'en bougent plus ; cascade 0,94 → 1 (au plus {peakScale:0.000}), flou 6 → 0 ; outil : fantôme = tuile au départ, effacé à l'arrivée"
                : "courbes qui ne se posent pas : " + string.Join(", ", failed));

        // Inclinaison : jamais plus de 2,5°, même pour une souris hors de la tuile ou une valeur absurde ; rien au centre.
        double worstTilt = 0;
        for (double nx = -3; nx <= 3; nx += 0.25)
            for (double ny = -3; ny <= 3; ny += 0.25)
            {
                var (ax, ay) = TileMotion.TiltFor(nx, ny);
                worstTilt = Math.Max(worstTilt, Math.Max(Math.Abs(ax), Math.Abs(ay)));
            }
        var nan = TileMotion.TiltFor(double.NaN, 0.5);
        Check(lines, worstTilt <= Kinetics.TiltMax + 1e-9 && TileMotion.TiltFor(0, 0) == (0, 0) && nan == (0, 0) && TileMotion.TiltFor(1, 0).AngleX != 0,
            $"inclinaison d'une tuile bornée : au plus {worstTilt:0.0}° (≤ {Kinetics.TiltMax}°), nulle au centre");

        // Élastique : suit d'abord le geste, cède de moins en moins, ne dépasse jamais 14 DIP ; un coup de molette énorme revient à 0.
        double limit = Kinetics.ElasticMax;
        var samples = new[] { 0.5, 1, 5, 14, 50, 500, 1e6 };
        bool bounded = samples.All(x => Math.Abs(Elastic.Rubber(x, limit)) < limit && Elastic.Rubber(-x, limit) == -Elastic.Rubber(x, limit));
        bool monotone = samples.Zip(samples.Skip(1)).All(p => Elastic.Rubber(p.Second, limit) > Elastic.Rubber(p.First, limit));
        var elastic = new Spring(0);
        elastic.SetTarget(0, Springs.Elastic.Stiffness, Springs.Elastic.Damping);
        elastic.Impulse(20 * Kinetics.ElasticGain * limit * 10); // 20 crans d'un coup
        double shownPeak = 0;
        for (int i = 0; i < 600 && !elastic.Step(1 / 60.0); i++) shownPeak = Math.Max(shownPeak, Math.Abs(Elastic.Rubber(elastic.Value, limit)));
        Check(lines, bounded && monotone && Elastic.Rubber(0, limit) == 0 && Elastic.Rubber(1, limit) > 0.9 && shownPeak < limit && elastic.Value == 0,
            $"élastique borné : au plus {shownPeak:0.0} DIP pour 20 crans (< {limit}), revient à 0");

        // Accessibilité : « comme Windows » par défaut, enregistré par son nom ; ce qui s'applique = le choix, sinon Windows.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var fresh = new AppearanceSettings();
        var chosen = new AppearanceSettings { ReduceMotion = AccessibilityChoice.On, ReduceTransparency = AccessibilityChoice.Off };
        var reread = JsonSerializer.Deserialize<AppearanceSettings>(JsonSerializer.Serialize(chosen, options), options)!;
        var old = JsonSerializer.Deserialize<AppearanceSettings>("{\"OpalStyle\":\"Intense\"}", options)!;
        bool resolve = MotionPrefs.Resolve(AccessibilityChoice.Windows, true) && !MotionPrefs.Resolve(AccessibilityChoice.Windows, false)
                       && MotionPrefs.Resolve(AccessibilityChoice.On, false) && !MotionPrefs.Resolve(AccessibilityChoice.Off, true);
        var current = SettingsStore.Current.Appearance;
        bool animations = MotionPrefs.WindowsAnimations, transparency = MotionPrefs.WindowsTransparency; // lus, jamais écrits
        bool followsMotion = current.ReduceMotion != AccessibilityChoice.Windows || MotionPrefs.ReduceMotion == !animations;
        bool followsTransparency = current.ReduceTransparency != AccessibilityChoice.Windows || MotionPrefs.ReduceTransparency == !transparency;
        Check(lines, fresh.ReduceMotion == AccessibilityChoice.Windows && fresh.ReduceTransparency == AccessibilityChoice.Windows
                     && reread.ReduceMotion == AccessibilityChoice.On && reread.ReduceTransparency == AccessibilityChoice.Off
                     && old.ReduceMotion == AccessibilityChoice.Windows && resolve && followsMotion && followsTransparency,
            $"accessibilité : « comme Windows » par défaut (Windows ici : animations {(animations ? "activées" : "coupées")}, transparence {(transparency ? "activée" : "coupée")} → réduire les animations {(MotionPrefs.ReduceMotion ? "oui" : "non")}, la transparence {(MotionPrefs.ReduceTransparency ? "oui" : "non")})");

        // Animations réduites : ressort court et sans rebond (≈ fondu de forme), bien plus court que l'ouverture normale.
        var (calmSettle, calmPeak) = Simulate(Springs.Calm, 0, 100);
        var (openSettle, openPeak) = Simulate(Springs.Open, 0, 100);
        bool tames = MotionPrefs.Tame(Springs.Open) == (MotionPrefs.ReduceMotion ? Springs.Calm : Springs.Open);
        Check(lines, calmSettle < 0.35 && calmPeak <= 100 + 1e-9 && openPeak > 100 && calmSettle < openSettle && tames,
            $"animations réduites : {calmSettle * 1000:0} ms sans rebond (au lieu de {openSettle * 1000:0} ms avec un dépassement de {openPeak - 100:0.0} %)");
    }

    /// <summary>Temps pour se poser (NaN si jamais en 10 s) et valeur la plus haute atteinte.</summary>
    private static (double Settle, double Peak) Simulate(SpringSpec spec, double from, double to)
    {
        var spring = new Spring(from);
        spring.SetTarget(to, spec.Stiffness, spec.Damping);
        double peak = from;
        for (int i = 1; i <= 600; i++)
        {
            bool settled = spring.Step(1 / 60.0);
            peak = Math.Max(peak, spring.Value);
            if (settled) return (i / 60.0, peak);
        }
        return (double.NaN, peak);
    }
}
