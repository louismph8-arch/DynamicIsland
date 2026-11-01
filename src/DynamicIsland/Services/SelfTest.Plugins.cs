using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Plugins;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des widgets des potes (7H) : fichiers valables et piégés (taille, « .. » dans le zip, profondeur, http, site non
/// autorisé…), gabarits remplis avec des données d'exemple, chemins JSON, et AUCUNE requête réseau (la source web passe par
/// une fausse réponse, et seulement quand elle est autorisée). Rien n'est installé : tout se passe en mémoire.
/// </summary>
internal static partial class SelfTest
{
    private static void PluginsPure(List<string> lines)
    {
        int requestsAtStart = WebSource.Requests;

        // Exemples embarqués (modèle commenté compris) : lus comme des fichiers reçus.
        foreach (var (resource, _) in PluginStore.TemplateFiles.Where(t => t.Resource.EndsWith(WidgetPackageReader.Extension, StringComparison.Ordinal)))
        {
            var bytes = PluginStore.TemplateBytes(resource);
            try
            {
                var package = WidgetPackageReader.Parse(bytes ?? []);
                Check(lines, true, $"exemple « {package.Definition.Id} » valable ({package.Definition.Elements().Count()} éléments, source web : {package.Definition.Source?.Host ?? "aucune"})");
            }
            catch (Exception ex)
            {
                Check(lines, false, $"exemple {resource} refusé : {ex.Message}");
            }
        }
        Check(lines, PluginStore.TemplateBytes("DynamicIsland.Widgets.doc.md") is { Length: > 1000 }, "doc des widgets embarquée (Créer un widget)");

        // Icônes par nom, lues dans la police.
        Check(lines, LucideNames.Count > 1000 && LucideNames.Glyph("bitcoin") == "" && LucideNames.Glyph("clock") == Themes.Icons.Clock
                     && LucideNames.Glyph("cpu") == "" && LucideNames.Glyph("pas-une-icone") == null,
            $"icônes Lucide par nom ({LucideNames.Count} noms, bitcoin, clock, cpu ; nom inconnu refusé)");

        // Fichiers piégés : chacun refusé avec le bon message, rien d'installé.
        Refused(lines, "fichier de plus de 5 Mo", new byte[WidgetPackageReader.MaxFileBytes + 1], "plugins.error.too_big");
        Refused(lines, "« .. » dans le zip", Zip(("widget.json", Manifest(Text("Salut"))), ("../evil.png", FakePng())), "plugins.error.bad_path");
        Refused(lines, "chemin absolu dans le zip", Zip(("widget.json", Manifest(Text("Salut"))), ("/C/x.png", FakePng())), "plugins.error.bad_path");
        Refused(lines, "sous-dossiers trop profonds dans le zip", Zip(("widget.json", Manifest(Text("Salut"))), ("a/b/c.png", FakePng())), "plugins.error.bad_path");
        Refused(lines, "script dans le zip", Zip(("widget.json", Manifest(Text("Salut"))), ("script.js", "alert(1)"u8.ToArray())), "plugins.error.bad_file");
        Refused(lines, "image de plus de 1 Mo (bombe zip)", Zip(("widget.json", Manifest(Text("Salut"))), ("gros.png", new byte[3 * 1024 * 1024])), "plugins.error.image_too_big");
        Refused(lines, "faux PNG", Zip(("widget.json", Manifest(Image("faux.png"))), ("faux.png", "pas une image"u8.ToArray())), "plugins.error.not_png");
        Refused(lines, "zip sans widget.json", Zip(("a.png", FakePng())), "plugins.error.no_manifest");
        Refused(lines, "pas du JSON", "<html>bonjour</html>"u8.ToArray(), "plugins.error.not_json");
        Refused(lines, "format inconnu", Bytes(Manifest(Text("x")).Replace("\"format\": 1", "\"format\": 2")), "plugins.error.format");
        Refused(lines, "identifiant dangereux (..)", Bytes(Manifest(Text("x"), id: "../../windows")), "plugins.error.id");
        Refused(lines, "5 dispositions imbriquées", Bytes(Manifest(Nest(5, Text("x")))), "plugins.error.depth");
        Refused(lines, "41 éléments", Bytes(Manifest($"{{\"type\":\"stack\",\"children\":[{string.Join(",", Enumerable.Repeat(Text("x"), 40))}]}}")), "plugins.error.too_many_elements");
        Refused(lines, "texte de 201 caractères", Bytes(Manifest(Text(new string('a', 201)))), "plugins.error.too_long");
        Refused(lines, "type d'élément inconnu (script)", Bytes(Manifest("{\"type\":\"script\",\"code\":\"x\"}")), "plugins.error.element");
        Refused(lines, "propriété inconnue (onClick)", Bytes(Manifest("{\"type\":\"text\",\"text\":\"x\",\"onClick\":\"calc.exe\"}")), "plugins.error.property");
        Refused(lines, "icône inconnue", Bytes(Manifest("{\"type\":\"icon\",\"name\":\"pas-une-icone\"}")), "plugins.error.icon");
        Refused(lines, "image absente du paquet", Bytes(Manifest(Image("absente.png"))), "plugins.error.no_image");
        Refused(lines, "jauge sur une donnée inconnue", Bytes(Manifest("{\"type\":\"ring\",\"value\":\"motdepasse\"}")), "plugins.error.number_key");
        Refused(lines, "couleur invalide", Bytes(Manifest("{\"type\":\"text\",\"text\":\"x\",\"color\":\"url(x)\"}")), "plugins.error.value");
        Refused(lines, "source web en http", Bytes(Manifest(Text("{web.p}"), source: Source("http://api.exemple.com/prix"))), "plugins.error.not_https");
        Refused(lines, "source web vers une adresse IP", Bytes(Manifest(Text("{web.p}"), source: Source("https://192.168.2.1/admin"))), "plugins.error.bad_url");
        Refused(lines, "source web vers localhost", Bytes(Manifest(Text("{web.p}"), source: Source("https://localhost/x"))), "plugins.error.bad_url");
        Refused(lines, "source web avec identifiants", Bytes(Manifest(Text("{web.p}"), source: Source("https://moi:secret@api.exemple.com/x"))), "plugins.error.bad_url");
        Refused(lines, "source web avec un port", Bytes(Manifest(Text("{web.p}"), source: Source("https://api.exemple.com:8443/x"))), "plugins.error.bad_url");
        Refused(lines, "chemin JSON invalide", Bytes(Manifest(Text("{web.p}"), source: Source("https://api.exemple.com/x", "$..prix"))), "plugins.error.path");

        // Valables : zip avec image, gabarit inconnu (affiché tel quel), source toutes les 1 min ramenée à 5.
        try
        {
            var zip = WidgetPackageReader.Parse(Zip(("widget.json", Manifest(Image("logo.png"))), ("logo.png", FakePng()), ("inutile.png", FakePng()), ("LISEZMOI.md", "x"u8.ToArray())));
            Check(lines, zip.Images.Count == 1 && zip.Images.ContainsKey("logo.png"), "zip avec image valable (image inutilisée et LISEZMOI ignorés)");
            var unknown = WidgetPackageReader.Parse(Bytes(Manifest(Text("{inconnu.truc} et {time:HH:mm}"))));
            Check(lines, unknown.Definition.Root.Text == "{inconnu.truc} et {time:HH:mm}", "gabarit inconnu accepté (il sera affiché tel quel)");
            var web = WidgetPackageReader.Parse(Bytes(Manifest(Text("{web.p}"), source: Source("https://api.exemple.com/prix?x=1", every: 1))));
            Check(lines, web.Definition.Source is { EveryMinutes: WidgetPackageReader.MinEveryMinutes, Host: "api.exemple.com" }, "source web : toutes les 5 min au plus (1 demandée)");
        }
        catch (Exception ex)
        {
            Check(lines, false, $"widget valable refusé : {ex.Message}");
        }

        // Gabarits remplis avec des données d'exemple.
        var sample = new SampleWidgetData { Now = new DateTime(2026, 10, 31, 14, 5, 9) };
        var source = new WebSourceDefinition { Url = new Uri("https://api.exemple.com/"), Fields = new Dictionary<string, string> { ["usd"] = "$.a", ["vide"] = "$.b" } };
        var data = new ScopedWidgetData(sample, new Dictionary<string, string> { ["usd"] = "67512.4" }, source);
        var filled = WidgetTemplate.Fill("{time:HH:mm:ss} {cpu} {weather.temp} {web.usd:n0} {web.vide} {web.absent} {inconnu} {time:yyyy'<b>'}", data);
        var expected = $"14:05:09 {Loc.F("format.percent", 23)} 6° {67512.4.ToString("n0", Loc.Culture)} — {{web.absent}} {{inconnu}} 14:05";
        Check(lines, filled == expected, $"gabarits remplis : « {filled} »" + (filled == expected ? "" : $" (attendu « {expected} »)"));
        Check(lines, data.Number("web.usd") == 67512.4 && data.Number("cpu") == 23 && data.Number("web.vide") == null, "valeurs des jauges (web.usd, cpu, champ sans valeur)");

        // Chemins JSON.
        using (var document = JsonDocument.Parse("{\"bitcoin\":{\"usd\":67512.4,\"nom\":\"BTC\"},\"items\":[{\"name\":\"a\"},{\"name\":\"b\"}],\"ok\":true,\"rien\":null}"))
        {
            var root = document.RootElement;
            bool paths = JsonPath.Extract(root, "$.bitcoin.usd") == "67512.4" && JsonPath.Extract(root, "$.items[1].name") == "b"
                         && JsonPath.Extract(root, "$.ok") == "true" && JsonPath.Extract(root, "$.bitcoin.nom") == "BTC"
                         && JsonPath.Extract(root, "$.rien") == null && JsonPath.Extract(root, "$.items[5].name") == null
                         && JsonPath.Extract(root, "$.bitcoin") == null && JsonPath.Extract(root, "$.absent.x") == null;
            bool invalid = !JsonPath.IsValid("$..usd") && !JsonPath.IsValid("$.items[*]") && !JsonPath.IsValid("bitcoin.usd") && !JsonPath.IsValid("$.a b");
            Check(lines, paths && invalid, "chemins JSON simples ($.a.b, $.liste[1].nom ; objets, absents et jokers refusés)");
        }

        // Source web : aucune requête sans l'accord pour CE site ; avec l'accord, une fausse réponse (rien ne sort du PC).
        var bitcoin = PluginStore.Examples().First(p => p.Definition.Source != null).Definition;
        var previous = WebSource.TestFetcher;
        int fetched = 0;
        WebSource.TestFetcher = (_, _) =>
        {
            fetched++;
            return Task.FromResult("{\"bitcoin\":{\"usd\":67512.4,\"eur\":62230.9,\"usd_24h_change\":2.37}}");
        };
        try
        {
            var none = WebSource.FetchAsync(bitcoin, null, CancellationToken.None).GetAwaiter().GetResult();
            var other = WebSource.FetchAsync(bitcoin, new PluginState { Id = bitcoin.Id, AllowedHost = "evil.example.com" }, CancellationToken.None).GetAwaiter().GetResult();
            var unset = WebSource.FetchAsync(bitcoin, new PluginState { Id = bitcoin.Id }, CancellationToken.None).GetAwaiter().GetResult();
            Check(lines, none == null && other == null && unset == null && fetched == 0 && WebSource.Requests == requestsAtStart,
                "aucune requête sans accord (pas de choix, autre site autorisé, accord retiré)");
            var allowed = WebSource.FetchAsync(bitcoin, new PluginState { Id = bitcoin.Id, AllowedHost = bitcoin.Source!.Host }, CancellationToken.None).GetAwaiter().GetResult();
            Check(lines, fetched == 1 && allowed is { } values && values["usd"] == "67512.4" && values["change"] == "2.37",
                $"avec l'accord pour {bitcoin.Source.Host} : champs lus dans la réponse (fausse réponse, pas de réseau)");
            bool tooBig;
            try
            {
                WebSource.Extract(bitcoin.Source, "{\"x\":\"" + new string('a', WebSource.MaxResponseBytes) + "\"}");
                tooBig = false;
            }
            catch (InvalidDataException)
            {
                tooBig = true;
            }
            Check(lines, tooBig, "réponse de plus de 64 Ko refusée");
        }
        finally
        {
            WebSource.TestFetcher = previous;
        }
        Check(lines, WebSource.Requests == requestsAtStart + 1, $"une seule requête (fausse) pendant tout le test ({WebSource.Requests - requestsAtStart})");

        var examples = PluginStore.Examples().ToList();
        Check(lines, !PluginService.NeedsFast(examples.First(e => e.Definition.Id == "horloge-meteo").Definition)
                     && PluginService.NeedsFast(WidgetPackageReader.Parse(PluginStore.TemplateBytes("DynamicIsland.Widgets.modele.opalinewidget")!).Definition),
            "mise à jour lente pour l'horloge (15 s), rapide pour les mesures (2 s)");
    }

    /// <summary>Rendu (thread de l'interface) : exemples dessinés avec les données d'exemple, widget cassé = case « indisponible ».</summary>
    private static void PluginsRender(List<string> lines)
    {
        foreach (var package in PluginStore.Examples())
        {
            var rendered = RenderedWidget.Render(package, PluginService.Sample(package.Definition));
            var texts = Texts(rendered.Root).ToList();
            bool filled = texts.Count > 0 && texts.All(t => !t.Contains('{'));
            Check(lines, !rendered.Failed && filled, $"rendu de « {package.Definition.Id} » : {string.Join(" | ", texts.Take(4))}");
        }
        var broken = WidgetPackageReader.Parse(Zip(("widget.json", Manifest(Image("logo.png"))), ("logo.png", FakePng())));
        RenderedWidget? result = null;
        Exception? escaped = null;
        try
        {
            result = RenderedWidget.Render(broken);
            result.Update(SampleWidgetData.Instance);
        }
        catch (Exception ex)
        {
            escaped = ex;
        }
        Check(lines, escaped == null && result is { Failed: true } && Texts(result.Root).Contains(Loc.T("plugins.unavailable")),
            "image illisible au rendu : case « Widget indisponible », aucune exception");
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock { Text.Length: > 0 } text && text.FontFamily != Themes.FontStack.Icons) yield return text.Text;
            foreach (var inner in Texts(child)) yield return inner;
        }
    }

    private static void Refused(List<string> lines, string what, byte[] bytes, string key)
    {
        try
        {
            WidgetPackageReader.Parse(bytes);
            Check(lines, false, $"piège « {what} » ACCEPTÉ");
        }
        catch (WidgetPackageException ex)
        {
            Check(lines, ex.Key == key, $"piège « {what} » refusé : {ex.Message}" + (ex.Key == key ? "" : $" (attendu {key})"));
        }
        catch (Exception ex)
        {
            Check(lines, false, $"piège « {what} » : erreur inattendue {ex.GetType().Name} ({ex.Message})");
        }
    }

    private static string Text(string text) => $"{{\"type\":\"text\",\"text\":{JsonSerializer.Serialize(text)}}}";

    private static string Image(string src) => $"{{\"type\":\"image\",\"src\":{JsonSerializer.Serialize(src)},\"width\":32,\"height\":32}}";

    private static string Nest(int levels, string inner) =>
        levels == 0 ? inner : $"{{\"type\":\"stack\",\"children\":[{Nest(levels - 1, inner)}]}}";

    private static string Source(string url, string path = "$.prix", int every = 10) =>
        $"{{\"url\":{JsonSerializer.Serialize(url)},\"every\":{every},\"fields\":{{\"p\":{JsonSerializer.Serialize(path)}}}}}";

    private static string Manifest(string root, string id = "essai", string? source = null) =>
        $"{{\"format\": 1, \"id\": {JsonSerializer.Serialize(id)}, \"name\": \"Essai\", \"size\": {{\"width\": 200, \"height\": 100}}"
        + (source != null ? $", \"source\": {source}" : "") + $", \"root\": {root}}}";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>En-tête PNG valable (signature + IHDR 16 × 16) sans vraie image : passe la lecture, échoue au décodage.</summary>
    private static byte[] FakePng()
    {
        var png = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 16, 0, 0, 0, 16, 8, 6, 0, 0, 0 }
            .CopyTo(png, 0);
        return png;
    }

    private static byte[] Zip(params (string Name, object Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                var bytes = content as byte[] ?? Bytes((string)content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return memory.ToArray();
    }
}
