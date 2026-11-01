using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services.Friends;

/// <param name="Status">« online », « game » ou « offline ».</param>
/// <param name="Since">Depuis quand l'ami est en ligne / dans ce jeu (serveur version 3 ; null sinon).</param>
public sealed record Friend(string Code, string Name, string Status, string? Game, DateTimeOffset? Since = null)
{
    public bool Online => Status is "online" or "game";
    public string Display => Name.Length > 0 ? Name : FriendsApi.FormatCode(Code);
}

/// <param name="Blocked">Personnes bloquées.</param>
/// <param name="Extended">Le serveur connaît le blocage et les messages rapides (version 3 ou plus).</param>
public sealed record FriendLists(IReadOnlyList<Friend> Friends, IReadOnlyList<Friend> Incoming, IReadOnlyList<Friend> Outgoing,
    IReadOnlyList<Friend> Blocked, bool Extended)
{
    public static readonly FriendLists Empty = new([], [], [], [], false);
}

/// <summary>Message rapide reçu d'un ami (identifiant de la liste fermée, voir <see cref="QuickMessages"/>).</summary>
public sealed record FriendMessage(string Code, string Name, string Message, DateTimeOffset At)
{
    public string Display => Name.Length > 0 ? Name : FriendsApi.FormatCode(Code);
}

/// <summary>
/// Messages rapides (correctif 24) : liste FERMÉE, la même que celle du serveur (QUICK_MESSAGES dans worker.js). Seul
/// l'identifiant voyage ; chaque Opaline l'affiche dans sa langue. Jamais de texte libre (l'île ne prend pas le clavier).
/// </summary>
public static class QuickMessages
{
    public static readonly string[] Ids = ["play", "available", "join", "later"];

    public static bool IsKnown(string id) => Ids.Contains(id);

    public static string Text(string id) => IsKnown(id) ? Loc.T("friends.message." + id) : id;
}

/// <summary>Compte de ce PC sur le serveur des amis : code d'ami + secret (chiffrés par Windows, DPAPI).</summary>
public sealed record FriendsAccount(string Server, string Id, string Secret)
{
    private static readonly byte[] Entropy = "Opaline.FriendsAccount"u8.ToArray();
    private static string FilePath => Path.Combine(AppPaths.DataFolder, "amis-compte.bin");

    public static FriendsAccount? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = JsonNode.Parse(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
            return new FriendsAccount(json!["server"]!.GetValue<string>(), json["id"]!.GetValue<string>(), json["secret"]!.GetValue<string>());
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException or NullReferenceException or InvalidOperationException)
        {
            Log.Error("Amis : lecture du compte", ex);
            return null;
        }
    }

    public void Save()
    {
        AppPaths.EnsureDataFolder();
        var json = new JsonObject { ["server"] = Server, ["id"] = Id, ["secret"] = Secret }.ToJsonString();
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException ex)
        {
            Log.Error("Amis : suppression du compte", ex);
        }
    }
}

/// <summary>Le serveur « Amis en ligne » de l'utilisateur (server/amis/worker.js sur Cloudflare) : petites requêtes JSON.</summary>
public static class FriendsApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    /// <summary>Envoi d'un avis avec ses pièces jointes (quelques Mo) : plus de temps.</summary>
    private static readonly HttpClient Upload = new() { Timeout = TimeSpan.FromMinutes(2) };

    public sealed class AuthException() : Exception("Compte inconnu du serveur");

    /// <summary>Adresse https (ou http sur ce PC, pour les essais).</summary>
    public static bool ValidServer(string server) =>
        Uri.TryCreate(server, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.Query);

    /// <summary>« ABCDEFGH » → « ABCD-EFGH ».</summary>
    public static string FormatCode(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;

    public static async Task<bool> PingAsync(string server)
    {
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync(server + "/"));
            return json?["app"]?.GetValue<string>() == "opaline-amis";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Le serveur répond-il, et accepte-t-il les avis (secret FEEDBACK_WEBHOOK posé dans Cloudflare, 7C) ?</summary>
    public static async Task<(bool Ok, bool Feedback)> InfoAsync(string server)
    {
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync(server + "/"));
            return (json?["app"]?.GetValue<string>() == "opaline-amis", json?["feedback"]?.GetValue<bool>() == true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return (false, false);
        }
    }

    /// <summary>
    /// « Donner mon avis » (7C) : texte + capture (JPEG) et rapport de bug (.zip) facultatifs, envoyés au serveur qui les poste
    /// dans le salon Discord de son propriétaire. null = envoyé, sinon la raison (« feedback_off », « too_many », « too_big »,
    /// « auth », « offline », « discord »…).
    /// </summary>
    public static async Task<string?> SendFeedbackAsync(FriendsAccount account, bool bug, string text, string version, byte[]? screenshot, string? reportPath)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(bug ? "bug" : "idea"), "kind");
            form.Add(new StringContent(text), "text");
            form.Add(new StringContent(version), "version");
            if (screenshot != null)
            {
                var image = new ByteArrayContent(screenshot);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(image, "screenshot", "capture.jpg");
            }
            if (reportPath != null)
            {
                var report = new ByteArrayContent(await File.ReadAllBytesAsync(reportPath));
                report.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                form.Add(report, "report", "rapport.zip");
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, account.Server + "/feedback") { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{account.Id}.{account.Secret}");
            using var response = await Upload.SendAsync(request);
            if (response.IsSuccessStatusCode) return null;
            if (response.StatusCode == HttpStatusCode.Unauthorized) return "auth";
            try
            {
                return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["error"]?.GetValue<string>() ?? "server";
            }
            catch (JsonException)
            {
                return "server";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Avis : envoi impossible ({ex.GetType().Name})");
            return "offline";
        }
    }

    public static async Task<FriendsAccount> RegisterAsync(string server, string name)
    {
        var json = await SendAsync(HttpMethod.Post, server, "/register", null, new JsonObject { ["name"] = name });
        return new FriendsAccount(server, json["id"]!.GetValue<string>(), json["secret"]!.GetValue<string>());
    }

    /// <summary>Présence : « online », « game » (+ jeu), ou effacée (invisible, hors ligne).</summary>
    public static Task UpdateMeAsync(FriendsAccount account, string name, string status, string? game, bool invisible, CancellationToken token = default) =>
        SendAsync(HttpMethod.Post, account.Server, "/me", account,
            new JsonObject { ["name"] = name, ["status"] = status, ["game"] = game, ["invisible"] = invisible }, token);

    public static async Task<FriendLists> ListAsync(FriendsAccount account)
    {
        var json = await SendAsync(HttpMethod.Get, account.Server, "/friends", account, null);
        static List<Friend> Read(JsonNode? list) => (list?.AsArray() ?? []).Select(f => new Friend(
            f!["code"]!.GetValue<string>(), f["name"]?.GetValue<string>() ?? "",
            f["status"]?.GetValue<string>() ?? "offline", f["game"]?.GetValue<string>(), Time(f["since"]))).ToList();
        // « blocked » n'existe qu'à partir de la version 3 du serveur.
        return new FriendLists(Read(json["friends"]), Read(json["incoming"]), Read(json["outgoing"]), Read(json["blocked"]), json["blocked"] is JsonArray);
    }

    /// <summary>Heure envoyée par le serveur (millisecondes depuis 1970), null si absente.</summary>
    private static DateTimeOffset? Time(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var ms) && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;

    /// <summary>Bloquer (version 3) : liens coupés, plus de présence, de demande ni de message de cette personne.</summary>
    public static Task BlockAsync(FriendsAccount account, string code) =>
        SendAsync(HttpMethod.Post, account.Server, "/friends/block", account, new JsonObject { ["code"] = code });

    public static Task UnblockAsync(FriendsAccount account, string code) =>
        SendAsync(HttpMethod.Post, account.Server, "/friends/unblock", account, new JsonObject { ["code"] = code });

    /// <summary>Message rapide à un ami mutuel : null si envoyé, sinon la raison (« not_friend », « too_many », « message »…).</summary>
    public static async Task<string?> SendMessageAsync(FriendsAccount account, string code, string message)
    {
        try
        {
            await SendAsync(HttpMethod.Post, account.Server, "/messages/send", account, new JsonObject { ["code"] = code, ["message"] = message });
            return null;
        }
        catch (FriendsRefusedException ex)
        {
            return ex.Reason;
        }
    }

    /// <summary>Mes messages en attente (le serveur les efface dès qu'il les a donnés).</summary>
    public static async Task<IReadOnlyList<FriendMessage>> ReadMessagesAsync(FriendsAccount account)
    {
        var json = await SendAsync(HttpMethod.Get, account.Server, "/messages", account, null);
        return (json["messages"]?.AsArray() ?? []).Select(m => new FriendMessage(
                m!["code"]!.GetValue<string>(), m["name"]?.GetValue<string>() ?? "", m["message"]?.GetValue<string>() ?? "",
                Time(m["at"]) ?? DateTimeOffset.UtcNow))
            .Where(m => QuickMessages.IsKnown(m.Message)).ToList();
    }

    /// <summary>Ajoute (ou accepte) : null si c'est fait, sinon la raison (« unknown », « code », « full »).</summary>
    public static async Task<string?> AddAsync(FriendsAccount account, string code)
    {
        try
        {
            await SendAsync(HttpMethod.Post, account.Server, "/friends/add", account, new JsonObject { ["code"] = code });
            return null;
        }
        catch (FriendsRefusedException ex)
        {
            return ex.Reason;
        }
    }

    public static Task RemoveAsync(FriendsAccount account, string code) =>
        SendAsync(HttpMethod.Post, account.Server, "/friends/remove", account, new JsonObject { ["code"] = code });

    /// <summary>Efface ce compte du serveur (pseudo, présence, liens d'amis).</summary>
    public static Task ForgetAsync(FriendsAccount account) => SendAsync(HttpMethod.Post, account.Server, "/forget", account, new JsonObject());

    public sealed class FriendsRefusedException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }

    private static async Task<JsonNode> SendAsync(HttpMethod method, string server, string path, FriendsAccount? account, JsonObject? body,
        CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(method, server + path);
        if (account != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{account.Id}.{account.Secret}");
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new AuthException();
        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // page d'erreur de Cloudflare, mauvaise adresse…
        }
        if (!response.IsSuccessStatusCode)
        {
            var reason = json?["error"]?.GetValue<string>();
            if (reason != null && (int)response.StatusCode < 500) throw new FriendsRefusedException(reason);
            throw new HttpRequestException($"Serveur des amis : {(int)response.StatusCode} {reason}");
        }
        return json ?? throw new HttpRequestException("Serveur des amis : réponse illisible");
    }
}
