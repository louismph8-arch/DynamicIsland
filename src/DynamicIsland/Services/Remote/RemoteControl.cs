using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Télécommande mobile (6D, module « remote ») : quand elle est activée, sert la page du téléphone sur les adresses des
/// réseaux « Privé » du PC seulement, et suit les changements de réseau (toutes les 20 s et à chaque changement d'adresse).
/// Désactivée par défaut, jamais en mode sans échec. Rien ne passe par Internet.
/// </summary>
public static class RemoteControl
{
    public enum Status { Off, Listening, NoPrivateNetwork, PortBusy }

    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(20);

    private static RemoteServer? _server;
    private static RemoteApi? _api;
    private static DispatcherTimer? _timer;
    private static bool _started;
    private static string _bound = "";

    /// <summary>État, adresse, appairage ou téléphones changés (thread de l'interface).</summary>
    public static event Action? Changed;

    public static Status State { get; private set; }

    /// <summary>Réseaux actifs réglés sur « Public » (pour expliquer pourquoi le téléphone ne voit rien).</summary>
    public static IReadOnlyList<LocalNetwork.Address> PublicNetworks { get; private set; } = [];

    /// <summary>Adresse à ouvrir sur le téléphone (« http://192.168.1.20:48765/ »), null si rien n'écoute.</summary>
    public static string? Url { get; private set; }

    public static string? PairingCode => _api?.PairingOpen == true ? _api.PairingCode : null;
    public static DateTime PairingUntil => _api?.PairingUntil ?? DateTime.MinValue;
    public static string? LastPaired => _api?.LastPaired;

    private static RemoteSettings Settings => SettingsStore.Current.Remote;

    public static void Start(IRemoteBackend backend)
    {
        if (_started) return;
        _started = true;
        var uploads = new RemoteUploads(DownloadsService.Folder);
        _ = Task.Run(uploads.CleanStale);
        _api = new RemoteApi(backend,
            () => Settings.Devices,
            device => SettingsStore.Update(s => s.Remote.Devices.Add(device)),
            id => SettingsStore.Update(s => s.Remote.Devices.RemoveAll(d => d.Id == id)),
            uploads);
        _api.Changed += () => Changed?.Invoke();
        _timer = new DispatcherTimer { Interval = Recheck };
        _timer.Tick += (_, _) => Refresh();
        SettingsStore.Changed += Apply;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        Apply();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SettingsStore.Changed -= Apply;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        Close();
        _api = null;
        Changed?.Invoke();
    }

    /// <summary>Ouvre l'appairage pour 5 minutes (bouton « Ajouter un téléphone »).</summary>
    public static void OpenPairing() => _api?.OpenPairing();

    public static void ClosePairing() => _api?.ClosePairing();

    /// <summary>Code expiré ? (appelé chaque seconde par la page des paramètres pendant l'appairage)</summary>
    public static void CheckPairing()
    {
        if (_api is { PairingCode: not null, PairingOpen: false }) _api.ClosePairing();
    }

    private static void Apply()
    {
        bool wanted = Settings.Enabled && !App.SafeMode && !ModuleGuard.IsDisabled(ModuleGuard.Remote);
        if (wanted && _server == null && State == Status.Off) Listen();
        else if (!wanted && State != Status.Off) Close();
    }

    private static void OnAddressChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (State != Status.Off) Refresh();
        });

    /// <summary>Réseau changé (nouvelle adresse, réseau passé en « Privé »…) : on réécoute si les adresses ont changé.</summary>
    private static void Refresh()
    {
        if (!_started || State == Status.Off) return;
        var addresses = LocalNetwork.Addresses();
        var trusted = addresses.Where(a => a.Trusted).Select(a => a.Ip.ToString()).Distinct().ToList();
        if (string.Join(",", trusted) != _bound || State != Status.Listening) Listen(addresses);
    }

    private static void Listen(List<LocalNetwork.Address>? addresses = null)
    {
        addresses ??= LocalNetwork.Addresses();
        var trusted = addresses.Where(a => a.Trusted).Select(a => a.Ip).DistinctBy(ip => ip.ToString()).ToList();
        PublicNetworks = addresses.Where(a => !a.Trusted).ToList();
        _bound = string.Join(",", trusted.Select(ip => ip.ToString()));
        _server ??= new RemoteServer(Dispatch);
        _server.Stop();
        Url = null;
        if (trusted.Count == 0)
        {
            State = Status.NoPrivateNetwork;
            Log.Info($"Télécommande : aucun réseau « Privé » ({PublicNetworks.Count} réseau(x) public(s)), rien n'écoute");
        }
        else
        {
            try
            {
                _server.Start(trusted, Settings.Port);
                State = Status.Listening;
                Url = $"http://{trusted[0]}:{Settings.Port}/";
                Log.Info($"Télécommande : à l'écoute sur {trusted.Count} adresse(s) privée(s), port {Settings.Port}");
            }
            catch (SocketException ex)
            {
                State = Status.PortBusy;
                Log.Error($"Télécommande : port {Settings.Port} indisponible", ex);
            }
        }
        _timer?.Start();
        Changed?.Invoke();
    }

    private static void Close()
    {
        _timer?.Stop();
        _server?.Stop();
        _server = null;
        _api?.ClosePairing();
        _bound = "";
        Url = null;
        State = Status.Off;
        Changed?.Invoke();
    }

    private static Task<RemoteResponse> Dispatch(RemoteRequest request)
    {
        var api = _api;
        if (api == null || Application.Current == null) return Task.FromResult(RemoteResponse.Empty(404));
        return Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                return await api.HandleAsync(request);
            }
            catch (Exception ex)
            {
                if (!ModuleGuard.TryFail(ex)) Log.Error("Télécommande", ex);
                return RemoteResponse.Empty(500);
            }
        }).Task.Unwrap();
    }

    /// <summary>Adresse + code dans le QR : le téléphone s'appaire tout seul en l'ouvrant (le « # » n'est jamais envoyé au PC).</summary>
    public static string? PairingLink => Url != null && PairingCode is { } code ? $"{Url}#p={code}" : null;

    /// <summary>Lien des paramètres réseau de Windows pour passer le réseau en « Privé ».</summary>
    public static string NetworkSettingsUri =>
        PublicNetworks.FirstOrDefault(n => n.HasGateway) is { Wireless: true } ? "ms-settings:network-wifi" : "ms-settings:network-ethernet";
}
