using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Photos et fichiers envoyés par le téléphone (6D-1b), façon AirDrop : reçus par morceaux de 4 Mo dans un fichier caché
/// « .opaline-xxxx.part » du dossier Téléchargements, puis renommés (sans écraser) quand tout est là — l'île l'annonce
/// alors comme un téléchargement terminé. Appelé hors du thread de l'interface ; le nom vient du téléphone et est nettoyé.
/// </summary>
public sealed partial class RemoteUploads
{
    public const long MaxFileBytes = 4L * 1024 * 1024 * 1024;
    private const long KeepFree = 200L * 1024 * 1024;
    private const string Prefix = ".opaline-";

    private readonly string _folder;

    public RemoteUploads(string folder) => _folder = folder;

    [GeneratedRegex("^[a-z0-9]{8,32}$")]
    private static partial Regex UploadId();

    /// <summary>Un morceau : <c>?id=…&amp;name=…&amp;offset=…&amp;total=…</c>, le corps = les octets.</summary>
    public RemoteResponse Receive(RemoteRequest request)
    {
        var id = request.Param("id") ?? "";
        if (!UploadId().IsMatch(id) || !long.TryParse(request.Param("offset"), out long offset) || !long.TryParse(request.Param("total"), out long total)
            || offset < 0 || total <= 0)
            return RemoteResponse.Json(400, """{"error":"upload"}""");
        if (total > MaxFileBytes) return RemoteResponse.Json(413, """{"error":"size"}""");

        var partial = Path.Combine(_folder, Prefix + id + ".part");
        long current = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset != current) return RemoteResponse.Json(409, new JsonObject { ["offset"] = current }.ToJsonString()); // le téléphone reprend là
        if (offset + request.Body.Length > total) return RemoteResponse.Json(400, """{"error":"upload"}""");
        if (offset == 0)
        {
            Directory.CreateDirectory(_folder);
            if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_folder))!).AvailableFreeSpace < total + KeepFree)
                return RemoteResponse.Json(507, """{"error":"space"}""");
        }

        using (var file = new FileStream(partial, FileMode.Append, FileAccess.Write, FileShare.None))
            file.Write(request.Body);
        if (offset == 0) File.SetAttributes(partial, File.GetAttributes(partial) | FileAttributes.Hidden);
        long received = offset + request.Body.Length;
        if (received < total) return RemoteResponse.Json(200, new JsonObject { ["offset"] = received }.ToJsonString());

        var final = UniquePath(SafeName(request.Param("name")));
        File.Move(partial, final);
        File.SetAttributes(final, File.GetAttributes(final) & ~FileAttributes.Hidden);
        Log.Info($"Télécommande : fichier reçu ({total / 1024} Ko)");
        return RemoteResponse.Json(200, new JsonObject { ["done"] = true, ["name"] = Path.GetFileName(final) }.ToJsonString());
    }

    /// <summary>Morceaux abandonnés depuis plus d'un jour (téléphone parti en plein envoi) : effacés.</summary>
    public void CleanStale()
    {
        try
        {
            if (!Directory.Exists(_folder)) return;
            foreach (var file in Directory.EnumerateFiles(_folder, Prefix + "*.part"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Télécommande : nettoyage des envois", ex);
        }
    }

    /// <summary>Nom de fichier sûr : pas de dossier, pas de caractère interdit, pas de nom réservé, 120 caractères au plus.</summary>
    internal static string SafeName(string? name)
    {
        name = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last());
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        name = name.Trim().TrimEnd('.', ' ').TrimStart('.');
        if (name.Length > 120)
        {
            var extension = Path.GetExtension(name);
            name = name[..(120 - Math.Min(extension.Length, 20))] + extension[..Math.Min(extension.Length, 20)];
        }
        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (name.Length == 0 || stem is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(stem, "^(COM|LPT)[0-9]$")) name = "fichier" + Path.GetExtension(name);
        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) name += ".fichier";
        return name;
    }

    private string UniquePath(string name)
    {
        var path = Path.Combine(_folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (int i = 2; File.Exists(path) || Directory.Exists(path); i++) path = Path.Combine(_folder, $"{stem} ({i}){extension}");
        return path;
    }
}
