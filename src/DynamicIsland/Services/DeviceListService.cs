using System.IO;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Gaming.Input;
using Windows.Media.Devices;

namespace DynamicIsland.Services;

/// <summary>
/// Correctif urgent 8 : appareils connectés en ce moment, pour le centre de contrôle de l'île — Bluetooth connectés (casque,
/// souris, clavier…), casques USB ou à dongle (sorties audio « casque » / « écouteurs »), souris et claviers USB ou à dongle,
/// manettes, clés USB. Batterie seulement si Windows la donne (Bluetooth récent, manettes). Lecture seule, à la demande : relue
/// seulement pendant que l'onglet Contrôles est ouvert. Le PC lui-même (clavier d'un portable…) n'est jamais listé.
/// </summary>
public static class DeviceListService
{
    public enum Kind { Headset, Mouse, Keyboard, Controller, Storage, Bluetooth }

    /// <summary>Appareil : type, nom, batterie en % (null si inconnue), lecteur (clé USB, pour l'ouvrir).</summary>
    public sealed record Device(Kind Kind, string Name, int? Battery, string? Drive);

    private const string ContainerKey = "System.Devices.ContainerId";
    /// <summary>PKEY_AudioEndpoint_FormFactor : 3 = casque d'écoute, 5 = casque micro.</summary>
    private const string FormFactorKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e} 0";
    /// <summary>Conteneur « cet ordinateur » : tout ce qui est intégré au PC.</summary>
    private static readonly Guid LocalMachine = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>Liste actuelle (quelques requêtes à Windows, hors du thread de l'interface ; ~ dizaines de ms).</summary>
    public static async Task<IReadOnlyList<Device>> SnapshotAsync()
    {
        var devices = new List<Device>();
        var containers = new HashSet<Guid>();
        await Safe(() => AddBluetoothAsync(devices, containers), "Bluetooth");
        await Safe(() => AddHeadsetsAsync(devices, containers), "casques");
        await Safe(() => AddHidAsync(devices, containers, 0x02, Kind.Mouse), "souris");
        await Safe(() => AddHidAsync(devices, containers, 0x06, Kind.Keyboard), "claviers");
        await Safe(() => Task.Run(() => AddControllers(devices)), "manettes");
        await Safe(() => Task.Run(() => AddDrives(devices)), "clés USB");
        return devices;
    }

    private static async Task Safe(Func<Task> read, string what)
    {
        try
        {
            await read();
        }
        catch (Exception ex)
        {
            Log.Info($"Appareils connectés : {what} illisibles ({ex.Message})");
        }
    }

    private static async Task AddBluetoothAsync(List<Device> devices, HashSet<Guid> containers)
    {
        var paired = await DeviceInformation.FindAllAsync(DeviceNotifier.PairedBluetooth,
            [DeviceNotifier.IsConnectedKey, DeviceNotifier.AddressKey, DeviceNotifier.MajorClassKey, "System.Devices.Aep.ContainerId"],
            DeviceInformationKind.AssociationEndpoint);
        foreach (var info in paired)
        {
            if (!(info.Properties.TryGetValue(DeviceNotifier.IsConnectedKey, out var connected) && connected is true)) continue;
            if (info.Properties.TryGetValue("System.Devices.Aep.ContainerId", out var container) && container is Guid id && !containers.Add(id)) continue;
            int major = info.Properties.TryGetValue(DeviceNotifier.MajorClassKey, out var value) && value != null ? Convert.ToInt32(value) : -1;
            var kind = major == 4 ? Kind.Headset : Kind.Bluetooth; // 4 = audio ; souris, clavier… = icône Bluetooth
            int? battery = info.Properties.TryGetValue(DeviceNotifier.AddressKey, out var address) && address is string mac
                ? await DeviceNotifier.BatteryAsync(mac)
                : null;
            if (!string.IsNullOrWhiteSpace(info.Name)) devices.Add(new Device(kind, info.Name.Trim(), battery, null));
        }
    }

    /// <summary>Sorties audio actives de type casque (USB, dongle 2,4 GHz…) : nommées d'après leur appareil.</summary>
    private static async Task AddHeadsetsAsync(List<Device> devices, HashSet<Guid> containers)
    {
        var outputs = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(), [FormFactorKey, ContainerKey]);
        foreach (var output in outputs)
        {
            if (!output.IsEnabled) continue;
            if (!(output.Properties.TryGetValue(FormFactorKey, out var form) && form != null && Convert.ToInt32(form) is 3 or 5)) continue;
            if (await ContainerNameAsync(output, containers) is { } name) devices.Add(new Device(Kind.Headset, name, null, null));
        }
    }

    /// <summary>Souris (usage HID 0x02) ou claviers (0x06) qui ne font pas partie du PC.</summary>
    private static async Task AddHidAsync(List<Device> devices, HashSet<Guid> containers, ushort usage, Kind kind)
    {
        var found = await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(0x01, usage), [ContainerKey]);
        foreach (var info in found)
            if (await ContainerNameAsync(info, containers) is { } name) devices.Add(new Device(kind, name, null, null));
    }

    /// <summary>Nom de l'appareil entier (« PRO X 2 LIGHTSPEED » plutôt que « Souris HID »), une seule fois par appareil.</summary>
    private static async Task<string?> ContainerNameAsync(DeviceInformation info, HashSet<Guid> containers)
    {
        if (!(info.Properties.TryGetValue(ContainerKey, out var value) && value is Guid id) || id == LocalMachine || id == Guid.Empty) return null;
        if (!containers.Add(id)) return null;
        try
        {
            var container = await DeviceInformation.CreateFromIdAsync("{" + id + "}", [], DeviceInformationKind.DeviceContainer);
            return string.IsNullOrWhiteSpace(container.Name) ? null : container.Name.Trim();
        }
        catch (Exception)
        {
            return string.IsNullOrWhiteSpace(info.Name) ? null : info.Name.Trim();
        }
    }

    private static void AddControllers(List<Device> devices)
    {
        foreach (var controller in RawGameController.RawGameControllers)
        {
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
            var name = string.IsNullOrWhiteSpace(controller.DisplayName) ? Loc.T("device.controller") : controller.DisplayName.Trim();
            // Une manette Bluetooth est déjà listée par son nom : pas deux fois.
            if (!devices.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
                devices.Add(new Device(Kind.Controller, name, battery, null));
        }
    }

    private static void AddDrives(List<Device> devices)
    {
        foreach (var drive in DeviceNotifier.RemovableDrives())
        {
            string label = "";
            try
            {
                label = drive.VolumeLabel;
            }
            catch (IOException)
            {
                // Pas de nom de volume.
            }
            devices.Add(new Device(Kind.Storage, (label.Length > 0 ? label : Loc.T("device.usb")) + $" ({drive.Name.TrimEnd('\\')})", null, drive.Name));
        }
    }

    /// <summary>--self-test : ce que la liste voit (lecture seule).</summary>
    internal static async Task<string> DescribeAsync()
    {
        var devices = await SnapshotAsync();
        return devices.Count == 0 ? "aucun" : string.Join(", ", devices.GroupBy(d => d.Kind).Select(g => $"{g.Key} × {g.Count()}"));
    }
}
