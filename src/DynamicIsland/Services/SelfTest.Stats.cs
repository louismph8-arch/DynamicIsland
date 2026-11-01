using System.IO;
using DynamicIsland.Services.Stats;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>Auto-test de « Ma semaine » (7F) : calculs purs + aller-retour du fichier dans un dossier temporaire (jamais le vrai).</summary>
internal static partial class SelfTest
{
    private static void Stats(List<string> lines)
    {
        Check(lines, !new AppSettings().Stats.Enabled, "suivi de l'activité désactivé par défaut");

        // Coupure à minuit : 23:30 → 00:45 = 30 min la veille, 45 min le lendemain.
        var log = new UsageLog();
        log.Record(new DateTime(2026, 10, 27, 23, 30, 0), new DateTime(2026, 10, 28, 0, 45, 0), null, "Chrome");
        Check(lines, log.Find(new DateOnly(2026, 10, 27))?.ScreenSeconds == 1800 && log.Find(new DateOnly(2026, 10, 28))?.ScreenSeconds == 2700
                     && log.Find(new DateOnly(2026, 10, 28))?.Apps["Chrome"] == 2700, "intervalle coupé à minuit (30 min + 45 min)");

        // Semaine du lundi au dimanche.
        Check(lines, UsageLog.WeekStart(new DateOnly(2026, 10, 31)) == new DateOnly(2026, 10, 26) && UsageLog.WeekStart(new DateOnly(2026, 10, 26)) == new DateOnly(2026, 10, 26)
                     && UsageLog.WeekStart(new DateOnly(2026, 11, 1)) == new DateOnly(2026, 10, 26), "semaine = lundi → dimanche");

        // Agrégation : jeux comptés dans le temps d'écran, applis et jeux classés, Pomodoros.
        var week = new UsageLog();
        var monday = new DateTime(2026, 10, 26, 10, 0, 0);
        week.Record(monday, monday.AddHours(2), null, "Code");
        week.Record(monday.AddHours(3), monday.AddHours(4), "Valorant", null);
        week.Record(monday.AddDays(2), monday.AddDays(2).AddHours(1), null, "Chrome");
        week.Record(monday.AddDays(2).AddHours(2), monday.AddDays(2).AddHours(2.5), null, "Code");
        foreach (var (game, hours) in new[] { ("A", 1.0), ("B", 2.0), ("C", 3.0), ("D", 0.5) })
            week.Record(monday.AddDays(5), monday.AddDays(5).AddHours(hours), game, null);
        week.AddPomodoro(monday);
        week.AddPomodoro(monday.AddDays(6));
        week.AddPomodoro(monday.AddDays(7)); // lundi suivant : pas dans cette semaine
        var summary = week.Week(new DateOnly(2026, 10, 29));
        Check(lines, summary.Days.Count == 7 && summary.Start == new DateOnly(2026, 10, 26) && summary.ScreenSeconds == 11 * 3600
                     && summary.GameSeconds == (int)(7.5 * 3600) && summary.Pomodoros == 2,
            $"semaine : {summary.ScreenSeconds / 3600.0} h d'écran dont {summary.GameSeconds / 3600.0} h de jeu, {summary.Pomodoros} Pomodoros");
        Check(lines, summary.TopApps.Count == 2 && summary.TopApps[0].Key == "Code" && summary.TopApps[0].Value == 9000
                     && summary.TopGames.Count == 3 && string.Join(",", summary.TopGames.Select(g => g.Key)) == "C,B,A",
            "classement : applis (Code d'abord), 3 jeux au plus (C, B, A)");
        Check(lines, summary.Days[0].GameSeconds == 3600 && summary.Days[0].ScreenSeconds == 3 * 3600 && summary.Days[1].ScreenSeconds == 0,
            "jour par jour (lundi : 3 h dont 1 h de jeu, mardi vide)");

        var partial = week.Week(new DateOnly(2026, 10, 26), 3); // lundi → mercredi seulement
        Check(lines, partial.ScreenSeconds == (int)(4.5 * 3600) && partial.Days[5].ScreenSeconds == 0, "semaine partielle (lundi → mercredi) pour comparer une semaine en cours");

        // Comparaison avec la semaine d'avant.
        Check(lines, UsageLog.Change(120, 100) is { } up && Math.Abs(up - 20) < 0.001 && UsageLog.Change(50, 100) is { } down && Math.Abs(down + 50) < 0.001
                     && UsageLog.Change(10, 0) == null, "comparaison : +20 %, −50 %, rien à comparer si la semaine d'avant est vide");
        Check(lines, SettingsWindow.WeekChangeText(12.4).Contains("+12") && SettingsWindow.WeekChangeText(-8).Contains("−8"),
            $"texte d'évolution : « {SettingsWindow.WeekChangeText(12.4)} »");

        // 90 jours gardés.
        var old = new UsageLog();
        var today = new DateOnly(2026, 10, 31);
        for (int i = 0; i < 120; i++) old.Record(today.AddDays(-i).ToDateTime(new TimeOnly(12, 0)), today.AddDays(-i).ToDateTime(new TimeOnly(12, 10)), null, null);
        old.Prune(today);
        Check(lines, old.Days.Count == UsageLog.KeepDays && old.Days[0].Day == today.AddDays(-(UsageLog.KeepDays - 1)) && old.Days[^1].Day == today,
            $"{UsageLog.KeepDays} jours gardés (le plus ancien : {old.Days[0].Date})");

        // Applis limitées par jour.
        var many = new UsageLog();
        for (int i = 0; i < UsageLog.MaxAppsPerDay + 10; i++) many.Record(monday.AddMinutes(i * 2), monday.AddMinutes(i * 2 + 1 + i % 3), null, $"Appli {i}");
        Check(lines, many.Days.Single().Apps.Count == UsageLog.MaxAppsPerDay, $"au plus {UsageLog.MaxAppsPerDay} applis par jour");

        // Inactivité : au-delà de 5 min sans entrée, le temps ne compte plus.
        var minute = TimeSpan.FromMinutes(1);
        Check(lines, UsageStats.ActivePart(minute, TimeSpan.FromMinutes(2)) == minute && UsageStats.ActivePart(minute, TimeSpan.FromSeconds(330)) == TimeSpan.FromSeconds(30)
                     && UsageStats.ActivePart(minute, TimeSpan.FromMinutes(10)) == TimeSpan.Zero, "inactif après 5 min (1 min, 30 s, 0)");

        // Durées et résumé du lundi.
        Check(lines, UsageStats.Format(3 * 3600 + 300).Contains("05") && UsageStats.Format(45 * 60).Contains("45") && UsageStats.Format(20) == Loc.T("week.duration.less")
                     && UsageStats.Format(32 * 3600) == Loc.F("week.duration.h", 32),
            $"durées : {UsageStats.Format(3 * 3600 + 300)}, {UsageStats.Format(45 * 60)}, {UsageStats.Format(32 * 3600)}");
        lines.Add("Résumé du lundi : " + UsageStats.SummaryLine(summary));

        // Aller-retour du fichier (dossier temporaire), fichier abîmé mis de côté.
        var folder = Path.Combine(Path.GetTempPath(), "opaline-selftest-stats-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "statistiques.json");
            week.Save(path);
            var back = UsageLog.Load(path);
            Check(lines, back.ToJson() == week.ToJson() && !File.Exists(path + ".tmp"), $"aller-retour de statistiques.json ({new FileInfo(path).Length} octets)");
            File.WriteAllText(path, "{ pas du json");
            var broken = UsageLog.Load(path);
            Check(lines, broken.Days.Count == 0 && File.Exists(path + ".illisible"), "fichier illisible : mis de côté, historique vide");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }

        // Rapport de bug : aucun nom d'appli ni de jeu (les statistiques ne sont pas dans les réglages).
        var cleaned = BugReport.CleanSettings(SettingsStore.ToJson());
        Check(lines, !cleaned.Contains("Valorant") && !cleaned.Contains("ScreenSeconds") && !cleaned.Contains("\"Apps\""),
            "rapport de bug : ni applis ni jeux suivis");
    }
}
