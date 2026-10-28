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
