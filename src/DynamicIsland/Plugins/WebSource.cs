using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DynamicIsland.Settings;

namespace DynamicIsland.Plugins;

/// <summary>
/// Source web d'un widget des potes : un simple GET https de l'adresse déclarée dans le fichier, et seulement si
/// l'utilisateur a autorisé CE site (réglage <see cref="PluginState.AllowedHost"/>, révocable). Aucun cookie, aucun
/// identifiant, aucun en-tête de l'utilisateur, pas de redirection suivie (on ne part jamais vers un autre site), réponse
/// JSON de 64 Ko au plus, toutes les 5 minutes au plus.
/// </summary>
public static class WebSource
{
    public const int MaxResponseBytes = 64 * 1024;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static int _requests;

    /// <summary>Nombre de requêtes parties depuis le lancement (l'auto-test vérifie qu'il n'y en a pas sans accord).</summary>
    public static int Requests => _requests;

    /// <summary>Auto-test : remplace le réseau par une réponse fabriquée (rien ne sort du PC).</summary>
    internal static Func<Uri, CancellationToken, Task<string>>? TestFetcher { get; set; }

    /// <summary>Vrai si ce widget a une source web ET que l'utilisateur a autorisé exactement son site.</summary>
    public static bool IsAllowed(WidgetDefinition definition, PluginState? state) =>
        definition.Source is { } source && state?.AllowedHost is { } allowed && string.Equals(allowed, source.Host, StringComparison.Ordinal);

    /// <summary>Lit la source (si elle est autorisée) et renvoie ses champs ; null si elle n'est pas autorisée.</summary>
    public static async Task<IReadOnlyDictionary<string, string>?> FetchAsync(WidgetDefinition definition, PluginState? state, CancellationToken cancel)
    {
        if (!IsAllowed(definition, state) || definition.Source is not { } source) return null;
        Interlocked.Increment(ref _requests);
        var json = TestFetcher != null ? await TestFetcher(source.Url, cancel) : await GetAsync(source.Url, cancel);
        return Extract(source, json);
    }

    private static async Task<string> GetAsync(Uri url, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Opaline-Widgets/1.0");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("réponse trop grosse");
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var copy = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
        {
            copy.Write(buffer, 0, read);
            if (copy.Length > MaxResponseBytes) throw new InvalidDataException("réponse trop grosse");
        }
        return Encoding.UTF8.GetString(copy.GetBuffer(), 0, (int)copy.Length);
    }

    /// <summary>Champs déclarés lus dans la réponse (champ absent = pas de valeur). Calcul pur, vérifié par --self-test.</summary>
    public static Dictionary<string, string> Extract(WebSourceDefinition source, string json)
    {
        if (json.Length > MaxResponseBytes) throw new InvalidDataException("réponse trop grosse");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, path) in source.Fields)
            if (JsonPath.Extract(document.RootElement, path) is { } value) values[name] = value;
        return values;
    }
}
