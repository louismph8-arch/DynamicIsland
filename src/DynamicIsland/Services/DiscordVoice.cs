using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>Personne du salon vocal Discord.</summary>
public sealed record VoiceMember(string Id, string Name, string AvatarUrl, bool Muted, bool Deafened, bool Speaking);

/// <summary>
/// Salon vocal Discord dans l'île, par l'API locale officielle de Discord (RPC sur le canal nommé
/// « discord-ipc-N », pas de bot ni de jeton de compte). Il faut une appli créée par l'utilisateur sur le portail
/// développeur (« Public Client » activé : autorisation PKCE, sans clé secrète). Discord demande l'accord une
/// fois ; le jeton est gardé chiffré pour ce compte Windows (DPAPI). Tant que l'appli n'est pas approuvée par
/// Discord, seuls son propriétaire et ses testeurs (50 max.) peuvent l'autoriser.
/// Tout le réseau tourne en arrière-plan ; l'état publié et <see cref="Changed"/> sont sur le thread UI.
/// 6D-1b : aussi « rpc.voice.write », pour couper son micro / son casque depuis la télécommande (comme les boutons de
/// Discord) ; un ancien jeton sans ce droit fait redemander l'accord une fois.
/// </summary>
public static class DiscordVoice
{
    private const string RedirectUri = "http://127.0.0.1/callback";
    private static readonly string[] Scopes = ["rpc", "rpc.voice.read", "rpc.voice.write"];
    private static readonly string[] ChannelEvents = ["VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP"];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static Dispatcher? _ui;
    private static CancellationTokenSource? _wake;
    private static bool _started;
    /// <summary>Autorisation refusée (ou impossible) : on ne redemande pas toute seule, on attend « Se connecter ».</summary>
    private static bool _waitForUser;
    /// <summary>« Accès refusé » déjà noté dans le journal (sinon une ligne toutes les 15 s).</summary>
    private static bool _deniedLogged;
    /// <summary>Le nouveau droit (micro / casque) a été refusé : on reste en lecture seule jusqu'au prochain démarrage.</summary>
    private static bool _writeDeclined;

    public static string Status { get; private set; } = Loc.T("discord.off");
    public static bool IsConnected { get; private set; }
    /// <summary>Nom du salon vocal où l'on est (null si aucun).</summary>
    public static string? Channel { get; private set; }
    public static IReadOnlyList<VoiceMember> Members { get; private set; } = [];
    public static bool InVoice => Channel != null;
    /// <summary>Son micro coupé dans Discord (null tant qu'on ne le sait pas).</summary>
    public static bool? SelfMuted { get; private set; }
    /// <summary>Son casque coupé dans Discord (« sourdine » : coupe aussi le micro).</summary>
    public static bool? SelfDeafened { get; private set; }

    private static Session? _session;

    /// <summary>État changé (thread UI).</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        _ui = Dispatcher.CurrentDispatcher;
        string applied = Key();
        SettingsStore.Changed += () =>
        {
            if (Key() == applied) return;
            applied = Key();
            _waitForUser = false;
            Wake();
        };
        _ = Task.Run(RunAsync);
    }

    /// <summary>Bouton « Se connecter » : relance tout de suite (et redemande l'accord si besoin).</summary>
    public static void Reconnect()
    {
        _waitForUser = false;
        Wake();
    }

    /// <summary>« Oublier l'autorisation » : efface le jeton ; Discord redemandera l'accord.</summary>
    public static void Forget()
    {
        TokenStore.Delete();
        Reconnect();
    }

    private static string Key()
    {
        var discord = SettingsStore.Current.Discord;
        return $"{Enabled}|{discord.ClientId}";
    }

    /// <summary>Activé dans les réglages, et pas arrêté après une erreur.</summary>
    private static bool Enabled => SettingsStore.Current.Discord.Enabled && !ModuleGuard.IsDisabled(ModuleGuard.Discord);

    private static void Wake()
    {
        try
        {
            _wake?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // ------------------------------------------------------------------ boucle de connexion

    private static async Task RunAsync()
    {
        while (true)
        {
            _wake = new CancellationTokenSource();
            var wake = _wake.Token;
            var settings = SettingsStore.Current.Discord;
            try
            {
                if (!Enabled) Publish(Loc.T("discord.off"), connected: false, clearChannel: true);
                else if (!ulong.TryParse(settings.ClientId, out _)) Publish(Loc.T("discord.need_id"), false, true);
                else if (!_waitForUser) await RunSessionAsync(settings.ClientId.Trim(), wake);
            }
            catch (OperationCanceledException)
            {
                // Réglages changés ou « Se connecter » : on recommence tout de suite.
            }
            catch (DiscordClosedException)
            {
                Publish(Loc.T("discord.not_running"), false, true);
            }
            catch (UnauthorizedAccessException)
            {
                // Canal présent mais interdit : Discord tourne en administrateur, une appli normale ne peut pas lui parler.
                Publish(Loc.T("discord.elevated"), false, true);
                if (!_deniedLogged) Log.Info("Discord : accès au canal refusé (Discord lancé en administrateur ?)");
                _deniedLogged = true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException or InvalidDataException)
            {
                Publish(Loc.T("discord.lost"), false, true);
            }
            catch (Exception ex)
            {
                Log.Error("Discord", ex);
                Publish(Loc.F("discord.error", ex.Message), false, true);
            }

            try
            {
                // Attente avant un nouvel essai (ou jusqu'à un changement de réglage / « Se connecter »).
                await Task.Delay(Enabled && !_waitForUser ? TimeSpan.FromSeconds(15) : Timeout.InfiniteTimeSpan, wake);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static async Task RunSessionAsync(string clientId, CancellationToken wake)
    {
        await using var session = await Session.ConnectAsync(clientId, wake);
        _deniedLogged = false;
        Publish(Loc.T("discord.connecting"), false, false);

        if (!await AuthenticateAsync(session, clientId, wake)) return;
        Publish(Loc.T("discord.connected"), true, false);
        Log.Info("Discord : connecté");

        await session.SubscribeAsync("VOICE_SETTINGS_UPDATE", null, wake);
        PublishVoiceSettings(await session.CommandAsync("GET_VOICE_SETTINGS", new JsonObject(), wake));
        await session.SubscribeAsync("VOICE_CHANNEL_SELECT", null, wake);
        var selected = await session.CommandAsync("GET_SELECTED_VOICE_CHANNEL", new JsonObject(), wake);
        await session.WatchChannelAsync(selected, wake);
        Log.Info($"Discord : salon vocal au départ = {(selected?["name"]?.ToString() is { Length: > 0 } name ? name : selected == null ? "aucun" : "appel")}");

        _session = session;
        try
        {
            await session.ReadEventsAsync(wake);
        }
        finally
        {
            _session = null;
            session.FailPending();
        }
    }

    /// <summary>
    /// Couper / remettre son micro (<paramref name="mute"/>) ou son casque (<paramref name="deaf"/>) dans Discord, comme ses
    /// propres boutons. Faux si Discord n'est pas connecté ou refuse.
    /// </summary>
    public static async Task<bool> SetVoiceAsync(bool? mute = null, bool? deaf = null)
    {
        var session = _session;
        if (session == null) return false;
        var args = new JsonObject();
        if (mute is { } m) args["mute"] = m;
        if (deaf is { } d) args["deaf"] = d;
        try
        {
            var reply = await session.QueueAsync(new JsonObject { ["cmd"] = "SET_VOICE_SETTINGS", ["args"] = args }).WaitAsync(TimeSpan.FromSeconds(5));
            if (reply["evt"]?.GetValue<string>() == "ERROR")
            {
                Log.Info($"Discord : micro / casque refusé — {reply["data"]?["message"]}");
                return false;
            }
            PublishVoiceSettings(reply["data"]);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException)
        {
            return false;
        }
    }

    private static void PublishVoiceSettings(JsonNode? data)
    {
        if (data?["mute"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) return;
        bool mute = data["mute"]!.GetValue<bool>();
        bool deaf = data["deaf"]?.GetValueKind() == JsonValueKind.True;
        _ui?.BeginInvoke(() =>
        {
            SelfMuted = mute;
            SelfDeafened = deaf;
            Changed?.Invoke();
        });
    }

    /// <summary>Jeton gardé → AUTHENTICATE ; sinon (ou s'il est refusé) : rafraîchissement, puis accord dans Discord.</summary>
    private static async Task<bool> AuthenticateAsync(Session session, string clientId, CancellationToken wake)
    {
        var token = TokenStore.Load(clientId);
        if (token != null && token.Expires < DateTimeOffset.UtcNow.AddHours(1))
            token = await TryRefreshAsync(clientId, token, wake);
        // Discord refuse un nouvel accord après AUTHENTICATE sur la même connexion : un jeton qui n'a pas tous les droits
        // (ancien jeton d'avant le droit « micro / casque ») fait demander l'accord AVANT de s'en servir.
        bool complete = token != null && Scopes.All(token.Scopes.Contains);
        if (token != null && (complete || _writeDeclined) && await session.TryAuthenticateAsync(token.AccessToken, wake) != null) return true;
        if (token != null && !complete) Log.Info("Discord : jeton sans « rpc.voice.write », accord redemandé (micro / casque depuis la télécommande)");

        Publish(Loc.T("discord.accept"), false, false);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var (code, error) = await session.AuthorizeAsync(clientId, Scopes, challenge, wake);
        if (code == null && token != null && !complete)
        {
            _writeDeclined = true; // pas redemandé avant le prochain démarrage d'Opaline
            Log.Info("Discord : droit « micro / casque » refusé, on garde la lecture seule");
            if (await session.TryAuthenticateAsync(token.AccessToken, wake) != null) return true;
        }
        if (code == null)
        {
            _waitForUser = true; // pas de nouvelle fenêtre d'accord toute seule
            Publish(error ?? Loc.T("discord.denied"), false, true);
            return false;
        }

        var (fresh, exchangeError) = await ExchangeAsync(clientId, code, verifier, wake);
        if (fresh == null || await session.TryAuthenticateAsync(fresh.AccessToken, wake) == null)
        {
            _waitForUser = true;
            Publish(exchangeError ?? Loc.T("discord.token_refused"), false, true);
            return false;
        }
        TokenStore.Save(clientId, fresh);
        return true;
    }

    private static async Task<(Token? Token, string? Error)> ExchangeAsync(string clientId, string code, string verifier, CancellationToken wake)
    {
        using var response = await Http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = RedirectUri,
        }), wake);
        var body = await response.Content.ReadAsStringAsync(wake);
        if (response.IsSuccessStatusCode) return (Token.Parse(body), null);
        Log.Info($"Discord : échange du code refusé ({(int)response.StatusCode}) {body}");
        return (null, body.Contains("invalid_client")
            ? Loc.T("discord.public_client")
            : body.Contains("redirect_uri")
                ? Loc.T("discord.redirect")
                : Loc.F("discord.authorization_refused", (int)response.StatusCode));
    }

    private static async Task<Token?> TryRefreshAsync(string clientId, Token token, CancellationToken wake)
    {
        try
        {
            using var response = await Http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["client_id"] = clientId,
            }), wake);
            if (!response.IsSuccessStatusCode) return null;
            var fresh = Token.Parse(await response.Content.ReadAsStringAsync(wake));
            if (fresh != null) TokenStore.Save(clientId, fresh);
            return fresh;
        }
        catch (HttpRequestException)
        {
            return token; // hors ligne : on essaie quand même l'ancien
        }
    }

    // ------------------------------------------------------------------ publication vers l'interface

    private static void Publish(string status, bool connected, bool clearChannel) => _ui?.BeginInvoke(() =>
    {
        Status = status;
        IsConnected = connected;
        if (!connected)
        {
            SelfMuted = null;
            SelfDeafened = null;
        }
        if (clearChannel)
        {
            Channel = null;
            Members = [];
        }
        Changed?.Invoke();
    });

    private static void PublishChannel(string? channel, IReadOnlyList<VoiceMember> members) => _ui?.BeginInvoke(() =>
    {
        Channel = channel;
        Members = members;
        Changed?.Invoke();
    });

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class DiscordClosedException : Exception;

    /// <summary>Mode --snapshot : un salon d'exemple pour le rendu (rien n'est connecté).</summary>
    internal static void UseDemo()
    {
        Status = Loc.T("discord.connected");
        IsConnected = true;
        Channel = Loc.T("discord.sample_channel");
        Members =
        [
            new("1", "Louis", "https://cdn.discordapp.com/embed/avatars/0.png", false, false, true),
            new("2", "Max", "https://cdn.discordapp.com/embed/avatars/1.png", false, false, false),
            new("3", "Léa", "https://cdn.discordapp.com/embed/avatars/2.png", true, false, false),
            new("4", "Sam", "https://cdn.discordapp.com/embed/avatars/3.png", true, true, false),
        ];
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ une connexion au canal de Discord

    /// <summary>
    /// Trames du canal : opcode (int32) + longueur (int32) + JSON. Un seul lecteur à la fois : les commandes
    /// sont envoyées puis on lit jusqu'à la réponse qui porte le même « nonce », en traitant les événements reçus entre-temps.
    /// </summary>
    private sealed class Session : IAsyncDisposable
    {
        private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;

        private readonly NamedPipeClientStream _pipe;
        private readonly System.Threading.Channels.Channel<(JsonObject Command, TaskCompletionSource<JsonNode> Reply)> _queue = System.Threading.Channels.Channel.CreateUnbounded<(JsonObject, TaskCompletionSource<JsonNode>)>();
        private readonly Dictionary<string, TaskCompletionSource<JsonNode>> _pending = [];
        private readonly Dictionary<string, MemberState> _members = [];
        private readonly List<string> _order = [];
        private string? _channelId;
        private string? _channelName;

        private Session(NamedPipeClientStream pipe) => _pipe = pipe;

        public static async Task<Session> ConnectAsync(string clientId, CancellationToken wake)
        {
            bool denied = false;
            for (int i = 0; i < 10; i++)
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(400, wake);
                }
                catch (Exception ex) when (ex is TimeoutException or UnauthorizedAccessException)
                {
                    // Pas de Discord sur ce numéro, ou un Discord lancé en administrateur : on essaie le suivant.
                    denied |= ex is UnauthorizedAccessException;
                    await pipe.DisposeAsync();
                    continue;
                }
                var session = new Session(pipe);
                try
                {
                    await session.WriteAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId }, wake);
                    var (_, ready) = await session.ReadAsync(wake);
                    if (ready["evt"]?.GetValue<string>() != "READY") throw new ServerClosedException("pas de READY");
                    return session;
                }
                catch (ServerClosedException ex)
                {
                    // Le plus souvent : ID d'application inconnu. Inutile de réessayer en boucle.
                    await session.DisposeAsync();
                    _waitForUser = true;
                    throw new InvalidOperationException(Loc.F("discord.bad_id", ex.Message));
                }
            }
            if (denied) throw new UnauthorizedAccessException();
            throw new DiscordClosedException();
        }

        /// <summary>Droits accordés par ce jeton (null s'il est refusé).</summary>
        public async Task<HashSet<string>?> TryAuthenticateAsync(string accessToken, CancellationToken wake)
        {
            var reply = await SendAsync(new JsonObject { ["cmd"] = "AUTHENTICATE", ["args"] = new JsonObject { ["access_token"] = accessToken } }, wake);
            if (reply["evt"]?.GetValue<string>() == "ERROR") return null;
            return (reply["data"]?["scopes"]?.AsArray() ?? []).Select(s => s?.ToString() ?? "").ToHashSet();
        }

        /// <summary>Commande venue d'ailleurs (télécommande) : envoyée par la boucle de lecture, réponse dans la tâche.</summary>
        public Task<JsonNode> QueueAsync(JsonObject command)
        {
            var reply = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_queue.Writer.TryWrite((command, reply))) reply.SetException(new IOException("Discord déconnecté"));
            return reply.Task;
        }

        public void FailPending()
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var queued)) queued.Reply.TrySetException(new IOException("Discord déconnecté"));
            foreach (var reply in _pending.Values) reply.TrySetException(new IOException("Discord déconnecté"));
            _pending.Clear();
        }

        /// <summary>Fenêtre d'accord dans Discord (PKCE). Renvoie le code, ou un message si refusé.</summary>
        public async Task<(string? Code, string? Error)> AuthorizeAsync(string clientId, string[] scopes, string challenge, CancellationToken wake)
        {
            var reply = await SendAsync(new JsonObject
            {
                ["cmd"] = "AUTHORIZE",
                ["args"] = new JsonObject
                {
                    ["client_id"] = clientId,
                    ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)s).ToArray()),
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                },
            }, wake);
            if (reply["evt"]?.GetValue<string>() == "ERROR")
            {
                var message = reply["data"]?["message"]?.ToString();
                Log.Info($"Discord : accord refusé — {message}");
                // « invalid_scope » : les droits RPC ne sont donnés qu'au propriétaire de l'appli Discord, à ses testeurs et aux membres
                // de l'équipe qui POSSÈDE l'appli (vu chez un ami de l'utilisateur, membre d'une équipe qui ne possédait pas l'appli).
                return (null, message != null && (message.Contains("tester", StringComparison.OrdinalIgnoreCase)
                                                  || message.Contains("invalid_scope", StringComparison.OrdinalIgnoreCase))
                    ? Loc.T("discord.not_tester")
                    : Loc.F("discord.denied_detail", message));
            }
            return (reply["data"]?["code"]?.GetValue<string>(), null);
        }

        public async Task<JsonNode?> CommandAsync(string command, JsonObject args, CancellationToken wake)
        {
            var reply = await SendAsync(new JsonObject { ["cmd"] = command, ["args"] = args }, wake);
            if (reply["evt"]?.GetValue<string>() == "ERROR")
                throw new InvalidOperationException($"Discord : {command} refusé ({reply["data"]?["message"]}).");
            return reply["data"];
        }

        public Task SubscribeAsync(string evt, JsonObject? args, CancellationToken wake) =>
            SendAsync(new JsonObject { ["cmd"] = "SUBSCRIBE", ["evt"] = evt, ["args"] = args ?? new JsonObject() }, wake);

        private Task UnsubscribeAsync(string evt, JsonObject args, CancellationToken wake) =>
            SendAsync(new JsonObject { ["cmd"] = "UNSUBSCRIBE", ["evt"] = evt, ["args"] = args }, wake);

        /// <summary>Nouveau salon (ou aucun) : on change d'abonnements et on publie la liste des présents.</summary>
        public async Task WatchChannelAsync(JsonNode? channel, CancellationToken wake)
        {
            if (_channelId != null)
                foreach (var evt in ChannelEvents) await UnsubscribeAsync(evt, new JsonObject { ["channel_id"] = _channelId }, wake);
            _members.Clear();
            _order.Clear();
            _channelId = channel?["id"]?.GetValue<string>();
            _channelName = channel?["name"]?.GetValue<string>();
            if (_channelId != null)
            {
                if (string.IsNullOrEmpty(_channelName)) _channelName = Loc.T("discord.call");
                foreach (var state in channel!["voice_states"]?.AsArray() ?? new JsonArray()) UpdateMember(state);
                foreach (var evt in ChannelEvents) await SubscribeAsync(evt, new JsonObject { ["channel_id"] = _channelId }, wake);
            }
            PublishMembers();
        }

        /// <summary>
        /// Boucle : une seule lecture en cours à la fois. Pendant qu'on attend Discord, une commande de la télécommande peut
        /// arriver : on l'écrit tout de suite, et sa réponse sera reconnue à son « nonce ».
        /// </summary>
        public async Task ReadEventsAsync(CancellationToken wake)
        {
            Task<(int Op, JsonNode Message)>? read = null;
            Task<bool>? queued = null;
            while (true)
            {
                read ??= ReadAsync(wake);
                queued ??= _queue.Reader.WaitToReadAsync(wake).AsTask();
                if (await Task.WhenAny(read, queued) == read)
                {
                    var (_, message) = await read;
                    read = null;
                    if (!Answer(message)) await HandleEventAsync(message, wake);
                }
                else
                {
                    queued = null;
                    while (_queue.Reader.TryRead(out var item))
                    {
                        var nonce = Guid.NewGuid().ToString();
                        item.Command["nonce"] = nonce;
                        _pending[nonce] = item.Reply;
                        await WriteAsync(OpFrame, item.Command, wake);
                    }
                }
            }
        }

        /// <summary>Réponse à une commande de la télécommande ?</summary>
        private bool Answer(JsonNode message)
        {
            if (message["nonce"]?.GetValue<string>() is not { } nonce || !_pending.Remove(nonce, out var reply)) return false;
            reply.TrySetResult(message);
            return true;
        }

        private async Task<JsonNode> SendAsync(JsonObject command, CancellationToken wake)
        {
            var nonce = Guid.NewGuid().ToString();
            command["nonce"] = nonce;
            await WriteAsync(OpFrame, command, wake);
            while (true)
            {
                var (_, message) = await ReadAsync(wake);
                if (message["nonce"]?.GetValue<string>() == nonce) return message;
                if (!Answer(message)) await HandleEventAsync(message, wake);
            }
        }

        private async Task HandleEventAsync(JsonNode message, CancellationToken wake)
        {
            if (message["cmd"]?.GetValue<string>() != "DISPATCH") return;
            var data = message["data"];
            switch (message["evt"]?.GetValue<string>())
            {
                case "VOICE_SETTINGS_UPDATE":
                    PublishVoiceSettings(data);
                    break;
                case "VOICE_CHANNEL_SELECT":
                    var id = data?["channel_id"]?.GetValue<string>();
                    if (id == _channelId) return;
                    var channel = id == null ? null : await CommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = id }, wake);
                    await WatchChannelAsync(channel, wake);
                    break;
                case "VOICE_STATE_CREATE" or "VOICE_STATE_UPDATE":
                    UpdateMember(data);
                    PublishMembers();
                    break;
                case "VOICE_STATE_DELETE":
                    if (data?["user"]?["id"]?.GetValue<string>() is { } gone && _members.Remove(gone))
                    {
                        _order.Remove(gone);
                        PublishMembers();
                    }
                    break;
                case "SPEAKING_START" or "SPEAKING_STOP":
                    if (data?["user_id"]?.GetValue<string>() is { } speaker && _members.TryGetValue(speaker, out var member))
                    {
                        member.Speaking = message["evt"]!.GetValue<string>() == "SPEAKING_START";
                        PublishMembers();
                    }
                    break;
            }
        }

        private void UpdateMember(JsonNode? state)
        {
            var user = state?["user"];
            if (user?["id"]?.GetValue<string>() is not { } id) return;
            if (!_members.TryGetValue(id, out var member))
            {
                member = new MemberState { Id = id };
                _members[id] = member;
                _order.Add(id);
            }
            member.Name = state!["nick"]?.GetValue<string>() is { Length: > 0 } nick ? nick
                : user["global_name"]?.GetValue<string>() is { Length: > 0 } global ? global
                : user["username"]?.GetValue<string>() ?? "?";
            var avatar = user["avatar"]?.GetValue<string>();
            member.AvatarUrl = avatar != null
                ? $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png?size=64"
                : $"https://cdn.discordapp.com/embed/avatars/{(ulong.TryParse(id, out var n) ? (n >> 22) % 6 : 0)}.png";
            var voice = state["voice_state"];
            member.Muted = Flag(voice, "self_mute") || Flag(voice, "mute") || Flag(state, "mute");
            member.Deafened = Flag(voice, "self_deaf") || Flag(voice, "deaf");

            static bool Flag(JsonNode? node, string name) => node?[name]?.GetValueKind() == JsonValueKind.True;
        }

        private void PublishMembers() =>
            PublishChannel(_channelId == null ? null : _channelName,
                _order.Select(id => _members[id]).Select(m => new VoiceMember(m.Id, m.Name, m.AvatarUrl, m.Muted, m.Deafened, m.Speaking)).ToList());

        private async Task WriteAsync(int op, JsonNode payload, CancellationToken wake)
        {
            var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
            var frame = new byte[8 + json.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
            json.CopyTo(frame, 8);
            await _pipe.WriteAsync(frame, wake);
            await _pipe.FlushAsync(wake);
        }

        private async Task<(int Op, JsonNode Message)> ReadAsync(CancellationToken wake)
        {
            while (true)
            {
                var header = new byte[8];
                await _pipe.ReadExactlyAsync(header, wake);
                int op = BinaryPrimitives.ReadInt32LittleEndian(header);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (length is < 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Trame Discord invalide.");
                var body = new byte[length];
                await _pipe.ReadExactlyAsync(body, wake);
                var message = JsonNode.Parse(body) ?? new JsonObject();
                if (op == OpPing)
                {
                    await WriteAsync(OpPong, message, wake);
                    continue;
                }
                if (op == OpClose) throw new ServerClosedException(message["message"]?.ToString() ?? "connexion fermée");
                return (op, message);
            }
        }

        public async ValueTask DisposeAsync() => await _pipe.DisposeAsync();

        /// <summary>Discord a fermé la connexion (trame CLOSE), avec son message.</summary>
        private sealed class ServerClosedException(string message) : IOException(message);

        private sealed class MemberState
        {
            public string Id { get; init; } = "";
            public string Name { get; set; } = "";
            public string AvatarUrl { get; set; } = "";
            public bool Muted { get; set; }
            public bool Deafened { get; set; }
            public bool Speaking { get; set; }
        }
    }

    // ------------------------------------------------------------------ jeton (chiffré pour ce compte Windows)

    /// <param name="Scope">Droits accordés, séparés par des espaces (vide pour un jeton gardé avant la 6D-1b).</param>
    private sealed record Token(string AccessToken, string RefreshToken, DateTimeOffset Expires, string Scope)
    {
        public IReadOnlyCollection<string> Scopes => Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        public static Token? Parse(string json)
        {
            var node = JsonNode.Parse(json);
            var access = node?["access_token"]?.GetValue<string>();
            var refresh = node?["refresh_token"]?.GetValue<string>() ?? "";
            if (access == null) return null;
            int seconds = node!["expires_in"]?.GetValue<int>() ?? 604800;
            return new Token(access, refresh, DateTimeOffset.UtcNow.AddSeconds(seconds), node["scope"]?.GetValue<string>() ?? "");
        }
    }

    private static class TokenStore
    {
        private static string File => Path.Combine(AppPaths.DataFolder, "discord-jeton.bin");

        public static Token? Load(string clientId)
        {
            try
            {
                if (!System.IO.File.Exists(File)) return null;
                var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), null, DataProtectionScope.CurrentUser));
                var node = JsonNode.Parse(json);
                if (node?["client_id"]?.GetValue<string>() != clientId) return null; // autre appli Discord
                return new Token(node["access"]!.GetValue<string>(), node["refresh"]!.GetValue<string>(),
                    DateTimeOffset.FromUnixTimeSeconds(node["expires"]!.GetValue<long>()), node["scope"]?.GetValue<string>() ?? "");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or NullReferenceException or InvalidOperationException)
            {
                return null;
            }
        }

        public static void Save(string clientId, Token token)
        {
            var json = new JsonObject
            {
                ["client_id"] = clientId,
                ["access"] = token.AccessToken,
                ["refresh"] = token.RefreshToken,
                ["expires"] = token.Expires.ToUnixTimeSeconds(),
                ["scope"] = token.Scope,
            }.ToJsonString();
            AppPaths.EnsureDataFolder();
            System.IO.File.WriteAllBytes(File, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
        }

        public static void Delete()
        {
            try
            {
                System.IO.File.Delete(File);
            }
            catch (IOException)
            {
            }
        }
    }
}
