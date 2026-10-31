using System.Text.Json;
using DynamicIsland.Feedback;
using DynamicIsland.Services.Friends;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la bêta (7C) : numéros de version avec pré-version, choix de la release selon le canal, notes de « Nouveautés »
/// embarquées, et (si OPALINE_AMIS_ESSAI donne un serveur d'essai, faux webhook de test-local.mjs) un avis envoyé de bout en bout.
/// </summary>
internal static partial class SelfTest
{
    private static void Beta(List<string> lines)
    {
        static ReleaseVersion V(string tag) => UpdateService.TryParseRelease(tag, out var version) ? version : default;
        Check(lines, V("v1.24.0-beta.1") < V("v1.24.0-beta.2") && V("1.24.0-beta.2") < V("v1.24.0") && V("v1.23.0") < V("1.24.0-beta.1")
                     && V("v1.24.0+abc") == V("1.24.0") && V("1.24.0-rc1").Beta == 1 && V("1.24.0-beta").Beta == 0 && !UpdateService.TryParseRelease("nimporte", out _)
                     && V("v1.24.0-beta.3").ToString() == "1.24.0-beta.3" && V("1.24.0").ToString() == "1.24.0",
            "versions : 1.23.0 < 1.24.0-beta.1 < 1.24.0-beta.2 < 1.24.0 (métadonnées « +… » ignorées)");

        using var releases = JsonDocument.Parse("""
            [ { "tag_name": "v1.25.0-beta.1", "draft": true },
              { "tag_name": "v1.24.0-beta.2", "prerelease": true },
              { "tag_name": "v1.23.0" },
              { "tag_name": "v1.24.0-beta.1", "prerelease": true },
              { "tag_name": "pas-une-version" } ]
            """);
        var newest = UpdateService.PickNewest(releases.RootElement)?.GetProperty("tag_name").GetString();
        using var withFinal = JsonDocument.Parse("""[ { "tag_name": "v1.24.0-beta.2", "prerelease": true }, { "tag_name": "v1.24.0" } ]""");
        var final = UpdateService.PickNewest(withFinal.RootElement)?.GetProperty("tag_name").GetString();
        Check(lines, newest == "v1.24.0-beta.2" && final == "v1.24.0",
            "canal Bêta : la plus récente, pré-versions comprises, sans brouillon ; la version finale passe devant sa bêta");
        Check(lines, true, $"version qui tourne : {UpdateService.CurrentText} (bêta : {(UpdateService.Current.IsBeta ? "oui" : "non")})");

        var resources = typeof(WhatsNewWindow).Assembly.GetManifestResourceNames();
        Check(lines, resources.Contains("DynamicIsland.WhatsNew.fr.md") && resources.Contains("DynamicIsland.WhatsNew.en.md"),
            "Nouveautés : notes françaises ET anglaises dans l'exe (pas dans une assembly satellite « en »)");
        var notes = WhatsNewWindow.Notes();
        var (title, sections) = WhatsNewWindow.Parse(notes ?? "");
        Check(lines, notes != null && title.Length > 0 && sections.Count >= 2 && sections.All(s => s.Lines.Count > 0)
                     && !sections.SelectMany(s => s.Lines).Any(l => l.Contains("Opaline-Setup.exe")),
            $"Nouveautés : notes de la {UpdateService.CurrentText} embarquées (« {title} », {sections.Count} sections, sans la ligne de téléchargement)");
        var (sampleTitle, sample) = WhatsNewWindow.Parse("## Opaline 9.9.9 — Titre\nTélécharge Opaline-Setup.exe ci-dessous.\n\n### 🧪 Essai\n- **gras** et `code`\n  suite de la puce\nUn paragraphe.\n### Vide\n");
        Check(lines, sampleTitle == "Titre" && sample.Count == 1 && sample[0].Lines.Count == 2 && sample[0].Lines[0] == "**gras** et `code` suite de la puce",
            "Nouveautés : titre après le tiret, puces sur deux lignes recollées, sections vides ignorées");

        var server = Environment.GetEnvironmentVariable("OPALINE_AMIS_ESSAI");
        if (string.IsNullOrEmpty(server))
        {
            lines.Add("(avis de bout en bout sauté : pas de serveur d'essai)");
            return;
        }
        var info = FriendsApi.InfoAsync(server).GetAwaiter().GetResult();
        var tester = FriendsApi.RegisterAsync(server, "Testeur (essai)").GetAwaiter().GetResult();
        var report = BugReport.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Opaline-avis-essai"));
        var sent = FriendsApi.SendFeedbackAsync(tester, bug: true, "Avis d'essai de l'auto-test", UpdateService.CurrentText, [0xFF, 0xD8, 0xFF, 0xD9], report)
            .GetAwaiter().GetResult();
        var empty = FriendsApi.SendFeedbackAsync(tester, bug: false, "   ", UpdateService.CurrentText, null, null).GetAwaiter().GetResult();
        FriendsApi.ForgetAsync(tester).GetAwaiter().GetResult();
        System.IO.File.Delete(report);
        Check(lines, info is { Ok: true, Feedback: true } && sent == null && empty == "empty",
            $"avis de bout en bout sur {server} : envoyé avec capture et rapport (faux webhook), message vide refusé");
    }
}
