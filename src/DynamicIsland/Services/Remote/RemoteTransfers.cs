namespace DynamicIsland.Services.Remote;

/// <summary>
/// Avancement des transferts téléphone ↔ PC (7E), pour l'activité en direct de l'île (O5) : réception par morceaux
/// (<see cref="RemoteUploads"/>) et téléchargement d'un fichier de l'étagère par le téléphone (<see cref="RemoteServer"/>).
/// Appelé hors du fil de l'interface ; <see cref="Changed"/> au plus toutes les <see cref="Throttle"/> par transfert (plus le
/// début et la fin). Un transfert sans nouvelles depuis <see cref="Stale"/> est oublié (téléphone parti en plein envoi).
/// </summary>
public static class RemoteTransfers
{
    public sealed record Transfer(string Id, bool Incoming, long Done, long Total, DateTime Updated);

    private static readonly Dictionary<string, Transfer> Items = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);

    /// <summary>Moins que ça : trop court pour une activité (elle clignoterait).</summary>
    public const long MinBytes = 1024 * 1024;

    public static event Action? Changed;

    public static void Report(string id, bool incoming, long done, long total)
    {
        if (total < MinBytes) return;
        bool raise;
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            raise = !Items.TryGetValue(id, out var old) || now - old.Updated >= Throttle;
            Items[id] = new Transfer(id, incoming, Math.Clamp(done, 0, total), total, raise ? now : old!.Updated);
        }
        if (raise) Changed?.Invoke();
    }

    public static void Finish(string id)
    {
        bool removed;
        lock (Gate) removed = Items.Remove(id);
        if (removed) Changed?.Invoke();
    }

    /// <summary>Transferts encore vivants (les oubliés sont retirés au passage).</summary>
    public static List<Transfer> Snapshot()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            foreach (var stale in Items.Values.Where(t => now - t.Updated > Stale).Select(t => t.Id).ToList()) Items.Remove(stale);
            return [.. Items.Values];
        }
    }

    /// <summary>Avancement global (octets reçus / envoyés sur le total de tous les transferts) et sens du plus gros.</summary>
    public static (double Progress, bool Incoming)? Overall(IReadOnlyCollection<Transfer> transfers)
    {
        if (transfers.Count == 0) return null;
        long total = transfers.Sum(t => t.Total), done = transfers.Sum(t => t.Done);
        var biggest = transfers.MaxBy(t => t.Total)!;
        return (total > 0 ? Math.Clamp((double)done / total, 0, 1) : 0, biggest.Incoming);
    }

    internal static void Clear()
    {
        lock (Gate) Items.Clear();
    }
}
