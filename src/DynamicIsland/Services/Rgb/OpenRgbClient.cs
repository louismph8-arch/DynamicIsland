using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DynamicIsland.Services.Rgb;

/// <summary>
/// Client du « SDK Server » d'OpenRGB (logiciel libre, à installer et lancer par l'utilisateur, onglet « SDK Server » →
/// « Start Server ») pour les autres marques (Corsair, ASUS, MSI, Razer…). Protocole réseau d'OpenRGB, version 0 (la plus
/// simple, comprise par tous les serveurs) sur 127.0.0.1:6742 : nombre d'appareils, description de chaque appareil (pour
/// connaître son nombre de LED), mode « personnalisé », puis la même couleur sur toutes les LED.
/// </summary>
public sealed class OpenRgbClient : IDisposable
{
    public const int DefaultPort = 6742;
    private const uint RequestControllerCount = 0, RequestControllerData = 1, SetClientName = 50, UpdateLeds = 1050, SetCustomMode = 1100;
    private static readonly byte[] Magic = "ORGB"u8.ToArray();

    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public OpenRgbClient(int port = DefaultPort) => _port = port;

    private static bool IsOpenRgbRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("OpenRGB");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    /// <summary>Appareils trouvés : nom et nombre de LED.</summary>
    public List<(string Name, int Leds)> Devices { get; } = [];

    public bool IsConnected => _client?.Connected == true;

    /// <summary>Se connecte et lit les appareils ; faux si OpenRGB ne tourne pas (ou sans son serveur SDK).</summary>
    public bool Start()
    {
        Stop();
        // OpenRGB tourne forcément sur ce PC (adresse locale) : pas de processus, pas d'essai (7A). Un port fermé met ~2 s à
        // répondre sous Windows, et la tentative abandonnée laissait une erreur dans le journal toutes les 30 s.
        if (_port == DefaultPort && !IsOpenRgbRunning()) return false;
        try
        {
            _client = new TcpClient { NoDelay = true, ReceiveTimeout = 2000, SendTimeout = 2000 };
            var connect = _client.ConnectAsync(IPAddress.Loopback, _port);
            if (!connect.Wait(TimeSpan.FromSeconds(1)) || !_client.Connected)
            {
                _ = connect.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // erreur vue : rien au journal
                Stop();
                return false;
            }
            _stream = _client.GetStream();
            Send(0, SetClientName, Encoding.ASCII.GetBytes(AppInfo.Name + "\0"));
            Send(0, RequestControllerCount, []);
            int count = (int)BitConverter.ToUInt32(Receive(RequestControllerCount), 0);
            for (uint device = 0; device < count && device < 64; device++)
            {
                Send(device, RequestControllerData, []);
                var (name, leds) = ParseController(Receive(RequestControllerData));
                Devices.Add((name, leds));
                if (leds > 0) Send(device, SetCustomMode, []); // mode « Direct » : on peut choisir chaque LED
            }
            Log.Info($"RVB : OpenRGB connecté, {Devices.Count} appareil(s)");
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or AggregateException or ObjectDisposedException
                                       or ArgumentOutOfRangeException or EndOfStreamException)
        {
            Log.Info($"RVB : OpenRGB indisponible ({ex.GetType().Name})");
            Stop();
            return false;
        }
    }

    /// <summary>Même couleur sur toutes les LED de tous les appareils.</summary>
    public bool SetColor(byte red, byte green, byte blue)
    {
        if (_stream == null) return false;
        try
        {
            uint color = (uint)(red | green << 8 | blue << 16);
            for (int device = 0; device < Devices.Count; device++)
            {
                int leds = Devices[device].Leds;
                if (leds == 0) continue;
                var data = new byte[4 + 2 + 4 * leds];
                BitConverter.TryWriteBytes(data.AsSpan(0), (uint)data.Length);
                BitConverter.TryWriteBytes(data.AsSpan(4), (ushort)leds);
                for (int i = 0; i < leds; i++) BitConverter.TryWriteBytes(data.AsSpan(6 + 4 * i), color);
                Send((uint)device, UpdateLeds, data);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Stop();
            return false;
        }
    }

    /// <summary>OpenRGB garde la dernière couleur : son profil se recharge depuis OpenRGB.</summary>
    public void Stop()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        Devices.Clear();
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ protocole

    private void Send(uint device, uint packet, byte[] data)
    {
        var frame = new byte[16 + data.Length];
        Magic.CopyTo(frame, 0);
        BitConverter.TryWriteBytes(frame.AsSpan(4), device);
        BitConverter.TryWriteBytes(frame.AsSpan(8), packet);
        BitConverter.TryWriteBytes(frame.AsSpan(12), (uint)data.Length);
        data.CopyTo(frame, 16);
        _stream!.Write(frame);
    }

    /// <summary>Réponse attendue ; les annonces du serveur (liste d'appareils changée…) passent.</summary>
    private byte[] Receive(uint expected)
    {
        for (int skipped = 0; skipped < 16; skipped++)
        {
            var header = new byte[16];
            _stream!.ReadExactly(header);
            if (!header.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("OpenRGB : trame invalide");
            uint packet = BitConverter.ToUInt32(header, 8);
            int size = (int)BitConverter.ToUInt32(header, 12);
            if (size is < 0 or > 4 * 1024 * 1024) throw new InvalidDataException("OpenRGB : taille invalide");
            var data = new byte[size];
            _stream.ReadExactly(data);
            if (packet == expected) return data;
        }
        throw new InvalidDataException("OpenRGB : réponse attendue absente");
    }

    /// <summary>Description d'un appareil (protocole 0) : on n'en garde que le nom et le nombre de LED.</summary>
    internal static (string Name, int Leds) ParseController(byte[] data)
    {
        var reader = new Reader(data);
        reader.UInt32(); // taille
        reader.Int32(); // type
        var name = reader.Text();
        reader.Text(); // description
        reader.Text(); // version
        reader.Text(); // numéro de série
        reader.Text(); // emplacement
        int modes = reader.UInt16();
        reader.Int32(); // mode actif
        for (int i = 0; i < modes; i++)
        {
            reader.Text(); // nom
            reader.Skip(4 * 9); // valeur, options, vitesse min / max, couleurs min / max, vitesse, direction, mode de couleur
            reader.Skip(4 * reader.UInt16()); // couleurs du mode
        }
        int zones = reader.UInt16();
        for (int i = 0; i < zones; i++)
        {
            reader.Text(); // nom
            reader.Skip(4 * 4); // type, LED min / max / nombre
            reader.Skip(reader.UInt16()); // matrice (taille en octets, hauteur et largeur comprises)
        }
        int leds = reader.UInt16();
        return (name, leds);
    }

    private sealed class Reader(byte[] data)
    {
        private int _position;

        public uint UInt32() => BitConverter.ToUInt32(Take(4));
        public int Int32() => BitConverter.ToInt32(Take(4));
        public ushort UInt16() => BitConverter.ToUInt16(Take(2));
        public void Skip(int count) => Take(count);

        /// <summary>Texte : longueur sur 2 octets (zéro final compris), puis les caractères.</summary>
        public string Text()
        {
            int length = UInt16();
            var bytes = Take(length);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _position + count > data.Length) throw new EndOfStreamException("OpenRGB : description tronquée");
            var span = data.AsSpan(_position, count);
            _position += count;
            return span;
        }
    }
}
