using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services.Friends;

/// <summary>
/// Amis en ligne (6D-4, module « friends », désactivé par défaut) : ce PC dit au serveur de l'utilisateur s'il est en ligne
/// ou en jeu (et à quoi, si l'option est choisie) toutes les 2 minutes et à chaque début / fin de partie, et lit la liste de
/// ses amis mutuels (toutes les minutes, toutes les 20 s quand l'île ou les paramètres la montrent). Invisible = plus rien
/// n'est partagé. Coupé : la présence est effacée du serveur ; le compte (code d'ami) reste pour la prochaine fois.
/// </summary>
public static partial class FriendsService
{
    public enum State { Off, NoServer, Connecting, Connected, ServerUnreachable }

    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollWatched = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NotifyAgainAfter = TimeSpan.FromMinutes(20);

    private static DispatcherTimer? _heartbeat, _poll;
    private static FriendsAccount? _account;
    private static bool _started, _busy, _retrying, _firstList = true;
    private static int _watchers;
    private static string _sent = "";
    private static readonly Dictionary<string, DateTime> Notified = [];

    /// <summary>État, liste d'amis ou demandes changés (thread de l'interface).</summary>
    public static event Action? Changed;

    /// <summary>Un ami vient de se connecter ou de lancer un jeu (pour l'annonce dans l'île).</summary>
    public static event Action<Friend>? CameOnline;

    /// <summary>Message rapide reçu d'un ami (ni bloqué, ni coupé) : annonce dans l'île (thread de l'interface).</summary>
    public static event Action<FriendMessage>? MessageReceived;

    /// <summary>Un message plus vieux que ça (resté sur le serveur pendant qu'Opaline était fermée) n'est plus annoncé.</summary>
    private static readonly TimeSpan MessageTooOld = TimeSpan.FromMinutes(30);

    public static State Status { get; private set; }
    public static FriendLists Lists { get; private set; } = FriendLists.Empty;
    /// <summary>Mon code d'ami (« ABCD-EFGH »), null tant que ce PC n'est pas inscrit.</summary>
    public static string? MyCode => _account is { } account ? FriendsApi.FormatCode(account.Id) : null;

    private static FriendsSettings Settings => SettingsStore.Current.Friends;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        _heartbeat = new DispatcherTimer { Interval = HeartbeatEvery };
        _heartbeat.Tick += (_, _) => _ = HeartbeatAsync();
        _poll = new DispatcherTimer { Interval = PollEvery };
        _poll.Tick += (_, _) => _ = PollAsync();
        SettingsStore.Changed += OnSettingsChanged;
        GamingMode.Changed += OnGamingChanged;
        _ = ApplyAsync();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SettingsStore.Changed -= OnSettingsChanged;
        GamingMode.Changed -= OnGamingChanged;
        _heartbeat?.Stop();
        _poll?.Stop();
        GoOffline();
        Status = State.Off;
        Changed?.Invoke();
    }

    /// <summary>L'île (onglet Amis) ou les paramètres montrent la liste : on la relit plus souvent.</summary>
    public static void Watch(bool watching)
    {
        _watchers = Math.Max(0, _watchers + (watching ? 1 : -1));
        if (_poll == null) return;
        _poll.Interval = _watchers > 0 ? PollWatched : PollEvery;
        if (watching && Status == State.Connected) _ = PollAsync();
    }

    private static bool Wanted => Settings.Enabled && !App.SafeMode && !ModuleGuard.IsDisabled(ModuleGuard.Friends);

    private static void OnSettingsChanged()
    {
        var key = $"{Wanted}|{Settings.Server}|{Settings.Name}|{Settings.Invisible}|{Settings.ShareGame}";
        if (key == _settingsKey) return;
        _settingsKey = key;
        _ = ApplyAsync();
    }

    private static string _settingsKey = "";

    private static void OnGamingChanged()
    {
        if (Status == State.Connected) _ = HeartbeatAsync();
    }

    /// <summary>Réglages changés (ou démarrage) : inscription si besoin, présence, liste.</summary>
    private static async Task ApplyAsync()
    {
        if (!Wanted)
        {
            if (Status is State.Connected) GoOffline();
            _heartbeat?.Stop();
            _poll?.Stop();
            Status = State.Off;
            Lists = FriendLists.Empty;
            Changed?.Invoke();
            return;
        }
        if (!FriendsApi.ValidServer(Settings.Server))
        {
            Status = State.NoServer;
            Changed?.Invoke();
            return;
        }
        Status = State.Connecting;
        Changed?.Invoke();
        try
        {
            _account = FriendsAccount.Load();
            if (_account == null || _account.Server != Settings.Server)
            {
                _account = await FriendsApi.RegisterAsync(Settings.Server, Settings.Name);
                _account.Save();
                Log.Info("Amis : ce PC est inscrit sur le serveur des amis");
            }
            _sent = "";
            await HeartbeatAsync();
            await PollAsync();
            _heartbeat!.Start();
            _poll!.Start();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.FriendsRefusedException)
        {
            Status = State.ServerUnreachable;
            Log.Info($"Amis : serveur injoignable ({ex.GetType().Name})");
            _poll!.Start(); // on réessaie à la prochaine relecture
            Changed?.Invoke();
        }
    }

    /// <summary>« Je suis en ligne / en jeu » (ou plus rien si invisible).</summary>
    private static async Task HeartbeatAsync()
    {
        if (_account is not { } account || !Wanted) return;
        bool game = GamingMode.IsActive;
        string? gameName = game && Settings.ShareGame ? GameName(GamingMode.Game) : null;
        _sent = $"{game}|{gameName}|{Settings.Invisible}";
        try
        {
            await FriendsApi.UpdateMeAsync(account, Settings.Name, game ? "game" : "online", gameName, Settings.Invisible);
        }
        catch (FriendsApi.AuthException)
        {
            ForgetLocalAccount(); // serveur remis à zéro : on se réinscrit
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            SetStatus(State.ServerUnreachable);
        }
    }

    private static async Task PollAsync()
    {
        if (_busy || !Wanted) return;
        if (_account == null || Status == State.ServerUnreachable)
        {
            if (!_retrying) await RetryAsync(); // serveur revenu ? on se reconnecte
            return;
        }
        _busy = true;
        try
        {
            var account = _account;
            var lists = await FriendsApi.ListAsync(account);
            if (lists.Extended) await SyncBlockedAsync(account, lists);
            lists = Filter(lists);
            AnnounceArrivals(lists);
            Lists = lists;
            SetStatus(State.Connected);
            Changed?.Invoke();
            if (lists.Extended) AnnounceMessages(await FriendsApi.ReadMessagesAsync(account));
        }
        catch (FriendsApi.AuthException)
        {
            ForgetLocalAccount();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.FriendsRefusedException)
        {
            SetStatus(State.ServerUnreachable);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task RetryAsync()
    {
        _retrying = true;
        try
        {
            await ApplyAsync();
        }
        finally
        {
            _retrying = false;
        }
    }

    /// <summary>Hors ligne → en ligne / en jeu : une annonce, au plus une toutes les 20 minutes par ami (pas au démarrage).</summary>
    private static void AnnounceArrivals(FriendLists lists)
    {
        bool first = _firstList;
        _firstList = false;
        if (first || !Settings.Notify) return;
        var before = Lists.Friends.ToDictionary(f => f.Code);
        foreach (var friend in lists.Friends.Where(f => f.Online && !IsMuted(f.Code)))
        {
            var previous = before.GetValueOrDefault(friend.Code);
            bool arrived = previous is not { Online: true } || previous.Status != "game" && friend.Status == "game";
            if (!arrived) continue;
            if (Notified.TryGetValue(friend.Code, out var last) && DateTime.UtcNow - last < NotifyAgainAfter) continue;
            Notified[friend.Code] = DateTime.UtcNow;
            CameOnline?.Invoke(friend);
        }
    }

    /// <summary>Messages reçus : un par ami (le plus récent), sauf bloqué, coupé, trop vieux ou option désactivée.</summary>
    private static void AnnounceMessages(IReadOnlyList<FriendMessage> messages)
    {
        if (messages.Count == 0 || !Settings.Messages) return;
        var blocked = BlockedCodes();
        foreach (var message in messages
                     .Where(m => !blocked.Contains(m.Code) && !IsMuted(m.Code) && DateTimeOffset.UtcNow - m.At < MessageTooOld)
                     .GroupBy(m => m.Code).Select(g => g.Last()))
        {
            Log.Info("Amis : message rapide reçu"); // ni l'ami, ni le message dans le journal
            MessageReceived?.Invoke(message);
        }
    }

    // ------------------------------------------------------------------ blocage et annonces coupées (correctifs 23-24)

    private static HashSet<string> BlockedCodes() => Settings.Blocked.Select(b => b.Code).ToHashSet();

    /// <summary>
    /// Les personnes bloquées sur ce PC sont cachées partout, même avec un serveur ancien (qui ne sait pas bloquer : le lien
    /// est alors seulement coupé, et une nouvelle demande de leur part reste invisible ici).
    /// </summary>
    private static FriendLists Filter(FriendLists lists)
    {
        var blocked = BlockedCodes();
        return lists with
        {
            Friends = lists.Friends.Where(f => !blocked.Contains(f.Code)).ToList(),
            Incoming = lists.Incoming.Where(f => !blocked.Contains(f.Code)).ToList(),
            Outgoing = lists.Outgoing.Where(f => !blocked.Contains(f.Code)).ToList(),
            Blocked = Settings.Blocked.Select(b => new Friend(b.Code, b.Name, "offline", null)).ToList(),
        };
    }

    /// <summary>
    /// Serveur version 3 : un blocage fait sur ce PC quand le serveur ne savait pas encore bloquer lui est envoyé ; un blocage
    /// connu du serveur mais pas de ce PC (réglages remis d'une sauvegarde…) est repris ici.
    /// </summary>
    private static async Task SyncBlockedAsync(FriendsAccount account, FriendLists lists)
    {
        var onServer = lists.Blocked.Select(b => b.Code).ToHashSet();
        foreach (var local in Settings.Blocked.Where(b => !onServer.Contains(b.Code)).ToList())
        {
            try
            {
                await FriendsApi.BlockAsync(account, local.Code);
            }
            catch (FriendsApi.FriendsRefusedException)
            {
                // trop de blocages, code invalide : il reste au moins caché sur ce PC
            }
        }
        var missing = lists.Blocked.Where(b => !BlockedCodes().Contains(b.Code)).ToList();
        if (missing.Count > 0)
            SettingsStore.Update(s => s.Friends.Blocked.AddRange(missing.Select(b => new BlockedFriend { Code = b.Code, Name = b.Name })));
    }

    public static bool IsMuted(string code) => Settings.Muted.Contains(code);

    /// <summary>Couper (ou réactiver) les annonces et les messages d'un ami dans l'île.</summary>
    public static void SetMuted(string code, bool muted)
    {
        SettingsStore.Update(s =>
        {
            s.Friends.Muted.Remove(code);
            if (muted) s.Friends.Muted.Add(code);
        });
        Changed?.Invoke();
    }

    /// <summary>Bloquer : null si c'est fait, sinon le message à afficher.</summary>
    public static async Task<string?> BlockAsync(Friend friend)
    {
        if (_account is not { } account) return Loc.T("friends.error.not_connected");
        try
        {
            if (Lists.Extended) await FriendsApi.BlockAsync(account, friend.Code);
            else await FriendsApi.RemoveAsync(account, friend.Code); // serveur ancien : on coupe au moins le lien
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.AuthException or FriendsApi.FriendsRefusedException)
        {
            Log.Info($"Amis : blocage impossible ({ex.GetType().Name})");
            return Loc.T("friends.error.server");
        }
        SettingsStore.Update(s =>
        {
            s.Friends.Blocked.RemoveAll(b => b.Code == friend.Code);
            s.Friends.Blocked.Add(new BlockedFriend { Code = friend.Code, Name = friend.Name });
            s.Friends.Muted.Remove(friend.Code);
        });
        Log.Info("Amis : une personne bloquée");
        Lists = Filter(Lists);
        Changed?.Invoke();
        await PollAsync();
        return null;
    }

    /// <summary>Débloquer : d'abord sur le serveur (sinon le blocage reviendrait à la relecture suivante), puis ici.</summary>
    public static async Task<string?> UnblockAsync(string code)
    {
        if (_account is { } account && Lists.Extended)
        {
            try
            {
                await FriendsApi.UnblockAsync(account, code);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.AuthException or FriendsApi.FriendsRefusedException)
            {
                Log.Info($"Amis : déblocage impossible ({ex.GetType().Name})");
                return Loc.T("friends.error.server");
            }
        }
        SettingsStore.Update(s => s.Friends.Blocked.RemoveAll(b => b.Code == code));
        Lists = Filter(Lists);
        Changed?.Invoke();
        await PollAsync();
        return null;
    }

    /// <summary>Le serveur sait-il envoyer des messages rapides (version 3) ?</summary>
    public static bool MessagesAvailable => Status == State.Connected && Lists.Extended;

    /// <summary>Message rapide (identifiant de <see cref="QuickMessages"/>) : null si envoyé, sinon le message à afficher.</summary>
    public static async Task<string?> SendMessageAsync(string code, string message)
    {
        if (_account is not { } account) return Loc.T("friends.error.not_connected");
        if (!QuickMessages.IsKnown(message)) return Loc.T("friends.error.server");
        try
        {
            return await FriendsApi.SendMessageAsync(account, code, message) switch
            {
                null => null,
                "not_friend" => Loc.T("friends.error.not_friend"),
                "too_many" => Loc.T("friends.error.too_many"),
                _ => Loc.T("friends.error.server"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.AuthException)
        {
            return Loc.T("friends.error.server");
        }
    }

    /// <summary>« Joue à Valorant », « En ligne »… ; avec <paramref name="since"/>, « … depuis 14:32 » si le serveur le sait.</summary>
    public static string StatusText(Friend friend, bool since = false)
    {
        var text = friend.Status switch
        {
            "game" => friend.Game is { Length: > 0 } game ? Loc.F("friends.status.game", game) : Loc.T("friends.status.game_hidden"),
            "online" => Loc.T("friends.status.online"),
            _ => Loc.T("friends.status.offline"),
        };
        if (!since || !friend.Online || friend.Since is not { } start) return text;
        var local = start.ToLocalTime();
        var when = local.Date == DateTime.Today ? local.ToString("t", Loc.Culture) : local.ToString("g", Loc.Culture);
        return Loc.F("friends.status.since", text, when);
    }

    private static void SetStatus(State state)
    {
        if (Status == state) return;
        Status = state;
        Changed?.Invoke();
    }

    private static void ForgetLocalAccount()
    {
        Log.Info("Amis : compte inconnu du serveur, nouvelle inscription");
        FriendsAccount.Delete();
        _account = null;
        _ = ApplyAsync();
    }

    // ------------------------------------------------------------------ actions des paramètres

    /// <summary>Ajouter (ou accepter) un code d'ami : null si c'est fait, sinon le message à afficher.</summary>
    public static async Task<string?> AddAsync(string code)
    {
        if (_account is not { } account) return Loc.T("friends.error.not_connected");
        try
        {
            var refused = await FriendsApi.AddAsync(account, code);
            var clean = code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (refused == null && Settings.Blocked.Any(b => b.Code == clean))
                SettingsStore.Update(s => s.Friends.Blocked.RemoveAll(b => b.Code == clean)); // rajouter = débloquer (comme le serveur)
            await PollAsync();
            return refused switch
            {
                null => null,
                "unknown" => Loc.T("friends.error.unknown"),
                "full" => Loc.T("friends.error.full"),
                _ => Loc.T("friends.error.code"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.AuthException)
        {
            return Loc.T("friends.error.server");
        }
    }

    public static async Task RemoveAsync(string code)
    {
        if (_account is not { } account) return;
        try
        {
            await FriendsApi.RemoveAsync(account, code);
            await PollAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.FriendsRefusedException or FriendsApi.AuthException)
        {
            Log.Info($"Amis : retrait impossible ({ex.GetType().Name})");
        }
    }

    /// <summary>« Quitter les amis en ligne » : tout est effacé du serveur et de ce PC, la fonction est coupée.</summary>
    public static async Task LeaveAsync()
    {
        if (_account is { } account)
        {
            try
            {
                await FriendsApi.ForgetAsync(account);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FriendsApi.AuthException or FriendsApi.FriendsRefusedException)
            {
                Log.Info($"Amis : effacement sur le serveur impossible ({ex.GetType().Name})");
            }
        }
        FriendsAccount.Delete();
        _account = null;
        Lists = FriendLists.Empty;
        SettingsStore.Update(s =>
        {
            s.Friends.Enabled = false;
            s.Friends.Blocked.Clear(); // le compte n'existe plus : ses blocages non plus
            s.Friends.Muted.Clear();
        });
        Log.Info("Amis : compte effacé");
    }

    /// <summary>Texte à envoyer à un ami (adresse du serveur + mon code).</summary>
    public static string? InviteText() =>
        MyCode is { } code ? Loc.F("friends.invite", Settings.Server, code) : null;

    /// <summary>Présence effacée tout de suite (fermeture d'Opaline, fonction coupée) ; sinon elle expire en 10 min.</summary>
    public static void GoOffline()
    {
        if (_account is not { } account) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            Task.Run(() => FriendsApi.UpdateMeAsync(account, Settings.Name, "offline", null, true, timeout.Token)).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is AggregateException or HttpRequestException or TaskCanceledException)
        {
            // pas grave : la présence s'efface d'elle-même au bout de 10 minutes
        }
    }

    // ------------------------------------------------------------------ nom du jeu

    [GeneratedRegex(@"(?i)[-_ .]?(win64|win32|x64|x86|shipping|dx11|dx12|launcher|client|retail)\b")]
    private static partial Regex Noise();

    /// <summary>
    /// Nom lisible du jeu : celui écrit dans l'exe (« VALORANT », « Counter-Strike 2 »), sinon le nom du processus nettoyé
    /// (« valorant-win64-shipping » → « Valorant »).
    /// </summary>
    internal static string? GameName(string? process)
    {
        if (string.IsNullOrWhiteSpace(process)) return null;
        try
        {
            foreach (var running in Process.GetProcessesByName(process))
            {
                using (running)
                {
                    var path = running.MainModule?.FileName;
                    if (path == null) continue;
                    var info = FileVersionInfo.GetVersionInfo(path);
                    var described = new[] { info.ProductName, info.FileDescription }
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && !Regex.IsMatch(n, "(?i)unreal|microsoft|windows|unity|launcher|bootstrap"));
                    if (described != null) return described.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            // jeu protégé (anti-triche) : on garde le nom du processus
        }
        return PrettyName(process);
    }

    internal static string PrettyName(string process)
    {
        var name = Regex.Replace(Noise().Replace(process, ""), "[-_.]+", " ").Trim();
        if (name.Length == 0) name = process;
        return CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(name.ToLowerInvariant());
    }
}
