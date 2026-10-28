using System.IO;
using System.Windows;
using DynamicIsland.Settings;
using Windows.Devices.Enumeration;
using Windows.Gaming.Input;

namespace DynamicIsland.Services;

/// <summary>
/// Appareils qui se connectent (6C), pour les annonces « façon AirPods » de l'île : appareil Bluetooth jumelé qui se
/// connecte (casque, écouteurs, souris…), manette (Xbox, PlayStation…, filaire ou sans fil), clé USB ou carte mémoire.
/// Uniquement des événements de Windows (DeviceWatcher, Windows.Gaming.Input), aucune scrutation ; rien n'est annoncé pour
/// les appareils déjà là au démarrage. Batterie : seulement si Windows la donne (Bluetooth récent, manettes).
/// </summary>
public static class DeviceNotifier
{
    public enum Kind { Audio, Controller, Bluetooth, Storage }

    /// <summary>Appareil arrivé : type, nom, batterie en % (null si inconnue), lecteur (clé USB).</summary>
    public sealed record Arrival(Kind Kind, string Name, int? Battery, string? Drive);

    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string AddressKey = "System.Devices.Aep.DeviceAddress";
    private const string MajorClassKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    /// <summary>Niveau de batterie Bluetooth tel que l'affiche l'app Paramètres (DEVPKEY_Bluetooth_Battery).</summary>
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    /// <summary>Appareils Bluetooth jumelés (classiques et basse consommation).</summary>
    private const string PairedBluetooth = $"(System.Devices.Aep.ProtocolId:=\"{ClassicProtocol}\" OR System.Devices.Aep.ProtocolId:=\"{LowEnergyProtocol}\") "
                                           + "AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";

    private static readonly Dictionary<string, bool> Connected = [];
    private static readonly Dictionary<string, DateTime> Recent = [];
    private static readonly HashSet<string> Drives = new(StringComparer.OrdinalIgnoreCase);
    private static DeviceWatcher? _bluetooth;
    private static DeviceWatcher? _storage;
    private static bool _bluetoothReady;
    private static bool _storageReady;
    private static bool _controllers;
    private static DateTime _controllersSince;

    /// <summary>Un appareil vient d'arriver (thread UI).</summary>
    public static event Action<Arrival>? Arrived;

    public static bool IsRunning => _bluetooth != null;

    public static void Start()
    {
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        SettingsStore.Changed -= Apply;
        Watch(false);
    }

    private static void Apply() =>
        Watch(SettingsStore.Current.Island.DeviceNotices && !SettingsStore.IsReadOnly && !ModuleGuard.IsDisabled(ModuleGuard.Notices));

    private static void Watch(bool on)
    {
        if (on == IsRunning) return;
        if (!on)
        {
            foreach (var watcher in new[] { _bluetooth, _storage })
                if (watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted }) watcher.Stop();
            _bluetooth = _storage = null;
            if (_controllers) RawGameController.RawGameControllerAdded -= OnControllerAdded;
            _controllers = false;
            return;
        }

        // Bluetooth : appareils jumelés (classique et basse consommation), on suit leur état « connecté ».
        Connected.Clear();
        _bluetoothReady = false;
        _bluetooth = DeviceInformation.CreateWatcher(PairedBluetooth, [IsConnectedKey, AddressKey, MajorClassKey], DeviceInformationKind.AssociationEndpoint);
        _bluetooth.Added += (_, info) => OnBluetooth(info.Id, info.Name, info.Properties);
        _bluetooth.Updated += (_, update) => OnBluetooth(update.Id, null, update.Properties);
        _bluetooth.EnumerationCompleted += (_, _) => _bluetoothReady = true;
        _bluetooth.Start();

        // Clés USB, cartes mémoire : le lecteur apparaît un peu après l'appareil.
        Drives.Clear();
        foreach (var drive in RemovableDrives()) Drives.Add(drive.Name);
        _storageReady = false;
        _storage = DeviceInformation.CreateWatcher(Windows.Devices.Portable.StorageDevice.GetDeviceSelector());
        _storage.Added += (_, _) =>
        {
            if (_storageReady) _ = FindNewDrivesAsync();
        };
        _storage.EnumerationCompleted += (_, _) => _storageReady = true;
        _storage.Start();

        // Manettes : Windows.Gaming.Input voit les manettes XInput (Xbox filaire) comme les HID.
        _ = RawGameController.RawGameControllers.Count; // déclenche l'énumération : celles déjà branchées ne sont pas annoncées
        _controllersSince = DateTime.UtcNow;
        RawGameController.RawGameControllerAdded += OnControllerAdded;
        _controllers = true;
    }

    private static void OnBluetooth(string id, string? name, IReadOnlyDictionary<string, object> properties)
    {
        if (!properties.TryGetValue(IsConnectedKey, out var value) || value is not bool connected) return;
        bool was = Connected.TryGetValue(id, out var before) && before;
        Connected[id] = connected;
        if (!_bluetoothReady || !connected || was) return;
        properties.TryGetValue(AddressKey, out var address);
        properties.TryGetValue(MajorClassKey, out var major);
        _ = AnnounceBluetoothAsync(id, name, address as string, major is byte or int or uint ? Convert.ToInt32(major) : -1);
    }

    private static async Task AnnounceBluetoothAsync(string id, string? name, string? address, int majorClass)
    {
        try
        {
            if (name == null)
            {
                var info = await DeviceInformation.CreateFromIdAsync(id, [AddressKey, MajorClassKey], DeviceInformationKind.AssociationEndpoint);
                name = info.Name;
                if (address == null && info.Properties.TryGetValue(AddressKey, out var found)) address = found as string;
                if (majorClass < 0 && info.Properties.TryGetValue(MajorClassKey, out var major) && major != null) majorClass = Convert.ToInt32(major);
            }
            // Classe Bluetooth majeure 4 = audio / vidéo (casque, écouteurs, enceinte).
            var kind = majorClass == 4 ? Kind.Audio : Kind.Bluetooth;
            await Task.Delay(1500); // la batterie n'est publiée qu'une fois les services du casque connectés
            Raise(new Arrival(kind, name ?? "Bluetooth", address != null ? await BatteryAsync(address) : null, null));
        }
        catch (Exception ex)
        {
            Log.Error("Appareils : Bluetooth", ex);
        }
    }

    /// <summary>Batterie publiée par Windows sur un des périphériques de cet appareil (souvent l'audio « mains libres »).</summary>
    private static async Task<int?> BatteryAsync(string address)
    {
        try
        {
            var plain = address.Replace(":", "").ToUpperInvariant();
            var devices = await DeviceInformation.FindAllAsync($"System.Devices.DeviceInstanceId:~~\"{plain}\"", [BatteryKey], DeviceInformationKind.Device);
            foreach (var device in devices)
                if (device.Properties.TryGetValue(BatteryKey, out var level) && level != null && Convert.ToInt32(level) is >= 0 and <= 100 and var percent)
                    return percent;
        }
        catch (Exception ex)
        {
            Log.Info($"Appareils : batterie Bluetooth illisible ({ex.Message})");
        }
        return null;
    }

    private static void OnControllerAdded(object? sender, RawGameController controller)
    {
        if (DateTime.UtcNow - _controllersSince < TimeSpan.FromSeconds(3)) return; // manettes déjà branchées au démarrage
        int? battery = null;
        try
        {
            var report = controller.TryGetBatteryReport();
            if (report?.RemainingCapacityInMilliwattHours is int remaining && report.FullChargeCapacityInMilliwattHours is int full && full > 0)
                battery = (int)Math.Round(100.0 * remaining / full);
        }
        catch (Exception)
        {
            // Batterie inconnue (manette filaire…).
        }
        var name = string.IsNullOrWhiteSpace(controller.DisplayName) ? null : controller.DisplayName;
        Raise(new Arrival(Kind.Controller, name ?? Loc.T("device.controller"), battery, null));
    }

    private static async Task FindNewDrivesAsync()
    {
        foreach (var delay in new[] { 800, 2500 })
        {
            await Task.Delay(delay);
            foreach (var drive in RemovableDrives())
            {
                if (!Drives.Add(drive.Name)) continue;
                string label = "";
                try
                {
                    label = drive.VolumeLabel;
                }
                catch (IOException)
                {
                    // Pas de nom de volume.
                }
                var name = (label.Length > 0 ? label : Loc.T("device.usb")) + $" ({drive.Name.TrimEnd('\\')})";
                Raise(new Arrival(Kind.Storage, name, null, drive.Name));
            }
        }
        // Lecteurs retirés : oubliés, pour être annoncés à nouveau la prochaine fois.
        Drives.IntersectWith(RemovableDrives().Select(d => d.Name).ToList());
    }

    private static IEnumerable<DriveInfo> RemovableDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ready;
            try
            {
                ready = drive.DriveType == DriveType.Removable && drive.IsReady;
            }
            catch (IOException)
            {
                ready = false;
            }
            if (ready) yield return drive;
        }
    }

    /// <summary>Annonce sur le thread UI, une seule fois par appareil en 10 s (Bluetooth et manette peuvent signaler la même).</summary>
    private static void Raise(Arrival arrival)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var now = DateTime.UtcNow;
            if (Recent.TryGetValue(arrival.Name, out var last) && now - last < TimeSpan.FromSeconds(10)) return;
            Recent[arrival.Name] = now;
            Log.Info($"Appareil connecté : {arrival.Kind}{(arrival.Battery is { } b ? $", batterie {b} %" : "")}");
            Arrived?.Invoke(arrival);
        });
    }

    /// <summary>--self-test : ce que voient les requêtes (lecture seule, aucune annonce).</summary>
    internal static async Task<string> DescribeAsync()
    {
        var bluetooth = await DeviceInformation.FindAllAsync(PairedBluetooth, [IsConnectedKey], DeviceInformationKind.AssociationEndpoint);
        int connected = bluetooth.Count(d => d.Properties.TryGetValue(IsConnectedKey, out var v) && v is true);
        var storage = await DeviceInformation.FindAllAsync(Windows.Devices.Portable.StorageDevice.GetDeviceSelector());
        return $"Bluetooth jumelés : {bluetooth.Count} (connectés : {connected}), stockages amovibles : {storage.Count}, "
               + $"lecteurs amovibles : {RemovableDrives().Count()}, manettes : {RawGameController.RawGameControllers.Count}";
    }

    /// <summary>Ouvre une clé USB annoncée dans l'Explorateur.</summary>
    public static void OpenDrive(string drive) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{drive}\"") { UseShellExecute = true })?.Dispose();
}
