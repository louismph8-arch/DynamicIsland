using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Réseaux du PC pour la télécommande (6D) : adresses IPv4 de chaque carte réseau active avec le type de réseau choisi dans
/// Windows (« Privé » / « Public » / domaine, lu par le Network List Manager, sans droits administrateur). La télécommande
/// n'écoute QUE sur les réseaux « Privé » (comme la règle du pare-feu) : rien n'est ouvert sur un Wi-Fi public.
/// </summary>
public static class LocalNetwork
{
    public enum Category { Public = 0, Private = 1, Domain = 2, Unknown = -1 }

    public sealed record Address(IPAddress Ip, string Interface, bool Wireless, Category Category)
    {
        public bool Trusted => Category == Category.Private;
        /// <summary>Carte reliée à une passerelle (la box) : celle que le téléphone voit sur le Wi-Fi de la maison.</summary>
        public bool HasGateway { get; init; }
    }

    /// <summary>Adresses IPv4 des cartes actives (hors boucle locale), réseaux de confiance d'abord.</summary>
    public static List<Address> Addresses()
    {
        var categories = Categories();
        var result = new List<Address>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var category = Guid.TryParse(nic.Id, out var id) && categories.TryGetValue(id, out var found) ? found : Category.Unknown;
            var properties = nic.GetIPProperties();
            bool hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in properties.UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ip) || IsLinkLocal(ip)) continue;
                result.Add(new Address(ip, nic.Name, nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, category) { HasGateway = hasGateway });
            }
        }
        // La carte de la box (avec passerelle) d'abord : c'est elle que le téléphone voit sur le Wi-Fi de la maison.
        return result.OrderByDescending(a => a.Trusted).ThenByDescending(a => a.HasGateway).ToList();
    }

    /// <summary>Le téléphone qui se connecte est-il sur le réseau local (adresses privées, liens locaux) ?</summary>
    public static bool IsLocalPeer(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork) return IsPrivate(address) || IsLinkLocal(address);
        return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
    }

    /// <summary>10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168);
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    /// <summary>Type de réseau de chaque carte (identifiant de carte → catégorie) ; vide si Windows ne répond pas.</summary>
    private static Dictionary<Guid, Category> Categories()
    {
        var result = new Dictionary<Guid, Category>();
        object? manager = null;
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!);
            var connections = ((INetworkListManager)manager!).GetNetworkConnections();
            while (connections.Next(1, out var connection, out uint fetched) == 0 && fetched == 1 && connection != null)
            {
                try
                {
                    var network = connection.GetNetwork();
                    result[connection.GetAdapterId()] = (Category)network.GetCategory();
                    Marshal.ReleaseComObject(network);
                }
                finally
                {
                    Marshal.ReleaseComObject(connection);
                }
            }
            Marshal.ReleaseComObject(connections);
        }
        catch (Exception ex)
        {
            Log.Error("Télécommande : types de réseau", ex);
        }
        finally
        {
            if (manager != null) Marshal.ReleaseComObject(manager);
        }
        return result;
    }

    // ------------------------------------------------------------------ Network List Manager (netlistmgr.h)

    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        [return: MarshalAs(UnmanagedType.Interface)] object GetNetworks(int flags);
        INetwork GetNetwork(Guid networkId);
        IEnumNetworkConnections GetNetworkConnections();
    }

    [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.Interface)] object NewEnum();
        [PreserveSig] int Next(uint count, out INetworkConnection? connection, out uint fetched);
    }

    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        INetwork GetNetwork();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsConnectedToInternet();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsConnected();
        int GetConnectivity();
        Guid GetConnectionId();
        Guid GetAdapterId();
    }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetName([MarshalAs(UnmanagedType.BStr)] string name);
        [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
        Guid GetNetworkId();
        int GetDomainType();
        IEnumNetworkConnections GetNetworkConnections();
        void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsConnectedToInternet();
        [return: MarshalAs(UnmanagedType.VariantBool)] bool IsConnected();
        int GetConnectivity();
        int GetCategory();
    }
}
