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
public sealed record Friend(string Code, string Name, string Status, string? Game)
{
    public bool Online => Status is "online" or "game";
    public string Display => Name.Length > 0 ? Name : FriendsApi.FormatCode(Code);
}

public sealed record FriendLists(IReadOnlyList<Friend> Friends, IReadOnlyList<Friend> Incoming, IReadOnlyList<Friend> Outgoing)
{
    public static readonly FriendLists Empty = new([], [], []);
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
            f["status"]?.GetValue<string>() ?? "offline", f["game"]?.GetValue<string>())).ToList();
        return new FriendLists(Read(json["friends"]), Read(json["incoming"]), Read(json["outgoing"]));
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
