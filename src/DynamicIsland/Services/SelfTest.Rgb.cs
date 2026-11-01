using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DynamicIsland.Services.Rgb;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de l'éclairage RGB (6D-2) : le kit d'éclairage de G HUB est seulement CHERCHÉ (rien n'est chargé ni allumé :
/// l'éclairage de l'utilisateur ne bouge pas) ; le client OpenRGB parle à un faux serveur OpenRGB sur 127.0.0.1.
/// </summary>
internal static partial class SelfTest
{
    private static void Rgb(List<string> lines)
    {
        var sdk = LogitechLighting.SdkPath;
        Check(lines, true, "kit d'éclairage de G HUB (cherché, pas chargé) : " + (sdk ?? "absent (G HUB pas installé)"));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<(uint Packet, byte[] Data)>();
        var server = Task.Run(() => FakeOpenRgb(listener, received));

        using var client = new OpenRgbClient(port);
        bool started = client.Start();
        bool colored = client.SetColor(255, 0, 128);
        server.Wait(TimeSpan.FromSeconds(5));
        var leds = received.FirstOrDefault(r => r.Packet == 1050).Data;
        bool colors = leds != null && leds.Length == 4 + 2 + 3 * 4 && BitConverter.ToUInt16(leds, 4) == 3
                      && Enumerable.Range(0, 3).All(i => BitConverter.ToUInt32(leds, 6 + 4 * i) == 0x8000FFu);
        Check(lines, started && colored && client.Devices is [("Clavier test", 3)] && received.Any(r => r.Packet == 1100) && colors,
            "OpenRGB (faux serveur) : appareil et ses 3 LED lus, mode personnalisé, couleur envoyée à chaque LED");
        Check(lines, !new OpenRgbClient(1).Start(), "OpenRGB absent : refusé proprement, sans attendre");

        // Point 26 : Razer, SteelSeries contre de faux serveurs ; Corsair seulement cherché (jamais chargé).
        var dll = CorsairIcue.FindDll();
        Check(lines, true, "kit iCUE (cherché, pas chargé) : " + (dll ?? "introuvable (normal sans le SDK de Corsair)"));
        RazerFake(lines);
        SteelSeriesFake(lines);
    }

    /// <summary>Requête reçue par un faux serveur : méthode, chemin, corps JSON.</summary>
    private sealed record FakeRequest(string Method, string Path, System.Text.Json.Nodes.JsonNode? Body);

    /// <summary>Petit faux serveur HTTP sur localhost (port libre) : chaque requête est notée, la réponse vient de <paramref name="reply"/>.</summary>
    private static (HttpListener Listener, string Root, List<FakeRequest> Received) FakeHttp(Func<FakeRequest, string, string> reply)
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        var root = $"http://localhost:{port}";
        var listener = new HttpListener();
        listener.Prefixes.Add(root + "/");
        listener.Start();
        var received = new List<FakeRequest>();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var text = reader.ReadToEnd();
                System.Text.Json.Nodes.JsonNode? body = null;
                try
                {
                    if (text.Length > 0) body = System.Text.Json.Nodes.JsonNode.Parse(text);
                }
                catch (System.Text.Json.JsonException)
                {
                }
                var request = new FakeRequest(context.Request.HttpMethod, context.Request.Url!.AbsolutePath, body);
                lock (received) received.Add(request);
                var answer = Encoding.UTF8.GetBytes(reply(request, root));
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(answer);
                context.Response.Close();
            }
        });
        return (listener, root, received);
    }

    /// <summary>Faux Synapse (API REST Chroma) : inscription → adresse de session, effets, battement de cœur, fin de session.</summary>
    private static void RazerFake(List<string> lines)
    {
        var (listener, root, received) = FakeHttp((request, root) => request switch
        {
            { Method: "POST", Path: "/razer/chromasdk" } => $"{{\"sessionid\":42,\"uri\":\"{root}/session/chromasdk\"}}",
            { Path: "/session/chromasdk/heartbeat" } => "{\"tick\":1}",
            _ => "{\"result\":0}",
        });
        using (listener)
        {
            var razer = new RazerChroma(root + "/razer/chromasdk", checkProcess: false, settle: TimeSpan.Zero);
            razer.Start();
            bool connected = razer.IsConnected && razer.State == RgbBrandState.Connected;
            bool colored = razer.SetColor(255, 0, 128);
            bool beat = razer.Heartbeat();
            razer.Stop();
            List<FakeRequest> all;
            lock (received) all = [.. received];
            var init = all.FirstOrDefault(r => r.Method == "POST")?.Body;
            bool initOk = init?["device_supported"]?.AsArray().Count == 6 && init["category"]?.GetValue<string>() == "application"
                          && init["title"]?.GetValue<string>() == AppInfo.Name && init["author"]?["name"] != null;
            var effects = all.Where(r => r.Method == "PUT" && !r.Path.EndsWith("/heartbeat")).ToList();
            bool effectsOk = effects.Count == 6 && RazerChroma.Devices.All(d => effects.Any(e => e.Path == "/session/chromasdk/" + d))
                             && effects.All(e => e.Body?["effect"]?.GetValue<string>() == "CHROMA_STATIC"
                                                 && e.Body["param"]?["color"]?.GetValue<int>() == 0x8000FF); // BGR
            bool deleted = all.Any(r => r.Method == "DELETE" && r.Path == "/session/chromasdk");
            Check(lines, connected && colored && beat && initOk && effectsOk && deleted && razer.State == RgbBrandState.Off,
                "Razer Chroma (faux Synapse) : inscription, CHROMA_STATIC en BGR sur les 6 types d'appareils, battement de cœur, session rendue");

            var refused = new RazerChroma(root + "/refuse", checkProcess: false, settle: TimeSpan.Zero);
            refused.Start(); // le faux serveur répond { "result": 0 } sans adresse
            Check(lines, !refused.IsConnected && refused.State == RgbBrandState.Refused, "Razer Chroma : inscription sans adresse = refusé proprement");
        }
    }

    /// <summary>Faux SteelSeries GG (GameSense) + lecture de coreProps.json.</summary>
    private static void SteelSeriesFake(List<string> lines)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Opaline-dev", "gamesense-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        var props = Path.Combine(folder, "coreProps.json");
        File.WriteAllText(props, "{\"address\":\"127.0.0.1:51248\",\"encryptedAddress\":\"127.0.0.1:51249\"}");
        bool local = SteelSeriesGameSense.ReadAddress(props) == "127.0.0.1:51248";
        File.WriteAllText(props, "{\"address\":\"exemple.com:80\"}");
        bool remote = SteelSeriesGameSense.ReadAddress(props) == null;
        Directory.Delete(folder, true);
        Check(lines, local && remote && SteelSeriesGameSense.ReadAddress(props) == null,
            "SteelSeries : adresse lue dans coreProps.json, adresse non locale ou fichier absent refusés");

        var (listener, root, received) = FakeHttp((_, _) => "{}");
        using (listener)
        {
            var gg = new SteelSeriesGameSense(root.Replace("http://", ""));
            gg.Start();
            bool connected = gg.IsConnected && gg.State == RgbBrandState.Connected;
            bool colored = gg.SetColor(255, 0, 128) && gg.SetColor(255, 0, 128);
            bool beat = gg.Heartbeat();
            gg.Stop();
            List<FakeRequest> all;
            lock (received) all = [.. received];
            var meta = all.FirstOrDefault(r => r.Path == "/game_metadata")?.Body;
            var bind = all.FirstOrDefault(r => r.Path == "/bind_game_event")?.Body;
            var events = all.Where(r => r.Path == "/game_event").Select(r => r.Body).ToList();
            bool metaOk = meta?["game"]?.GetValue<string>() == "OPALINE" && meta["game_display_name"]?.GetValue<string>() == AppInfo.Name;
            var handlers = bind?["handlers"]?.AsArray();
            bool bindOk = bind?["event"]?.GetValue<string>() == "COLOR" && handlers?.Count == SteelSeriesGameSense.Zones.Length
                          && handlers.All(h => h?["mode"]?.GetValue<string>() == "context-color" && h["context-frame-key"]?.GetValue<string>() == "color")
                          && handlers.Any(h => h?["device-type"]?.GetValue<string>() == "rgb-per-key-zones" && h["zone"]?.GetValue<string>() == "all");
            var color = events.FirstOrDefault()?["data"]?["frame"]?["color"];
            bool eventOk = events.Count == 2 && color?["red"]?.GetValue<int>() == 255 && color["green"]?.GetValue<int>() == 0
                           && color["blue"]?.GetValue<int>() == 128
                           && events[0]?["data"]?["value"]?.GetValue<int>() != events[1]?["data"]?["value"]?.GetValue<int>();
            bool order = all.Select(r => r.Path).SequenceEqual(["/game_metadata", "/bind_game_event", "/game_event", "/game_event", "/game_heartbeat", "/remove_game"]);
            Check(lines, connected && colored && beat && metaOk && bindOk && eventOk && order && gg.State == RgbBrandState.Off,
                "SteelSeries GG (faux GameSense) : appli déclarée, gestionnaires « context-color », couleur dans l'événement (valeur qui change), battement, /remove_game");
        }
    }

    /// <summary>Faux « SDK Server » d'OpenRGB : un clavier de 3 LED (une zone avec matrice, un mode avec 2 couleurs).</summary>
    private static void FakeOpenRgb(TcpListener listener, List<(uint Packet, byte[] Data)> received)
    {
        using var socket = listener.AcceptTcpClient();
        var stream = socket.GetStream();
        stream.ReadTimeout = 3000;
        for (int frames = 0; frames < 5; frames++)
        {
            var header = new byte[16];
            stream.ReadExactly(header);
            uint packet = BitConverter.ToUInt32(header, 8);
            var data = new byte[BitConverter.ToUInt32(header, 12)];
            stream.ReadExactly(data);
            received.Add((packet, data));
            byte[]? reply = packet switch
            {
                0 => BitConverter.GetBytes(1u),
                1 => ControllerBlob(),
                _ => null,
            };
            if (reply == null) continue;
            var frame = new byte[16 + reply.Length];
            "ORGB"u8.CopyTo(frame);
            BitConverter.TryWriteBytes(frame.AsSpan(8), packet);
            BitConverter.TryWriteBytes(frame.AsSpan(12), (uint)reply.Length);
            reply.CopyTo(frame, 16);
            stream.Write(frame);
        }
    }

    private static byte[] ControllerBlob()
    {
        var body = new List<byte>();
        void U32(uint value) => body.AddRange(BitConverter.GetBytes(value));
        void U16(ushort value) => body.AddRange(BitConverter.GetBytes(value));
        void Text(string text)
        {
            U16((ushort)(text.Length + 1));
            body.AddRange(Encoding.ASCII.GetBytes(text));
            body.Add(0);
        }
        U32(0); // type
        Text("Clavier test");
        Text("Description");
        Text("1.0");
        Text("ABC123");
        Text("HID: test");
        U16(1); // modes
        U32(0); // mode actif
        Text("Direct");
        for (int i = 0; i < 9; i++) U32((uint)i);
        U16(2);
        U32(0xFF0000);
        U32(0x00FF00);
        U16(1); // zones
        Text("Touches");
        for (int i = 0; i < 4; i++) U32(3);
        U16(24); // matrice : hauteur, largeur, 2 × 2 cases
        for (int i = 0; i < 6; i++) U32(1);
        U16(3); // LED
        foreach (var name in new[] { "A", "B", "C" })
        {
            Text(name);
            U32(0);
        }
        U16(3); // couleurs
        for (int i = 0; i < 3; i++) U32(0);
        var blob = new List<byte>(BitConverter.GetBytes((uint)(body.Count + 4)));
        blob.AddRange(body);
        return [.. blob];
    }
}
