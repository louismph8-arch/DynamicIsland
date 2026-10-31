using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DynamicIsland.Services.Remote;

/// <summary>Un fichier proposé au téléphone : clé à usage du lien de téléchargement, gardé 2 h.</summary>
public sealed record OutboxItem(string Id, string Path, string Name, long Size, string Key, DateTime Added)
{
    /// <summary>Lien direct (sans jeton : la clé de 128 bits suffit, propre à ce fichier) : le téléphone le télécharge comme une page.</summary>
    public string Url => $"{RemoteOutbox.DownloadPrefix}{Id}?k={Key}";
}

/// <summary>
/// « Envoyer au téléphone » (7E, AirDrop maison) : fichiers glissés de l'étagère sur la zone du téléphone. Ils restent sur le PC
/// (rien n'est copié) ; la page de la télécommande les liste (/api/outbox, téléphone appairé) et les télécharge par un lien qui
/// porte une clé aléatoire propre à chaque fichier (/dl/…?k=…). Retirés au bout de 2 h, à la demande, ou si le fichier disparaît.
/// Seulement en mémoire : rien n'est gardé au redémarrage. Accès depuis plusieurs threads (serveur, interface) : verrou.
/// </summary>
public static class RemoteOutbox
{
    public const string DownloadPrefix = "/dl/";
    public const int MaxItems = 20;
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(2);
    private static readonly object Gate = new();
    private static readonly List<OutboxItem> Items = [];

    /// <summary>Liste changée (sur n'importe quel thread).</summary>
    public static event Action? Changed;

    /// <summary>Change à chaque ajout / retrait : la page du téléphone relit la liste quand ce nombre bouge.</summary>
    public static int Revision { get; private set; }

    /// <summary>La zone « Envoyer au téléphone » a un sens : télécommande et transfert de fichiers activés, un téléphone relié.</summary>
    public static bool Available
    {
        get
        {
            var remote = Settings.SettingsStore.Current.Remote;
            return remote.Enabled && remote.FileTransfer && remote.Devices.Count > 0;
        }
    }

    public static IReadOnlyList<OutboxItem> Current
    {
        get
        {
            lock (Gate)
            {
                Prune();
                return Items.ToList();
            }
        }
    }

    /// <summary>Ajoute les fichiers (pas les dossiers) ; renvoie combien sont maintenant proposés au téléphone.</summary>
    public static int Add(IEnumerable<string> paths)
    {
        int added = 0;
        lock (Gate)
        {
            Prune();
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                Items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)); // de nouveau en tête, nouvelle durée
                if (Items.Count >= MaxItems) Items.RemoveAt(0);
                Items.Add(new OutboxItem(Token(6), path, Path.GetFileName(path), new FileInfo(path).Length, Token(16), DateTime.UtcNow));
                added++;
            }
            if (added > 0) Revision++;
        }
        if (added > 0)
        {
            Log.Info($"Télécommande : {added} fichier(s) proposé(s) au téléphone");
            Changed?.Invoke();
        }
        return added;
    }

    public static void Remove(string id)
    {
        bool removed;
        lock (Gate)
        {
            removed = Items.RemoveAll(i => i.Id == id) > 0;
            if (removed) Revision++;
        }
        if (removed) Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            if (Items.Count == 0) return;
            Items.Clear();
            Revision++;
        }
        Changed?.Invoke();
    }

    /// <summary>Le fichier de ce lien, si la clé est la bonne (comparaison à temps constant) et qu'il est encore là.</summary>
    public static OutboxItem? Find(string id, string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (Gate)
        {
            Prune();
            var item = Items.FirstOrDefault(i => i.Id == id);
            return item != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(item.Key), Encoding.ASCII.GetBytes(key)) ? item : null;
        }
    }

    private static void Prune()
    {
        int removed = Items.RemoveAll(i => DateTime.UtcNow - i.Added > KeepFor || !File.Exists(i.Path));
        if (removed > 0) Revision++;
    }

    private static string Token(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
