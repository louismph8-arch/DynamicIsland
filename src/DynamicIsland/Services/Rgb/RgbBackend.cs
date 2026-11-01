using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services.Rgb;

/// <summary>État d'une marque d'éclairage (Razer, Corsair, SteelSeries) affiché dans Paramètres → Connecté.</summary>
public enum RgbBrandState
{
    /// <summary>Pas encore essayé (ou éclairage coupé).</summary>
    Off,
    Connected,
    /// <summary>Le logiciel de la marque ne tourne pas (ou n'est pas installé) : on n'essaie même pas.</summary>
    NotRunning,
    /// <summary>Le logiciel tourne mais refuse ou ne répond pas (SDK désactivé, accès refusé…).</summary>
    Refused,
    /// <summary>Le kit de la marque est introuvable (Corsair : iCUESDK.x64_2019.dll).</summary>
    Missing,
}

/// <summary>
/// Une marque d'éclairage pilotée par <see cref="RgbService"/>, toujours depuis le même fil (un appel à la fois).
/// Chaque marque est isolée : une erreur chez l'une ne touche pas les autres (le service attrape tout, marque par marque).
/// </summary>
internal interface IRgbBackend
{
    /// <summary>Nom pour le journal (jamais traduit).</summary>
    string Name { get; }
    RgbBrandState State { get; }
    bool IsConnected { get; }
    /// <summary>Essaie de se connecter, seulement si le logiciel de la marque tourne ; met <see cref="State"/> à jour.</summary>
    void Start();
    /// <summary>Même couleur partout ; faux si la marque ne répond plus (elle est alors arrêtée).</summary>
    bool SetColor(byte red, byte green, byte blue);
    /// <summary>Garde la session en vie (Razer, SteelSeries : sinon elle meurt après 15 s) ; faux si elle est perdue.</summary>
    bool Heartbeat();
    /// <summary>Rend la main au logiciel de la marque (ses effets reviennent).</summary>
    void Stop();
}

/// <summary>Petits outils communs aux marques qui parlent HTTP en local (Razer, SteelSeries).</summary>
internal static class RgbHttp
{
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(1) })
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    /// <summary>Requête JSON synchrone (on est sur le fil des appareils, jamais sur celui de l'interface).</summary>
    public static (bool Ok, JsonNode? Body) Send(HttpClient client, HttpMethod method, string url, JsonNode? body)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = client.Send(request);
        using var reader = new System.IO.StreamReader(response.Content.ReadAsStream());
        var text = reader.ReadToEnd();
        JsonNode? json = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(text)) json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
        }
        return (response.IsSuccessStatusCode, json);
    }

    /// <summary>Vrai si l'un de ces processus tourne (nom sans .exe).</summary>
    public static bool AnyProcess(params string[] names)
    {
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            if (processes.Length > 0) return true;
        }
        return false;
    }
}
