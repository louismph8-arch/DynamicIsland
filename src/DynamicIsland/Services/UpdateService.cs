using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Numéro de version avec pré-version (7C) : 1.24.0 &gt; 1.24.0-beta.2 &gt; 1.24.0-beta.1 &gt; 1.23.0. Une bêta s'écrit
/// « v1.24.0-beta.1 » (tag GitHub et &lt;Version&gt; du csproj).
/// </summary>
public readonly record struct ReleaseVersion(Version Core, int? Beta) : IComparable<ReleaseVersion>
{
    public bool IsBeta => Beta != null;

    public int CompareTo(ReleaseVersion other)
    {
        int core = Core.CompareTo(other.Core);
        if (core != 0) return core;
        if (Beta == other.Beta) return 0;
        if (Beta == null) return 1; // la version finale passe après ses bêtas
        if (other.Beta == null) return -1;
        return Beta.Value.CompareTo(other.Beta.Value);
    }

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => Beta is int beta ? $"{Core.ToString(3)}-beta.{beta}" : Core.ToString(3);
}

/// <summary>Dernière version publiée, plus récente que celle qui tourne.</summary>
/// <param name="AssetSha256">Empreinte SHA-256 de l'installeur publiée par GitHub (« digest » de l'asset), en hexadécimal.</param>
public sealed record UpdateInfo(ReleaseVersion Version, string Tag, string Notes, string PageUrl, string? AssetName, string? AssetUrl, long AssetSize,
                                string? AssetSha256 = null);

/// <summary>
/// Mises à jour via les « Releases » GitHub du dépôt choisi dans les paramètres : vérification 30 s après le
/// démarrage puis une fois par jour (rien n'est téléchargé sans accord). Canal Stable = dernière version normale
/// (/releases/latest) ; canal Bêta = la plus récente, pré-versions comprises (7C). « Installer » télécharge l'installeur
/// .exe de la release (adresse github.com uniquement, taille et empreinte SHA-256 vérifiées — 7B), le lance et ferme l'app.
/// </summary>
public static partial class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly DispatcherTimer Daily = new() { Interval = TimeSpan.FromHours(24) };

    public static ReleaseVersion Current { get; } = ReadCurrentVersion();
    public static string CurrentText => Current.ToString();

    public static UpdateInfo? Available { get; private set; }
    public static string Status { get; private set; } = Loc.T("updates.status.unchecked");
    public static bool Busy { get; private set; }

    /// <summary>État changé (vérification, téléchargement) : les paramètres se mettent à jour.</summary>
    public static event Action? Changed;
    /// <summary>Nouvelle version trouvée par une vérification automatique (annoncée dans l'île).</summary>
    public static event Action<UpdateInfo>? Found;

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPattern();

    public static void Start()
    {
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await AutoCheckAsync();
            Daily.Start();
        };
        Daily.Tick += async (_, _) => await AutoCheckAsync();
        first.Start();
    }

    private static async Task AutoCheckAsync()
    {
        var settings = SettingsStore.Current.Updates;
        if (!settings.CheckAutomatically || ModuleGuard.IsDisabled(ModuleGuard.Updates)) return;
        await CheckAsync();
        if (Available is { } update && update.Tag != settings.SkippedVersion) Found?.Invoke(update);
    }

    public static async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        Status = Loc.T("claude.checking");
        Changed?.Invoke();
        try
        {
            Available = null;
            var repository = SettingsStore.Current.Updates.Repository.Trim();
            if (!RepositoryPattern().IsMatch(repository))
            {
                Status = Loc.T("updates.status.bad_repository");
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            bool beta = SettingsStore.Current.Updates.Channel == UpdateChannel.Beta;
            var address = beta ? $"https://api.github.com/repos/{repository}/releases?per_page=20"
                               : $"https://api.github.com/repos/{repository}/releases/latest";
            using var response = await Http.GetAsync(address, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Status = Loc.T("updates.status.no_release");
                return;
            }
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if ((beta ? PickNewest(json.RootElement) : json.RootElement) is not { } release)
            {
                Status = Loc.T("updates.status.no_release");
                return;
            }
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseRelease(tag, out var version))
            {
                Status = Loc.F("updates.status.unreadable", tag);
                return;
            }
            if (version <= Current)
            {
                Status = Loc.F("updates.status.up_to_date", tag);
                return;
            }

            var (assetName, assetUrl, size, sha256) = PickInstaller(release);
            var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            var page = release.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
            Available = new UpdateInfo(version, tag, notes.Trim(), page, assetName, assetUrl, size, sha256);
            Status = assetUrl != null ? Loc.F("updates.status.available", tag) : Loc.F("updates.status.no_installer", tag);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Status = Loc.T("updates.status.offline");
            Log.Info($"Mises à jour : {ex.Message}");
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Télécharge l'installeur de la nouvelle version, le lance puis ferme l'app (la barre des tâches revient,
    /// l'installeur remplace l'app et la relance). Appelé seulement après un clic sur « Installer ».
    /// </summary>
    public static async Task InstallAsync()
    {
        if (Busy || Available is not { AssetUrl: { } url, AssetName: { } name } update) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
            throw new InvalidOperationException(Loc.T("updates.error.address"));
        // Sans empreinte publiée, on ne peut pas savoir si le fichier est bien celui de la release : on ne lance rien.
        if (update.AssetSha256 == null) throw new InvalidDataException(Loc.T("updates.error.no_hash"));

        Busy = true;
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "DynamicIsland-mise-a-jour");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, Path.GetFileName(name));
            string downloaded;

            using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? update.AssetSize;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long done = 0;
                int lastPercent = -1, read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent == lastPercent) continue;
                    lastPercent = percent;
                    Status = Loc.F("updates.status.downloading", update.Tag, percent);
                    Changed?.Invoke();
                }
                downloaded = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            if (update.AssetSize > 0 && new FileInfo(file).Length != update.AssetSize)
                throw new InvalidDataException(Loc.T("updates.error.incomplete"));
            if (!HashMatches(downloaded, update.AssetSha256))
            {
                File.Delete(file);
                Log.Info($"Mise à jour : empreinte différente ({downloaded} au lieu de {update.AssetSha256}), installeur effacé");
                throw new InvalidDataException(Loc.T("updates.error.hash"));
            }

            Status = Loc.T("updates.status.installing");
            Changed?.Invoke();
            Log.Info($"Mise à jour : lancement de {name} ({update.Tag})");
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status = Loc.F("updates.status.failed", ex.Message);
            Log.Error("Mise à jour", ex);
            throw;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Empreinte calculée = empreinte publiée (hexadécimal, sans tenir compte des majuscules).</summary>
    internal static bool HashMatches(string computed, string? published) =>
        published is { Length: 64 } && string.Equals(computed, published, StringComparison.OrdinalIgnoreCase);

    /// <summary>« sha256:8432b4fe… » (champ « digest » d'un asset GitHub) → l'empreinte en hexadécimal ; autre chose → null.</summary>
    internal static string? ParseDigest(string? digest)
    {
        const string Prefix = "sha256:";
        if (digest == null || !digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[Prefix.Length..].Trim();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>L'installeur .exe de la release (« Setup », 64 bits de préférence) et son empreinte SHA-256.</summary>
    private static (string? Name, string? Url, long Size, string? Sha256) PickInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return (null, null, 0, null);
        (string? Name, string? Url, long Size, string? Sha256, int Score) best = (null, null, 0, null, int.MinValue);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || url == null) continue;
            var lower = name.ToLowerInvariant();
            int score = (lower.Contains("setup") || lower.Contains("installer") ? 4 : 0)
                        + (lower.Contains("x64") || lower.Contains("win64") ? 1 : 0)
                        - (lower.Contains("arm") ? 8 : 0);
            string? digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (score > best.Score) best = (name, url, asset.GetProperty("size").GetInt64(), ParseDigest(digest), score);
        }
        return (best.Name, best.Url, best.Size, best.Sha256);
    }

    /// <summary>Canal Bêta : la release la plus récente (pré-versions comprises, brouillons exclus) parmi la liste de GitHub.</summary>
    internal static JsonElement? PickNewest(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;
        JsonElement? best = null;
        ReleaseVersion bestVersion = default;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (!release.TryGetProperty("tag_name", out var tag) || !TryParseRelease(tag.GetString() ?? "", out var version)) continue;
            if (best == null || version > bestVersion)
            {
                best = release;
                bestVersion = version;
            }
        }
        return best;
    }

    /// <summary>« v1.24.0-beta.2 », « 1.24.0-rc1 » (traité comme une bêta), « v1.24.0 », « 1.24.0+abc123 » (métadonnées ignorées).</summary>
    public static bool TryParseRelease(string tag, out ReleaseVersion release)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        int plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        string? pre = null;
        int dash = text.IndexOf('-');
        if (dash > 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
        }
        if (!TryParseVersion(text, out var core))
        {
            release = default;
            return false;
        }
        int? beta = null;
        if (pre != null)
        {
            var digits = new string(pre.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            beta = int.TryParse(digits, out var number) ? number : 0;
        }
        release = new ReleaseVersion(core, beta);
        return true;
    }

    /// <summary>« v1.2.0 », « 1.2 », « v1.2.0-beta » → 1.2.0.</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(['-', '+', ' ']);
        if (suffix > 0) text = text[..suffix];
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var parsed))
        {
            version = new Version(0, 0, 0);
            return false;
        }
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    /// <summary>« 1.24.0-beta.1+commit » (InformationalVersion, bêta comprise), sinon la version de l'assembly.</summary>
    private static ReleaseVersion ReadCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational != null && TryParseRelease(informational, out var release)) return release;
        var version = assembly.GetName().Version ?? new Version(0, 1, 0);
        return new ReleaseVersion(new Version(version.Major, version.Minor, Math.Max(0, version.Build)), null);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DynamicIsland/{ReadCurrentVersion().Core.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
