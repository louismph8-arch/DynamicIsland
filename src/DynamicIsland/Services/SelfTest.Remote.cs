using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using DynamicIsland.Services.Remote;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la télécommande (6D) : QR code, réseaux, et un vrai aller-retour HTTP sur 127.0.0.1 SEULEMENT (port choisi
/// par Windows, faux état : rien n'est verrouillé ni changé). Le pare-feu est seulement LU, jamais modifié.
/// </summary>
internal static partial class SelfTest
{
    private static void RemoteOffline(List<string> lines)
    {
        // Valeurs de la norme (ISO/IEC 18004, exemple « HELLO WORLD » en 1-M ; tables des bits de format et de version).
        byte[] hello = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];
        Check(lines, QrCode.ReedSolomonRemainder(hello, 10).SequenceEqual(new byte[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }),
            "QR : correction d'erreurs Reed-Solomon (exemple de la norme)");
        Check(lines, QrCode.FormatBits(1, 4) == 0b110011000101111 && QrCode.FormatBits(0, 5) == 0b100000011001110
                     && QrCode.VersionBits(7) == 0b000111110010010100,
            "QR : bits de format et de version (tables de la norme)");
        var link = QrCode.Encode("http://192.168.1.20:48765/#p=482913");
        Check(lines, link is { Version: 3, Size: 29 } && link[0, 0] && link[6, 6] && !link[7, 7] && link[3, 3],
            $"QR : adresse de la télécommande → version {link?.Version}, {link?.Size}×{link?.Size}, masque {link?.Mask}");
        Check(lines, QrCode.Encode(new string('a', 213))?.Version == 10 && QrCode.Encode(new string('a', 214)) == null,
            "QR : 213 octets au plus (version 10)");

        var peers = new (string Ip, bool Local)[]
        {
            ("192.168.1.5", true), ("10.0.0.1", true), ("172.20.1.1", true), ("169.254.3.4", true), ("127.0.0.1", true), ("::1", true),
            ("fe80::1", true), ("172.32.0.1", false), ("8.8.8.8", false), ("26.10.20.30", false), ("2001:db8::1", false),
        };
        Check(lines, peers.All(p => LocalNetwork.IsLocalPeer(IPAddress.Parse(p.Ip)) == p.Local),
            "téléphones acceptés : réseau local seulement (pas Internet, pas le VPN Radmin 26.x)");
        Check(lines, RemoteApi.HostIsAddress("192.168.1.20:48765") && RemoteApi.HostIsAddress("[fe80::1]:48765")
                     && !RemoteApi.HostIsAddress("evil.example:48765") && !RemoteApi.HostIsAddress("localhost:48765") && !RemoteApi.HostIsAddress(""),
            "adresse demandée = une IP (protège du DNS rebinding)");
        Check(lines, RemoteApi.DeviceName("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)") == "iPhone"
                     && RemoteApi.DeviceName("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36") == "Android (Pixel 8)"
                     && RemoteApi.DeviceName("Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36") == "Android",
            "nom des téléphones d'après le navigateur");

        var networks = LocalNetwork.Addresses();
        Check(lines, true, "réseaux du PC (lecture seule) : " + (networks.Count == 0 ? "aucune adresse privée"
            : string.Join(", ", networks.Select(n => $"{n.Interface} = {n.Category}{(n.HasGateway ? ", box" : "")}"))));
        Check(lines, true, $"règle du pare-feu « {FirewallRule.Name} » présente : {(FirewallRule.Exists() ? "oui" : "non")} (lu, pas modifié)");

        Check(lines, RemoteUploads.SafeName("../../Windows/evil.exe") == "evil.exe" && RemoteUploads.SafeName("CON.txt") == "fichier.txt"
                     && RemoteUploads.SafeName("photo.jpg.part") == "photo.jpg.part.fichier" && RemoteUploads.SafeName("") == "fichier"
                     && RemoteUploads.SafeName("a<b>:c?.png") == "a_b__c_.png",
            "fichiers reçus : noms nettoyés (pas de dossier, pas de nom réservé ni de faux téléchargement en cours)");
        var wake = WakeOnLan.Read();
        Check(lines, true, "réveil à distance (lecture seule) : " + (wake == null ? "pas de carte filaire"
            : $"{wake.Description}, paquet magique « {wake.MagicPacket} », autorisée {(wake.Armed ? "oui" : "non")}, démarrage rapide {(wake.FastStartup ? "oui" : "non")} → {(wake.Ready ? "prêt" : "pas prêt")}"));
        Check(lines, true, "dernier réveil (journal Système + powercfg, lecture seule) : " + WakeOnLan.Describe(WakeOnLan.LastWake(wake?.Description)));
        // 7B : le réglage en administrateur (sans PowerShell) passe par cette classe WMI ; ici on la LIT seulement.
        if (wake is { MagicPacket.Length: > 0 })
        {
            string? fromWmi;
            try
            {
                fromWmi = WakeOnLan.MagicPacketFromWmi(wake.Adapter);
            }
            catch (Exception ex)
            {
                fromWmi = ex.GetType().Name;
            }
            Check(lines, fromWmi == wake.MagicPacket,
                $"réveil à distance : réglage « paquet magique » trouvé par WMI (lecture seule) = « {fromWmi} », registre = « {wake.MagicPacket} »");
        }
        Check(lines, WakeOnLan.RunSetup(["--wake-setup", "Ethernet"]) == 3 && WakeOnLan.RunSetup(["--wake-setup", "Ethernet", "2", "-", "Carte"]) == 3
                     && WakeOnLan.RunSetup(["--wake-setup", "Ethernet", "-", "reboot", "Carte"]) == 3,
            "réveil à distance : le mode administrateur refuse les arguments incomplets ou inconnus (rien n'est changé)");

        Check(lines, UpdateService.ParseDigest("sha256:8432B4FED7B3A120F7A2099D7E3EE32F8D4EE1CB37FF57E370493058FD32FC06")
                         == "8432b4fed7b3a120f7a2099d7e3ee32f8d4ee1cb37ff57e370493058fd32fc06"
                     && UpdateService.ParseDigest("md5:abc") == null && UpdateService.ParseDigest("sha256:1234") == null && UpdateService.ParseDigest(null) == null
                     && UpdateService.HashMatches(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("Opaline"u8)),
                                                  "4D57E1A6C9F1C0E3F5A5D31D6C1B2B5D1F25A4D6A9F0C7B2E4D8F6A1B3C5D7E9") == false
                     && UpdateService.HashMatches("ab".PadRight(64, '0'), "AB".PadRight(64, '0')) && !UpdateService.HashMatches("ab".PadRight(64, '0'), null),
            "mises à jour : empreinte SHA-256 de GitHub lue (« sha256:… »), comparée sans tenir compte des majuscules, absente = refus");
        Check(lines, true, "micros de Windows tous coupés (lecture seule) : " + (VolumeService.AllMicrophonesMuted() is { } muted ? (muted ? "oui" : "non") : "aucun micro"));

        var report = BugReport.CleanSettings("""{"Remote":{"Enabled":true,"Devices":[{"Id":"ab12","Name":"iPhone","TokenHash":"0123456789abcdef"}]}}""");
        Check(lines, !report.Contains("TokenHash") && !report.Contains("iPhone") && report.Contains("1 appareils"),
            "rapport de bug : téléphones remplacés par leur nombre");
    }

    /// <summary>Deux requêtes sur la même connexion TCP : la deuxième doit passer sans rouvrir (keep-alive).</summary>
    private static async Task<bool> KeepAliveAsync(int port, string token)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var request = Encoding.ASCII.GetBytes($"GET /api/state HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nAuthorization: Bearer {token}\r\n\r\n");
        for (int i = 0; i < 2; i++)
        {
            await stream.WriteAsync(request);
            var head = new List<byte>();
            var one = new byte[1];
            while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
            {
                // Lecture asynchrone : la réponse est préparée sur ce même thread (celui de l'interface).
                if (await stream.ReadAsync(one) == 0) return false;
                head.Add(one[0]);
            }
            var text = Encoding.ASCII.GetString(head.ToArray());
            if (!text.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) || !text.Contains("Connection: keep-alive")) return false;
            int length = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"Content-Length: (\d+)").Groups[1].Value);
            var body = new byte[length];
            await stream.ReadExactlyAsync(body);
        }
        return true;
    }

    /// <summary>Aller-retour réel sur la boucle locale (thread de l'interface : l'API dessine le logo).</summary>
    private static async Task RemoteRoundTripAsync(List<string> lines)
    {
        var devices = new List<RemoteDevice>();
        var backend = new DemoRemoteBackend();
        var inbox = Path.Combine(Path.GetTempPath(), "DynamicIsland-selftest-envoi-" + Guid.NewGuid().ToString("N"));
        var api = new RemoteApi(backend, () => devices, devices.Add, id => devices.RemoveAll(d => d.Id == id), new RemoteUploads(inbox));
        using var server = new RemoteServer(request => Application.Current.Dispatcher.InvokeAsync(() => api.HandleAsync(request)).Task.Unwrap());
        server.Start([IPAddress.Loopback], 0);
        var root = new Uri($"http://127.0.0.1:{server.Endpoints[0].Port}/");
        using var http = new HttpClient { BaseAddress = root, Timeout = TimeSpan.FromSeconds(10) };

        async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string path, string? json = null, string? token = null, string? host = null, byte[]? bytes = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (bytes != null) request.Content = new ByteArrayContent(bytes);
            if (token != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (host != null) request.Headers.Host = host;
            using var response = await http.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        using (var page = await http.GetAsync("/"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Check(lines, page.IsSuccessStatusCode && html.Contains("\"pair.title\"") && !html.Contains("/*STRINGS*/")
                         && page.Headers.Contains("Content-Security-Policy"),
                $"page du téléphone servie ({html.Length / 1024} Ko, textes traduits, CSP)");
        }
        using (var font = await http.GetAsync("/font.ttf"))
            Check(lines, font.IsSuccessStatusCode && (await font.Content.ReadAsByteArrayAsync()).Length > 10_000, "police des icônes servie");

        Check(lines, (await Send(HttpMethod.Get, "/api/state")).Status == HttpStatusCode.Unauthorized, "sans jeton : refusé (401)");
        Check(lines, (await Send(HttpMethod.Get, "/", host: "evil.example:1234")).Status == HttpStatusCode.MisdirectedRequest,
            "nom de domaine dans l'adresse : refusé (421)");
        Check(lines, (await Send(HttpMethod.Post, "/api/pair", """{"code":"123456"}""")).Body.Contains("closed"), "appairage fermé par défaut");

        api.OpenPairing("482913");
        Check(lines, (await Send(HttpMethod.Post, "/api/pair", """{"code":"000000"}""")).Body.Contains("\"code\""), "mauvais code : refusé");
        var paired = await Send(HttpMethod.Post, "/api/pair", """{"code":"482913"}""");
        var token = paired.Status == HttpStatusCode.OK ? JsonNode.Parse(paired.Body)?["token"]?.GetValue<string>() : null;
        Check(lines, token is { Length: >= 40 } && devices.Count == 1 && devices[0].TokenHash == RemoteApi.Hash(token) && !devices[0].TokenHash.Contains(token),
            "bon code : jeton donné, seule son empreinte est gardée");
        Check(lines, (await Send(HttpMethod.Post, "/api/pair", """{"code":"482913"}""")).Body.Contains("closed"), "code à usage unique");

        var state = await Send(HttpMethod.Get, "/api/state", token: token);
        Check(lines, state.Status == HttpStatusCode.OK && state.Body.Contains("Les Reflets"), "état lu avec le jeton");
        var volume = await Send(HttpMethod.Post, "/api/action", """{"action":"volume","value":0.5}""", token);
        var bad = await Send(HttpMethod.Post, "/api/action", """{"action":"format-c"}""", token);
        await Send(HttpMethod.Post, "/api/action", """{"action":"profile","value":"gaming"}""", token);
        Check(lines, volume.Status == HttpStatusCode.OK && bad.Status == HttpStatusCode.BadRequest
                     && backend.Actions.SequenceEqual(["volume:0.5", "format-c", "profile:gaming"]),
            "actions reçues (fausses, rien ne bouge sur le PC) ; action inconnue refusée");
        Check(lines, (await Send(HttpMethod.Post, "/api/action", "{\"action\":\"" + new string('x', 20_000) + "\"}", token)).Status == HttpStatusCode.BadRequest,
            "requête trop grosse : refusée");

        // 6D-1b : droits, applis, réveil, envoi de fichiers
        Check(lines, (await Send(HttpMethod.Post, "/api/action", """{"action":"shutdown"}""", token)).Status == HttpStatusCode.Forbidden
                     && (await Send(HttpMethod.Get, "/api/clipboard", token: token)).Status == HttpStatusCode.Forbidden
                     && !backend.Actions.Contains("shutdown"),
            "sans « Contrôle complet » : arrêt et presse-papiers du PC refusés (403)");
        Check(lines, (await Send(HttpMethod.Post, "/api/input", """{"t":"move","x":5,"y":5}""", token)).Status == HttpStatusCode.Forbidden
                     && (await Send(HttpMethod.Get, "/api/screen?n=0", token: token)).Status == HttpStatusCode.Forbidden
                     && !backend.Actions.Any(a => a.StartsWith("input:", StringComparison.Ordinal)),
            "sans « Contrôle complet » : pavé tactile et aperçu de l'écran refusés (403)");
        devices[0].FullControl = true;
        var move = await Send(HttpMethod.Post, "/api/input", """{"t":"move","x":5,"y":5}""", token);
        var badInput = await Send(HttpMethod.Post, "/api/input", """{"t":"format"}""", token);
        var windows = await Send(HttpMethod.Get, "/api/windows", token: token);
        using (var screen = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/screen?n=0") { Headers = { Authorization = new("Bearer", token) } }))
        {
            var jpeg = await screen.Content.ReadAsByteArrayAsync();
            Check(lines, move.Status == HttpStatusCode.OK && backend.Actions.Contains("input:move") && badInput.Status == HttpStatusCode.BadRequest
                         && windows.Body.Contains("YouTube") && screen.Content.Headers.ContentType?.MediaType == "image/jpeg" && jpeg is [0xFF, 0xD8, ..],
                "avec « Contrôle complet » : pavé (faux, la souris ne bouge pas), fenêtres, aperçu JPEG ; entrée inconnue refusée");
        }
        Check(lines, await KeepAliveAsync(server.Endpoints[0].Port, token!), "connexion gardée ouverte entre deux requêtes (pavé tactile fluide)");
        var shutdown = await Send(HttpMethod.Post, "/api/action", """{"action":"shutdown"}""", token);
        var clipboard = await Send(HttpMethod.Get, "/api/clipboard", token: token);
        Check(lines, shutdown.Status == HttpStatusCode.OK && backend.Actions.Contains("shutdown") && clipboard.Body.Contains("exemple"),
            "avec « Contrôle complet » : acceptés (faux arrêt de la démo, rien ne s'éteint)");
        var apps = await Send(HttpMethod.Get, "/api/apps?q=spo", token: token);
        using (var icon = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/icon?id=pin%3Ademo0") { Headers = { Authorization = new("Bearer", token) } }))
            Check(lines, apps.Body.Contains("Discord") && apps.Body.Contains("spo (d") && icon.IsSuccessStatusCode && icon.Content.Headers.ContentType?.MediaType == "image/png",
                "applis du dock, recherche (paramètre d'adresse décodé) et icônes");
        Check(lines, (await Send(HttpMethod.Get, "/api/wake", token: token)).Body.Contains("D8:5E"), "infos de réveil à distance");

        var photo = new byte[5_000_000];
        new Random(42).NextBytes(photo);
        const string upload = "/api/upload?id=selftest0001&name=..%2F..%2Fphoto%20test.jpg&total=5000000";
        var first = await Send(HttpMethod.Post, upload + "&offset=0", token: token, bytes: photo[..4_194_304]);
        var wrong = await Send(HttpMethod.Post, upload + "&offset=12", token: token, bytes: photo[..10]);
        var hidden = Directory.GetFiles(inbox, ".opaline-*.part").Select(File.GetAttributes).All(a => a.HasFlag(FileAttributes.Hidden));
        var finish = await Send(HttpMethod.Post, upload + "&offset=4194304", token: token, bytes: photo[4_194_304..]);
        var received = Path.Combine(inbox, "photo test.jpg");
        Check(lines, first.Body.Contains("4194304") && wrong.Status == HttpStatusCode.Conflict && hidden && finish.Body.Contains("\"done\":true")
                     && File.Exists(received) && File.ReadAllBytes(received).SequenceEqual(photo) && !File.GetAttributes(received).HasFlag(FileAttributes.Hidden)
                     && Directory.GetFiles(inbox).Length == 1,
            "photo de 5 Mo envoyée en 2 morceaux : reprise au bon endroit, morceaux cachés, fichier identique dans le dossier");
        Check(lines, (await Send(HttpMethod.Post, upload.Replace("selftest0001", "selftest0002") + "&offset=0", bytes: photo[..10])).Status == HttpStatusCode.Unauthorized,
            "envoi sans jeton : refusé");
        Directory.Delete(inbox, recursive: true);

        var real = new AppRemoteBackend(() => null, (_, _, _) => { });
        var pinned = real.Apps(null)["pinned"]!.AsArray();
        var firstIcon = pinned.Count > 0 ? real.AppIcon(pinned[0]!["id"]!.GetValue<string>()) : null;
        Check(lines, pinned.Count == 0 || firstIcon is { Length: > 100 }, $"vraies applis du dock lues (lecture seule) : {pinned.Count}, icône {(firstIcon?.Length / 1024) ?? 0} Ko");
        var openWindows = real.Windows();
        var capture = await Task.Run(() => real.Screen(RemoteScreen.Primary));
        Check(lines, capture is [0xFF, 0xD8, ..],
            $"vraies fenêtres ({openWindows.Count}) et capture de l'écran principal en mémoire ({(capture?.Length ?? 0) / 1024} Ko, {RemoteScreen.Screens.Count} écran(s), rien n'est écrit)");

        api.OpenPairing("135790");
        string last = "";
        for (int i = 0; i < 5; i++) last = (await Send(HttpMethod.Post, "/api/pair", """{"code":"111111"}""")).Body;
        Check(lines, last.Contains("closed") && (await Send(HttpMethod.Post, "/api/pair", """{"code":"135790"}""")).Body.Contains("closed"),
            "5 mauvais codes : appairage fermé");

        await Send(HttpMethod.Post, "/api/forget", "{}", token);
        Check(lines, devices.Count == 0 && (await Send(HttpMethod.Get, "/api/state", token: token)).Status == HttpStatusCode.Unauthorized,
            "« Oublier ce téléphone » : le jeton ne marche plus");
        server.Stop();
    }
}
