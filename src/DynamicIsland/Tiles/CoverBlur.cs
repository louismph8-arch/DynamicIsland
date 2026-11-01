using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Design;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Pochette floutée de la tuile Musique (O6) : calculée UNE fois par pochette, hors du fil de l'interface
/// (<see cref="OpalPalette.Blurred"/> : réduite, saturée, floutée en boîte), puis gardée en image (partagée par les tailles S / W / L).
/// Pas de flou en direct (BlurEffect) : la tuile ne coûte rien à redessiner.
/// </summary>
internal static class CoverBlur
{
    private static BitmapSource? _source;
    private static Task<BitmapSource?>? _task;

    /// <summary>
    /// Image floutée de <paramref name="cover"/> si elle est prête ; sinon null, le calcul part (une seule fois) et
    /// <paramref name="ready"/> est appelé sur le fil de l'interface quand l'image est là.
    /// </summary>
    public static BitmapSource? Get(BitmapSource cover, Action ready)
    {
        if (!ReferenceEquals(cover, _source) || _task == null)
        {
            _source = cover;
            var frozen = cover.IsFrozen ? cover : Freeze(cover);
            _task = frozen == null ? Task.FromResult<BitmapSource?>(null) : Task.Run(() => Blur(frozen));
        }
        if (_task.IsCompletedSuccessfully) return _task.Result;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var task = _task;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(task, _task)) ready(); // une pochette plus récente a pu arriver entre-temps
        }), TaskScheduler.Default);
        return null;
    }

    /// <summary>--snapshot : attend l'image floutée de la pochette demandée en dernier.</summary>
    public static Task WhenReady() => _task ?? Task.CompletedTask;

    private static BitmapSource? Freeze(BitmapSource cover)
    {
        if (!cover.CanFreeze) return null;
        var copy = cover.Clone();
        copy.Freeze();
        return copy;
    }

    private static BitmapSource? Blur(BitmapSource cover)
    {
        try
        {
            return OpalPalette.Blurred(cover, LiveTiles.CoverBlur, LiveTiles.CoverVibrancy);
        }
        catch (Exception ex)
        {
            Log.Error("Tuile Musique : flou de la pochette", ex);
            return null;
        }
    }
}
