using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Toutes les tuiles de la grille de l'île (refonte O6, étape 1 : une tuile par onglet existant, vue simple). Ordre = celui de la
/// bibliothèque (O7). Les tuiles « vivantes » (pochette floutée, ciel animé, horloge analogique…) et les nouvelles tuiles
/// (Café, Capture, Couleur, Calculatrice…) s'ajoutent ici dans les étapes suivantes ; voir <see cref="TileDefinition"/>.
/// <para>Les lectures (<c>Read</c>) ne touchent qu'à ce qui est déjà en mémoire : pas de disque, pas de réseau, pas de mesure.</para>
/// </summary>
public static class TileRegistry
{
    private static readonly TileSize[] All3 = [TileSize.S, TileSize.W, TileSize.L];

    public static readonly IReadOnlyList<TileDefinition> All =
    [
        // Tuiles vivantes (lot « vivantes A ») : pochette floutée + visualiseur, ciel animé (la tuile Widgets montre la météo).
        Tab(IslandTabItem.Music, "island.musique", Icons.Music, ModuleGuard.MusicTab, TileSize.L, Music,
            face: (context, size, definition) => new MusicTileFace(definition, size, context)),
        Tab(IslandTabItem.Control, "island.controles", Icons.Controls, ModuleGuard.ControlTab, TileSize.W, Control),
        Tab(IslandTabItem.Calendar, "island.calendrier", Icons.Calendar, ModuleGuard.CalendarTab, TileSize.S, Calendar),
        Tab(IslandTabItem.Widgets, "island.widgets", Icons.Widgets, ModuleGuard.WidgetsTab, TileSize.S, Widgets,
            face: (context, size, definition) => new WeatherTileFace(definition, size, context)),
        Tab(IslandTabItem.Mixer, "island.mixer", Icons.Mixer, ModuleGuard.MixerTab, TileSize.S, _ => new TileInfo(Loc.T("tiles.mixer.line"))),
        Tab(IslandTabItem.Shelf, "island.etagere", Icons.Shelf, ModuleGuard.ShelfTab, TileSize.S, Shelf),
        Tab(IslandTabItem.Voice, "island.vocal", Icons.Voice, ModuleGuard.Discord, TileSize.S, Voice),
        Tab(IslandTabItem.Claude, "", Icons.Sparkles, ModuleGuard.Claude, TileSize.S, Assistant, () => ClaudeService.AssistantName),
        Tab(IslandTabItem.Clocks, "island.clocks", Icons.Clock, ModuleGuard.ClocksTab, TileSize.S, Clocks),
        Tab(IslandTabItem.Health, "island.health", Icons.Health, ModuleGuard.Health, TileSize.S, _ => new TileInfo(Loc.T("tiles.health.line"))),
        Tab(IslandTabItem.Friends, "island.friends", Icons.Friends, ModuleGuard.Friends, TileSize.S, Friends,
            face: (_, size, definition) => LiveTilesB.FriendsFace(definition, size)),
        Tab(IslandTabItem.Clipboard, "island.clipboard", Icons.Clipboard, ModuleGuard.ClipboardTab, TileSize.S,
            Everyday.EverydayTiles.Clipboard),
        Tab(IslandTabItem.Notes, "island.notes", Icons.Notes, ModuleGuard.NotesTab, TileSize.S,
            _ => new TileInfo(Count(NotesService.Store.Count, "tiles.notes")), face: (context, size, definition) => new Everyday.NotesFace(definition, size, context)),
        Tab(IslandTabItem.Translate, "island.translate", Icons.Translate, ModuleGuard.TranslateTab, TileSize.S, Translate),
        AnalogClockTile(),
        // Tuiles vivantes du lot B (pas des onglets) : Moniteur (→ outil Santé), Profil (changement en un clic).
        LiveTilesB.Monitor(),
        LiveTilesB.Profile(),
        // Tuiles de tous les jours (O6, lot A) : Minuteur, Café, Date, Compteur, Espace disque, À faire.
        .. Everyday.EverydayTiles.All,
        // Tuiles-outils (lot B) : fond d'écran, micro, capture, couleur, calculatrice, paramètres, QR code, mémo vocal, téléphone.
        ..Tools.ToolTiles.All,
    ];

    /// <summary>Id de la tuile Horloge analogique (pas un onglet : elle ouvre l'outil Horloges s'il existe).</summary>
    public const string AnalogClock = "clock";

    public static TileDefinition? Find(string? id) => id == null ? null : All.FirstOrDefault(d => d.Id == id);

    /// <summary>Tuile dont la vue détaillée est l'onglet existant <paramref name="id"/> de l'île.</summary>
    /// <param name="face">Vue vivante (facultatif) à la place de la vue simple.</param>
    private static TileDefinition Tab(string id, string nameKey, string glyph, string module, TileSize size, Func<TileContext, TileInfo> read,
        Func<string>? dynamicName = null, Func<TileContext, TileSize, TileDefinition, TileFace>? face = null)
    {
        TileDefinition? definition = null;
        definition = new TileDefinition
        {
            Id = id,
            NameKey = nameKey,
            DynamicName = dynamicName,
            Glyph = glyph,
            Sizes = All3,
            DefaultSize = size,
            Module = module,
            IsAvailable = context => context.ToolAvailable(id),
            Read = read,
            CreateFace = face == null ? null : (context, tileSize) => face(context, tileSize, definition!),
            CreateDetail = context => context.ExistingView(id),
        };
        return definition;
    }

    /// <summary>
    /// Horloge analogique (O6) : L par défaut (S possible : en W le cadran rond laisserait deux bandes vides). Toujours affichée
    /// (rien à attendre) ; un clic ouvre l'outil Horloges (horloges du monde) s'il a des villes, sinon rien.
    /// </summary>
    private static TileDefinition AnalogClockTile()
    {
        TileDefinition? definition = null;
        definition = new TileDefinition
        {
            Id = AnalogClock,
            NameKey = "tiles.clock.name",
            Glyph = Icons.AnalogClock,
            Sizes = [TileSize.S, TileSize.L],
            DefaultSize = TileSize.L,
            Module = ModuleGuard.ClocksTab,
            CreateFace = (_, size) => new AnalogClockFace(definition!, size),
            CreateDetail = context => context.ToolAvailable(IslandTabItem.Clocks) ? context.ExistingView(IslandTabItem.Clocks) : null,
        };
        return definition;
    }

    // ------------------------------------------------------------------ infos en direct (lectures en mémoire seulement)

    /// <summary>« Vide » / « 1 élément » / « 12 éléments » (clés <c>{prefix}.none / .one / .many</c>).</summary>
    internal static string Count(int count, string prefix) =>
        count <= 0 ? Loc.T(prefix + ".none") : count == 1 ? Loc.T(prefix + ".one") : Loc.F(prefix + ".many", count);

    private static TileInfo Music(TileContext context)
    {
        var media = context.Media;
        if (!media.HasSession || string.IsNullOrWhiteSpace(media.Title)) return new TileInfo(Loc.T("tiles.music.idle"));
        string line = string.IsNullOrWhiteSpace(media.Artist) ? media.Title : $"{media.Title} · {media.Artist}";
        return new TileInfo(line, null, media.IsPlaying, media.Cover);
    }

    private static TileInfo Calendar(TileContext context)
    {
        var timer = context.Timer;
        if (timer.State is TimerState.Running or TimerState.Paused or TimerState.Finished)
        {
            string line = Loc.T(timer.State == TimerState.Paused ? "tiles.calendar.paused" : "tiles.calendar.timer");
            return new TileInfo(line, TimerService.Format(timer.Remaining), timer.State == TimerState.Running);
        }
        var today = DateTime.Now;
        return new TileInfo(today.ToString("dddd", Loc.Culture), today.Day.ToString(Loc.Culture));
    }

    private static TileInfo Control(TileContext context) =>
        FocusService.DoNotDisturb ? new TileInfo(Loc.T("tiles.control.dnd"), null, true) : new TileInfo(Loc.T("tiles.control.line"));

    private static TileInfo Widgets(TileContext context)
    {
        if (WeatherService.Shared.Current is not { } weather) return new TileInfo(Loc.T("tiles.widgets.line"));
        return new TileInfo(weather.City, Math.Round(weather.Temperature).ToString("0", Loc.Culture) + "°");
    }

    private static TileInfo Shelf(TileContext context)
    {
        int count = ShelfService.Items.Count;
        return new TileInfo(count == 0 ? Loc.T("tiles.shelf.empty") : Count(count, "tiles.items"));
    }

    private static TileInfo Voice(TileContext context)
    {
        if (!DiscordVoice.InVoice) return new TileInfo(Loc.T("tiles.voice.off"));
        int count = DiscordVoice.Members.Count;
        return new TileInfo(DiscordVoice.Channel, count > 0 ? count.ToString(Loc.Culture) : null, DiscordVoice.Members.Any(m => m.Speaking));
    }

    private static TileInfo Assistant(TileContext context) =>
        ClaudeService.IsBusy ? new TileInfo(Loc.T("tiles.claude.busy"), null, true) : new TileInfo(Loc.T("tiles.claude.idle"));

    private static TileInfo Clocks(TileContext context)
    {
        var clocks = SettingsStore.Current.Island.WorldClocks;
        if (clocks.Count == 0 || WorldClockService.Now(clocks[0]) is not { } now) return new TileInfo(null);
        string city = clocks[0].Name.Split(',')[0].Trim();
        if (clocks.Count > 1) city += " " + Loc.F("tiles.clocks.more", clocks.Count - 1);
        return new TileInfo(city, now.Time.ToString("t", Loc.Culture));
    }

    private static TileInfo Friends(TileContext context)
    {
        int online = Services.Friends.FriendsService.Lists.Friends.Count(f => f.Online);
        return new TileInfo(Loc.T("tiles.friends.online"), online.ToString(Loc.Culture), online > 0);
    }

    private static TileInfo Translate(TileContext context)
    {
        if (TranslateService.IsBusy) return new TileInfo(Loc.T("tiles.translate.busy"), null, true);
        var target = TranslateService.Find(TranslateService.TargetCode);
        return new TileInfo(target == null ? null : Loc.F("tiles.translate.to", target.Name));
    }
}
