using System.Windows;
using DynamicIsland.Live;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

internal static partial class SelfTest
{
    /// <summary>
    /// Île réduite (refonte O5) : choix de l'activité principale, compteur « +1 », largeur de la pilule, respiration bornée et
    /// coupée quand il faut, batterie cachée sans batterie, étapes de Windows Update, transferts. Calculs purs (+ le registre
    /// des activités sur le fil de l'interface, avec des identifiants de test retirés à la fin).
    /// </summary>
    private static void LiveO5(List<string> lines)
    {
        var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        LiveActivity A(string id, int priority, int secondsAgo = 0) =>
            new(id, Icons.Timer, 0.5, "1", null, priority) { Since = t0.AddSeconds(-secondsAgo) };

        // 1. Priorités et sélection.
        var music = A(LiveIds.Music, LivePriority.Music) with { Style = LiveStyle.Music, Breathes = false };
        var timer = A(LiveIds.Timer, LivePriority.Timer);
        var transfer = A(LiveIds.Transfer, LivePriority.Transfer);
        var (main, others) = LivePill.Select([music, transfer, timer]);
        Check(lines, main?.Id == LiveIds.Timer && others == 2, $"minuteur devant le transfert et la musique : {main?.Id}, autres {others}");
        (main, others) = LivePill.Select([music]);
        Check(lines, main?.Id == LiveIds.Music && others == 0, "musique seule : principale, pas de « +1 »");
        (main, others) = LivePill.Select([]);
        Check(lines, main == null && others == 0, "aucune activité : pilule au repos");
        var early = A("a", 50, secondsAgo: 30);
        var late = A("b", 50, secondsAgo: 5);
        Check(lines, LivePill.Select([late, early]).Main?.Id == "a" && LivePill.Select([early, late]).Main?.Id == "a",
            "à priorité égale, la plus ancienne reste devant (pas de saut d'une activité à l'autre)");
        var alert = A(LiveIds.Timer, LivePriority.Alert);
        var recording = LiveActivity.Recording(TimeSpan.FromSeconds(75));
        Check(lines, LivePill.Select([recording, alert]).Main == alert && LivePill.Select([recording, timer, transfer]).Main?.Id == LiveIds.Recording
                     && recording.Value == "01:15" && recording.Progress == null,
            $"minuteur fini > enregistrement (O8, « {recording.Value} ») > minuteur > transfert");
        int[] order = [LivePriority.Alert, LivePriority.Recording, LivePriority.Timer, LivePriority.Transfer, LivePriority.Update, LivePriority.Assistant, LivePriority.Voice, LivePriority.Music];
        Check(lines, order.Zip(order.Skip(1)).All(p => p.First > p.Second), "ordre des priorités : alerte, enregistrement, minuteur, transfert, Windows Update, assistant, vocal, musique");

        // 2. Compteur « +1 ».
        Check(lines, LivePill.MoreText(0) == "" && LivePill.MoreText(1) == "+1" && LivePill.MoreText(3) == "+3" && LivePill.MoreText(14) == "+9",
            "compteur : rien, « +1 », « +3 », plafonné à « +9 »");

        // 3. Largeur de la pilule.
        double rest = LivePill.Width(40, 0, 0);
        double ring = LivePill.Width(40, IslandShape.LiveRing, 38);
        Check(lines, rest == Math.Max(IslandShape.CompactMin, 40 + 2 * IslandShape.PillRestPad) && rest < IslandShape.Compact.Width,
            $"repos : l'heure seule = {rest} DIP (avant 124)");
        Check(lines, ring > rest && ring == LivePill.Width(40, 38, IslandShape.LiveRing),
            $"activité : {ring} DIP, l'heure reste au centre (même largeur quel que soit le bord le plus large)");
        Check(lines, LivePill.Width(1, 0, 0) == IslandShape.CompactMin && LivePill.Width(400, 200, 200) == IslandShape.CompactMax,
            $"bornes : jamais moins de {IslandShape.CompactMin}, jamais plus de {IslandShape.CompactMax}");
        Check(lines, LivePill.Width(40, 20, 38 + 22) > ring, "« +1 » en plus : la pilule s'élargit");

        // 4. Respiration bornée, périodique, sans saut au départ.
        var (a0, g0) = LiveBreath.At(0);
        bool bounded = true;
        double minGlow = 1, maxAngle = 0;
        for (double s = 0; s <= 9; s += 0.05)
        {
            var (angle, glow) = LiveBreath.At(s);
            bounded &= Math.Abs(angle) <= Opal.BreathSwing + 1e-9 && glow >= Opal.BreathLow - 1e-9 && glow <= 1 + 1e-9;
            minGlow = Math.Min(minGlow, glow);
            maxAngle = Math.Max(maxAngle, Math.Abs(angle));
        }
        var period = LiveBreath.At(Opal.BreathPeriod.TotalSeconds);
        Check(lines, bounded && Math.Abs(a0) < 1e-9 && Math.Abs(g0 - 1) < 1e-9 && Math.Abs(period.Angle) < 1e-6 && Math.Abs(period.Glow - 1) < 1e-6,
            $"respiration : angle ≤ ±{maxAngle:0.0}°, éclat {minGlow:0.00}–1, période {Opal.BreathPeriod.TotalSeconds:0} s, part sans saut");
        Check(lines, Opal.BreathFrame.TotalMilliseconds >= 50, $"cadence de la respiration {1000 / Opal.BreathFrame.TotalMilliseconds:0} images/s (≤ 20)");
        bool runs = LiveBreath.ShouldRun(true, false, false, true, false, true, false);
        Check(lines, runs
                     && !LiveBreath.ShouldRun(false, false, false, true, false, true, false)   // au repos / musique
                     && !LiveBreath.ShouldRun(true, true, false, true, false, true, false)     // réduire les animations
                     && !LiveBreath.ShouldRun(true, false, true, true, false, true, false)     // mode Gaming
                     && !LiveBreath.ShouldRun(true, false, false, false, false, true, false)   // verre coupé
                     && !LiveBreath.ShouldRun(true, false, false, true, true, true, false)     // île ouverte
                     && !LiveBreath.ShouldRun(true, false, false, true, false, false, false)   // île cachée
                     && !LiveBreath.ShouldRun(true, false, false, true, false, true, true),    // alerte orange devant
            "respiration seulement pendant une activité ; coupée au repos, en animations réduites, en jeu, verre coupé, île ouverte / cachée, alerte");
        Check(lines, !music.Breathes && timer.Breathes, "la musique ne fait pas respirer la bordure (elle dure des heures)");

        // 5. Batterie : jamais sur un PC sans batterie.
        Check(lines, LivePill.Battery(128, 255, 1) == null && LivePill.Battery(255, 255, 255) == null,
            "pas de batterie (128) ou état inconnu (255) : rien dans la pilule");
        Check(lines, LivePill.Battery(1, 80, 0) is { Percent: 80 } full && full.Icon == Icons.BatteryFull
                     && LivePill.Battery(8, 42, 1) is { Icon: var charging } && charging == Icons.BatteryCharging
                     && LivePill.Battery(2, 12, 0)?.Icon == Icons.BatteryLow && LivePill.Battery(0, 255, 0)?.Percent == 0,
            "portable : 80 % pleine, en charge, faible, pourcentage inconnu = 0");
        var power = BatteryStatus.Read();
        lines.Add($"Ce PC : {(power is { } p ? $"BatteryFlag {p.Flag} → {(LivePill.Battery(p.Flag, p.Percent, p.AcLine) is { } b ? $"batterie {b.Percent} %" : "pas de batterie affichée")}" : "état illisible")}");

        // 6. Windows Update : étapes → progression.
        var steps = new[] { ("download|1/4", 0.0, "1/4"), ("download|3/4", 0.25, "3/4"), ("install|1/4", 0.5, "1/4"), ("install|4/4", 0.875, "4/4") };
        bool parsed = steps.All(s => WindowsUpdateInstall.Parse(s.Item1) is var r && r.Progress is double v && Math.Abs(v - s.Item2) < 1e-9 && r.Value == s.Item3);
        Check(lines, parsed && WindowsUpdateInstall.Parse("restart").Progress == 1 && WindowsUpdateInstall.Parse("rien").Progress == null
                     && WindowsUpdateInstall.Parse("install|x/0").Progress == null,
            "Windows Update : téléchargement = 1re moitié, installation = 2e moitié, « restart » = fini, ligne illisible = indéterminée");

        // 7. Transferts téléphone ↔ PC.
        RemoteTransfersTest(lines);

        // 8. Registre des activités (fil de l'interface) : une mise à jour identique ne redessine rien.
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.Invoke(() =>
        {
            int raised = 0;
            void Count() => raised++;
            LiveActivities.Changed += Count;
            try
            {
                var test = new LiveActivity("test-o5", Icons.Timer, 0.25, "05:00", null, 1);
                LiveActivities.Set(test);
                var since = LiveActivities.All.First(a => a.Id == "test-o5").Since;
                LiveActivities.Set(test with { Since = DateTime.UtcNow.AddHours(1) }); // même contenu : rien
                LiveActivities.Set(test with { Value = "04:59" });
                bool keptSince = LiveActivities.All.First(a => a.Id == "test-o5").Since == since;
                LiveActivities.Remove("test-o5");
                LiveActivities.Remove("test-o5");
                Check(lines, raised == 3 && keptSince, $"registre : ajout, mise à jour, retrait = {raised} redessins (3 attendus), début gardé");
            }
            finally
            {
                LiveActivities.Changed -= Count;
                LiveActivities.Remove("test-o5");
            }
        });
    }

    private static void RemoteTransfersTest(List<string> lines)
    {
        Remote.RemoteTransfers.Clear();
        Remote.RemoteTransfers.Report("small", true, 10, 1000); // trop petit : pas d'activité
        Check(lines, Remote.RemoteTransfers.Snapshot().Count == 0, "petit fichier (< 1 Mo) : pas d'activité qui clignote");
        long mb = 1024 * 1024;
        Remote.RemoteTransfers.Report("in:a", true, 4 * mb, 8 * mb);
        Remote.RemoteTransfers.Report("out:b", false, 1 * mb, 2 * mb);
        var overall = Remote.RemoteTransfers.Overall(Remote.RemoteTransfers.Snapshot());
        Check(lines, overall is { } o && Math.Abs(o.Progress - 0.5) < 1e-9 && o.Incoming,
            $"deux transferts : avancement global {(overall?.Progress ?? -1) * 100:0} %, sens du plus gros (reçu)");
        Remote.RemoteTransfers.Finish("in:a");
        Remote.RemoteTransfers.Finish("out:b");
        Check(lines, Remote.RemoteTransfers.Snapshot().Count == 0 && Remote.RemoteTransfers.Overall([]) == null, "transferts finis : l'activité disparaît");
        Remote.RemoteTransfers.Clear();
    }
}
