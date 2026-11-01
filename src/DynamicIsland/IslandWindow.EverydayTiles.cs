using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Tiles;
using DynamicIsland.Tiles.Everyday;

namespace DynamicIsland;

/// <summary>
/// --snapshot (mode grille) : les tuiles de tous les jours (O6, lot A) dans toutes leurs tailles, avec des données d'exemple
/// (minuteur qui tourne, Café actif, compteur, disques, notes et liste « À faire » inventées). Rien de réel n'est montré (ni les
/// notes ni le presse-papiers de l'utilisateur) et rien n'est enregistré ; la grille normale est remise à la fin.
/// </summary>
public partial class IslandWindow
{
    private async Task SnapshotEverydayTilesAsync(string folder)
    {
        if (!GridLayout) return;
        var data = SettingsStore.Current.TileData;
        long counter = data.Counter;
        string todoId = data.TodoNoteId;
        var notes = new NotesStore();
        notes.Add(Loc.T("snapshot.tiles.note_title"), Loc.T("snapshot.tiles.note_text"));
        var todo = notes.Add(Loc.T("tiles.todo.note_title"), Loc.T("snapshot.tiles.todo_text"));
        var disks = new[] { new DiskSpace("C:", 412L << 30, 1000L << 30), new DiskSpace("D:", 38L << 30, 1000L << 30), new DiskSpace("E:", 1300L << 30, 2000L << 30) };
        try
        {
            data.Counter = 12;
            data.TodoNoteId = todo?.Id ?? "";
            KeepAwake.Start(TimeSpan.FromHours(1)); // simulé en mode développeur : rien n'est demandé à Windows
            _timer.StartWith(TimeSpan.FromMinutes(25));
            _timer.Add(TimeSpan.FromMinutes(-7)); // 18 min restantes sur 25 : l'anneau a avancé

            var pages = new (string File, TileSlot[] Slots)[]
            {
                ("4h-tous-les-jours-1.png", [new(EverydayTiles.Timer, TileSize.W, 0, 0, 0), new(EverydayTiles.Coffee, TileSize.W, 0, 2, 0),
                    new(EverydayTiles.Date, TileSize.S, 0, 0, 1), new(EverydayTiles.Counter, TileSize.S, 0, 1, 1), new(EverydayTiles.Disk, TileSize.W, 0, 2, 1)]),
                ("4h-tous-les-jours-2.png", [new(EverydayTiles.Todo, TileSize.L, 0, 0, 0), new(IslandTabItem.Notes, TileSize.W, 0, 2, 0),
                    new(IslandTabItem.Clipboard, TileSize.S, 0, 2, 1), new(EverydayTiles.Coffee, TileSize.S, 0, 3, 1)]),
                ("4h-tous-les-jours-3.png", [new(EverydayTiles.Timer, TileSize.L, 0, 0, 0), new(EverydayTiles.Date, TileSize.L, 0, 2, 0)]),
                ("4h-tous-les-jours-4.png", [new(IslandTabItem.Notes, TileSize.L, 0, 0, 0), new(EverydayTiles.Counter, TileSize.W, 0, 2, 0), new(EverydayTiles.Todo, TileSize.W, 0, 2, 1)]),
                ("4h-tous-les-jours-5.png", [new(EverydayTiles.Disk, TileSize.L, 0, 0, 0), new(EverydayTiles.Coffee, TileSize.L, 0, 2, 0)]),
                ("4h-tous-les-jours-6.png", [new(EverydayTiles.Timer, TileSize.S, 0, 0, 0), new(EverydayTiles.Disk, TileSize.S, 0, 1, 0), new(IslandTabItem.Notes, TileSize.S, 0, 2, 0),
                    new(EverydayTiles.Todo, TileSize.S, 0, 3, 0), new(EverydayTiles.Date, TileSize.W, 0, 0, 1), new(EverydayTiles.Counter, TileSize.W, 0, 2, 1)]),
            };
            var context = TileContext;
            foreach (var (file, slots) in pages)
            {
                Collapse();
                FinishAnimations();
                GoHome(animate: false);
                Expand();
                TileGrid.Show(slots, (definition, size) => definition.Create(context, size));
                TileGrid.ShowPage(0, animate: false);
                foreach (var face in TileGrid.Faces)
                {
                    if (face is NotesFace notesFace) notesFace.Store = notes;
                    if (face is TodoFace todoFace) todoFace.Store = notes;
                    if (face is DiskFace diskFace) diskFace.Sample = disks;
                }
                TileGrid.RefreshAll();
                // Presse-papiers d'exemple (jamais celui de l'utilisateur).
                if (TileGrid.FaceOf(IslandTabItem.Clipboard) is { } clip)
                    clip.Show(new TileInfo(Loc.T("snapshot.tiles.clip_text"), "8"));
                FinishAnimations();
                TileGrid.IsActive = false; // plus de relecture pendant l'enregistrement de l'image (rien de réel ne revient)
                _opal.HideSweep();
                await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, file));
            }
        }
        finally
        {
            KeepAwake.Stop();
            _timer.Reset();
            data.Counter = counter;
            data.TodoNoteId = todoId;
            RefreshGrid(); // la grille normale revient
            GoHome(animate: false);
            FinishAnimations();
        }
    }
}
