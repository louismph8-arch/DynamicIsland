using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuiles de tous les jours (O6, lot « nouvelles A ») ajoutées au registre : Minuteur, Café, Date, Espace disque, Compteur,
/// À faire ; plus l'info enrichie des tuiles Presse-papiers et Notes (<see cref="Clipboard"/>, <see cref="NotesFace"/>). Toutes
/// utiles tout de suite ; les tuiles sans outil (Café, Compteur, Espace disque) gardent le clic pour elles.
/// </summary>
public static class EverydayTiles
{
    public const string Timer = "timer";
    public const string Coffee = "coffee";
    public const string Date = "date";
    public const string Disk = "disk";
    public const string Counter = "counter";
    public const string Todo = "todo";

    private static readonly TileSize[] All3 = [TileSize.S, TileSize.W, TileSize.L];

    public static readonly IReadOnlyList<TileDefinition> All =
    [
        new()
        {
            Id = Timer, NameKey = "tiles.timer.name", Glyph = Icons.Timer, Sizes = All3, DefaultSize = TileSize.W, Module = ModuleGuard.EverydayTiles,
            CreateFace = (context, size) => new TimerFace(Find(Timer), size, context),
            CreateDetail = context => context.ExistingView(IslandTabItem.Calendar),
        },
        new()
        {
            Id = Coffee, NameKey = "tiles.coffee.name", Glyph = Icons.Coffee, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.Coffee,
            CreateFace = (context, size) => new CoffeeFace(Find(Coffee), size, context),
            CreateDetail = _ => null, // pas d'outil : le clic met en marche / arrête
        },
        new()
        {
            Id = Date, NameKey = "tiles.date.name", Glyph = Icons.CalendarDays, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.EverydayTiles,
            CreateFace = (context, size) => new DateFace(Find(Date), size, context),
            CreateDetail = context => context.ExistingView(IslandTabItem.Calendar),
        },
        new()
        {
            Id = Counter, NameKey = "tiles.counter.name", Glyph = Icons.Counter, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.EverydayTiles,
            CreateFace = (context, size) => new CounterFace(Find(Counter), size, context),
            CreateDetail = _ => null, // pas d'outil : le clic compte
        },
        new()
        {
            Id = Disk, NameKey = "tiles.disk.name", Glyph = Icons.HardDrive, Sizes = All3, DefaultSize = TileSize.W, Module = ModuleGuard.EverydayTiles,
            CreateFace = (context, size) => new DiskFace(Find(Disk), size, context),
            CreateDetail = _ => null, // le clic ouvre Paramètres → Stockage
        },
        new()
        {
            Id = Todo, NameKey = "tiles.todo.name", Glyph = Icons.ListTodo, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.NotesTab,
            CreateFace = (context, size) => new TodoFace(Find(Todo), size, context),
            CreateDetail = context => context.ExistingView(IslandTabItem.Notes),
        },
    ];

    private static TileDefinition Find(string id) => All.First(d => d.Id == id);

    /// <summary>
    /// Presse-papiers : dernier élément (texte sur une ligne, ou miniature d'une image) et nombre d'éléments. Une copie sensible
    /// en attente (mot de passe proposé pour le coffre) n'est JAMAIS montrée : seulement « Copie protégée » (l'historique, lui, ne
    /// garde jamais de copie sensible). Lecture en mémoire seulement.
    /// </summary>
    internal static TileInfo Clipboard(TileContext context) => ClipboardInfo(ClipboardHistory.Store);

    internal static TileInfo ClipboardInfo(ClipboardHistoryStore store)
    {
        int count = store.Count;
        string? value = count > 0 ? count.ToString(Loc.Culture) : null;
        if (store.HasSecret) return new TileInfo(Loc.T("tiles.clipboard.secret"), value, true);
        if (store.Top() is not { } top) return new TileInfo(Loc.T("tiles.items.none"));
        if (top.Kind == ClipKind.Image)
            return new TileInfo(Loc.F("tiles.clipboard.image", top.Width, top.Height), value, false, top.Thumbnail);
        return new TileInfo(OneLine(top.Text), value);
    }

    /// <summary>Texte sur une seule ligne, coupé à 80 caractères (calcul pur).</summary>
    internal static string OneLine(string text)
    {
        var line = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 80 ? line[..80] + "…" : line;
    }
}
