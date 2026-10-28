using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Allumer le PC depuis le téléphone (6D-1b, « Wake-on-LAN ») : un PC éteint ne sert plus de page, donc c'est une appli
/// « Wake on LAN » du téléphone qui envoie le « paquet magique » à la carte réseau filaire. Ici : lire si la carte est
/// prête (sans droits), et la préparer / la remettre comme avant (fenêtre administrateur, après confirmation) :
/// « Wake on Magic Packet » de la carte + « Autoriser ce périphérique à sortir l'ordinateur du mode veille ».
/// Le BIOS (ErP désactivé, réveil par la carte réseau) reste à régler par l'utilisateur.
/// </summary>
public static class WakeOnLan
{
    private const string NetworkClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string MagicPacketKeyword = "*WakeOnMagicPacket";

    /// <param name="Adapter">Nom Windows de la connexion (« Ethernet 2 »).</param>
    /// <param name="Description">Nom de la carte (« Intel(R) Ethernet Connection (17) I219-V »).</param>
    /// <param name="MagicPacket">« Wake on Magic Packet » : « 1 », « 0 », ou vide si la carte n'a pas ce réglage.</param>
    /// <param name="Armed">La carte a le droit de réveiller le PC (powercfg).</param>
    public sealed record Status(string Adapter, string Description, string Mac, string Broadcast, string MagicPacket, bool Armed, bool FastStartup)
    {
        public bool Ready => MagicPacket != "0" && Armed;
    }

    /// <summary>La carte filaire reliée à la box (null s'il n'y en a pas : le Wi-Fi réveille rarement un PC).</summary>
    public static Status? Read()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                        && !IsVirtual(n.Description))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .FirstOrDefault();
        if (nic == null) return null;

        var mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
        var ipv4 = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        string broadcast = "255.255.255.255";
        if (ipv4 != null)
        {
            var ip = ipv4.Address.GetAddressBytes();
            var mask = ipv4.IPv4Mask.GetAddressBytes();
            broadcast = string.Join(".", ip.Select((b, i) => (byte)(b | ~mask[i])));
        }
        return new Status(nic.Name, nic.Description, mac, broadcast, MagicPacket(nic.Id), IsArmed(nic.Description), FastStartup());
    }

    private static bool IsVirtual(string description) =>
        new[] { "Virtual", "VPN", "Radmin", "Hyper-V", "VMware", "VirtualBox", "TAP", "Loopback", "Bluetooth" }
            .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>Réglage de la carte lu dans le registre du pilote (lisible sans droits).</summary>
    private static string MagicPacket(string adapterId)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(NetworkClass);
            if (root == null) return "";
            foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (!string.Equals(key?.GetValue("NetCfgInstanceId") as string, adapterId, StringComparison.OrdinalIgnoreCase)) continue;
                    return key!.GetValue(MagicPacketKeyword)?.ToString() ?? "";
                }
                catch (System.Security.SecurityException)
                {
                    // sous-clé réservée au système : on passe
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Réveil à distance : lecture de la carte réseau", ex);
        }
        return "";
    }

    private static bool IsArmed(string description) =>
        Powercfg("/devicequery wake_armed").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line, description, StringComparison.OrdinalIgnoreCase));

    private static string Powercfg(string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (Exception ex)
        {
            Log.Error("Réveil à distance : powercfg", ex);
            return "";
        }
    }

    // ------------------------------------------------------------------ dernier réveil

    /// <param name="FromSleep">Sortie de veille (sinon : démarrage, dont Windows ne dit pas la cause).</param>
    /// <param name="ByNetwork">Réveillé par la carte réseau (le paquet magique du téléphone).</param>
    public sealed record LastWakeInfo(DateTime Time, bool FromSleep, bool ByNetwork);

    /// <summary>
    /// Dernier réveil : l'heure vient du journal Système (Kernel-Power 107 = sortie de veille, lisible sans droits), la cause de
    /// <c>powercfg /lastwake</c> (la carte réseau y est nommée). Après un démarrage complet, Windows ne garde pas la cause.
    /// </summary>
    public static LastWakeInfo LastWake(string? adapterDescription)
    {
        var boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        DateTime? resume = null;
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]")
            {
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            resume = record?.TimeCreated;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            Log.Error("Réveil à distance : journal Système", ex);
        }
        if (resume is not { } time || time < boot) return new LastWakeInfo(boot, false, false);
        bool network = !string.IsNullOrEmpty(adapterDescription) && Powercfg("/lastwake").Contains(adapterDescription, StringComparison.OrdinalIgnoreCase);
        return new LastWakeInfo(time, true, network);
    }

    /// <summary>« Dernier réveil : par le réseau (ton téléphone), aujourd'hui à 19:56 ».</summary>
    public static string Describe(LastWakeInfo wake)
    {
        var when = wake.Time.Date == DateTime.Today
            ? Loc.F("wake.when.today", wake.Time.ToString("t", Loc.Culture))
            : Loc.F("wake.when.date", wake.Time.ToString("d MMMM", Loc.Culture), wake.Time.ToString("t", Loc.Culture));
        return Loc.F(!wake.FromSleep ? "wake.last.boot" : wake.ByNetwork ? "wake.last.network" : "wake.last.other", when);
    }

    /// <summary>Le PC vient d'être réveillé par le réseau (sur un autre thread que l'interface).</summary>
    public static event Action? WokenByNetwork;

    public static void Watch() => Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

    public static void StopWatching() => Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private static void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4)); // le journal et powercfg sont à jour
            if (Read() is { } nic && LastWake(nic.Description) is { FromSleep: true, ByNetwork: true } wake && DateTime.Now - wake.Time < TimeSpan.FromMinutes(3))
            {
                Log.Info("Réveil à distance : PC réveillé par le réseau");
                WokenByNetwork?.Invoke();
            }
        });
    }

    /// <summary>Démarrage rapide de Windows (il empêche souvent le réveil d'un PC éteint).</summary>
    private static bool FastStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
        return key?.GetValue("HiberbootEnabled") is int value && value == 1;
    }

    /// <summary>Active le réveil par paquet magique (la connexion réseau se coupe 2-3 s) ; garde les valeurs d'avant.</summary>
    public static async Task<bool?> PrepareAsync(Status status)
    {
        var done = await RunSetupAsync(status.Adapter, status.MagicPacket == "0" ? "1" : "-", status.Armed ? "-" : "enable", status.Description);
        if (done == true)
            SettingsStore.Update(s => s.Remote.Wake = new WakeBackup
            {
                Adapter = status.Adapter,
                Description = status.Description,
                MagicPacket = status.MagicPacket,
                WasArmed = status.Armed,
            });
        return done;
    }

    /// <summary>Remet la carte comme avant « Préparer le PC ».</summary>
    public static async Task<bool?> UndoAsync(WakeBackup backup)
    {
        var done = await RunSetupAsync(backup.Adapter, backup.MagicPacket is "0" or "1" ? backup.MagicPacket : "-",
                                       backup.WasArmed ? "-" : "disable", backup.Description);
        if (done == true) SettingsStore.Update(s => s.Remote.Wake = null);
        return done;
    }

    // ------------------------------------------------------------------ réglage en administrateur (7B)

    /// <summary>
    /// Argument de l'exe : Opaline elle-même, relancée avec la fenêtre administrateur de Windows, règle la carte puis se
    /// ferme (code 0 = réussi). Plus de PowerShell caché à commande encodée : c'est ce que les antivirus surveillent le plus.
    /// </summary>
    public const string SetupArgument = "--wake-setup";

    /// <param name="magic">« 1 » / « 0 » = valeur de « Wake on Magic Packet » à écrire, « - » = ne pas y toucher.</param>
    /// <param name="wake">« enable » / « disable » = droit de réveiller le PC (powercfg), « - » = ne pas y toucher.</param>
    private static Task<bool?> RunSetupAsync(string adapter, string magic, string wake, string description)
    {
        if (Environment.ProcessPath is not { } exe || adapter.Contains('"') || description.Contains('"'))
            return Task.FromResult<bool?>(false);
        return FirewallRule.RunElevatedAsync(exe, $"{SetupArgument} \"{adapter}\" {magic} {wake} \"{description}\"");
    }

    /// <summary>Dans le processus administrateur (<see cref="SetupArgument"/>) : 0 = réussi, 1 = carte, 2 = powercfg, 3 = arguments.</summary>
    public static int RunSetup(string[] args)
    {
        int at = Array.IndexOf(args, SetupArgument);
        if (at < 0 || at + 4 >= args.Length) return 3;
        string adapter = args[at + 1], magic = args[at + 2], wake = args[at + 3], description = args[at + 4];
        if (magic is not ("0" or "1" or "-") || wake is not ("enable" or "disable" or "-")) return 3;
        try
        {
            if (magic != "-" && !SetMagicPacket(adapter, magic)) return 1;
            if (wake != "-" && !RunPowercfg($"/device{wake}wake \"{description}\"")) return 2;
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Réveil à distance : réglage de la carte (administrateur)", ex);
            return 1;
        }
    }

    /// <summary>
    /// Même chose que Set-NetAdapterAdvancedProperty, par la classe WMI de Windows qu'il utilise
    /// (MSFT_NetAdapterAdvancedPropertySettingData) : Windows écrit le réglage et redémarre la carte (2-3 s).
    /// </summary>
    private static bool SetMagicPacket(string adapter, string value)
    {
        bool done = false;
        ForEachMagicPacketSetting(adapter, setting =>
        {
            setting.RegistryValue = new object[] { value };
            setting.Put_();
            done = true;
        });
        return done;
    }

    /// <summary>Valeur lue par WMI (sans droits) : sert à l'auto-test pour vérifier qu'on vise le bon réglage.</summary>
    internal static string? MagicPacketFromWmi(string adapter)
    {
        string? found = null;
        ForEachMagicPacketSetting(adapter, setting =>
        {
            if (setting.RegistryValue is object[] { Length: > 0 } values) found = values[0]?.ToString();
        });
        return found;
    }

    private static void ForEachMagicPacketSetting(string adapter, Action<dynamic> action)
    {
        var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI indisponible");
        dynamic locator = Activator.CreateInstance(type)!;
        dynamic service = locator.ConnectServer(".", @"root\StandardCimv2");
        string name = adapter.Replace(@"\", @"\\").Replace("'", @"\'");
        dynamic settings = service.ExecQuery("SELECT * FROM MSFT_NetAdapterAdvancedPropertySettingData " +
                                             $"WHERE Name = '{name}' AND RegistryKeyword = '{MagicPacketKeyword}'");
        int count = settings.Count;
        for (int i = 0; i < count; i++) action(settings.ItemIndex(i));
    }

    private static bool RunPowercfg(string arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start);
        if (process == null) return false;
        process.WaitForExit(15000);
        return process.HasExited && process.ExitCode == 0;
    }
}
