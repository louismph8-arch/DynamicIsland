using System.IO;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Tiles;

namespace DynamicIsland;

/// <summary>
/// --snapshot : tuiles vivantes (O6, lot « vivantes A »), images figées (<see cref="LiveTicker.Frozen"/>) avec des données
/// d'exemple (morceau inventé de --demo, météos d'exemple, heure 10:09:32) — rien n'est demandé au réseau pour ces images.
/// Chaque image montre une page composée à la main : une L, une W et deux S.
/// </summary>
public partial class IslandWindow
{
    private async Task SnapshotLiveTilesAsync(string folder)
    {
        if (!GridLayout) return;
        Collapse();
        FinishAnimations();
        GoHome(animate: false);
        TileGrid.ShowPage(0, animate: false);
        Expand();
        LiveTicker.Frozen = true;
        try
        {
            var morning = new DateTime(2026, 11, 1, 10, 9, 32);
            WeatherReport Weather(int code, bool day, double temp) => new("Montréal", temp, temp - 4, temp + 3, code, day, morning);

            // Musique : L, W, S avec le morceau d'exemple, à côté d'une météo ensoleillée.
            await PoseAsync(folder, "4f-musique", (IslandTabItem.Music, TileSize.L), (IslandTabItem.Music, TileSize.W),
                (IslandTabItem.Music, TileSize.S), (IslandTabItem.Widgets, TileSize.S), face => Sample(face, Weather(0, true, 14), morning, empty: false));
            // Musique sans rien en lecture.
            await PoseAsync(folder, "4f-musique-vide", (IslandTabItem.Music, TileSize.L), (IslandTabItem.Music, TileSize.W),
                (IslandTabItem.Music, TileSize.S), (TileRegistry.AnalogClock, TileSize.S), face => Sample(face, null, morning, empty: true));
            // Ciels : soleil, pluie, neige ; orage, brouillard, nuages ; nuit étoilée, ciel doré, nuit de pluie.
            var skies = new[]
            {
                ("4f-meteo-soleil-pluie-neige", Weather(0, true, 18), Weather(63, true, 9), Weather(73, true, -3.4), morning),
                ("4f-meteo-orage-brouillard-nuages", Weather(95, true, 24), Weather(45, true, 6), Weather(3, true, 11), morning),
                ("4f-meteo-nuit-dore-partiel", Weather(1, false, 4), Weather(2, true, 16), Weather(61, false, 7), new DateTime(2026, 11, 1, 17, 40, 0)),
            };
            foreach (var (name, large, wide, small, time) in skies)
            {
                await PoseAsync(folder, name, (IslandTabItem.Widgets, TileSize.L), (IslandTabItem.Widgets, TileSize.W),
                    (IslandTabItem.Widgets, TileSize.S), (TileRegistry.AnalogClock, TileSize.S), face =>
                    {
                        if (face is WeatherTileFace weather)
                        {
                            weather.Sample = weather.TileSize switch { TileSize.L => large, TileSize.W => wide, _ => small };
                            // Golden : seule la tuile W est « de jour » vers 17 h 40 (les autres sont de nuit).
                            weather.SampleTime = time;
                        }
                        if (face is AnalogClockFace clock) clock.SampleTime = time;
                    });
            }
            // Horloge : L (chiffres, date, trotteuse à l'accent) et S, avec la musique en W et le calendrier.
            await PoseAsync(folder, "4f-horloge", (TileRegistry.AnalogClock, TileSize.L), (IslandTabItem.Music, TileSize.W),
                (TileRegistry.AnalogClock, TileSize.S), (IslandTabItem.Calendar, TileSize.S), face => Sample(face, Weather(2, true, 12), morning, empty: false));
        }
        finally
        {
            foreach (var face in TileGrid.Faces) Sample(face, null, null, empty: false);
            LiveTicker.Frozen = false;
            RefreshGrid(); // la vraie disposition revient
            GoHome(animate: false);
            FinishAnimations();
        }
    }

    private static void Sample(TileFace face, WeatherReport? weather, DateTime? time, bool empty)
    {
        switch (face)
        {
            case WeatherTileFace w:
                w.Sample = weather;
                w.SampleTime = time;
                break;
            case AnalogClockFace c:
                c.SampleTime = time;
                break;
            case MusicTileFace m:
                m.SampleEmpty = empty;
                m.SampleLevel = time == null ? null : 0.55;
                break;
        }
    }

    /// <summary>Une page : L en haut à gauche, W en haut à droite, deux S dessous à droite ; images posées puis rendues.</summary>
    private async Task PoseAsync(string folder, string name, (string Id, TileSize Size) large, (string Id, TileSize Size) wide,
        (string Id, TileSize Size) first, (string Id, TileSize Size) second, Action<TileFace> sample)
    {
        var slots = new List<TileSlot>
        {
            new(large.Id, large.Size, 0, 0, 0), new(wide.Id, wide.Size, 0, 2, 0),
            new(first.Id, first.Size, 0, 2, 1), new(second.Id, second.Size, 0, 3, 1),
        };
        var context = TileContext;
        TileGrid.Show(slots, (definition, size) => definition.Create(context, size));
        foreach (var face in TileGrid.Faces) sample(face);
        TileGrid.RefreshAll();
        await CoverBlur.WhenReady(); // pochette floutée (calculée hors du fil de l'interface)
        foreach (var face in TileGrid.Faces.OfType<LiveTileFace>()) face.Pose(0.4); // barres du visualiseur bien variées à ce temps-là
        foreach (var face in TileGrid.Faces.Where(f => f is WeatherTileFace { ShownSky: WeatherSky.Storm })) ((LiveTileFace)face).Pose(0.05); // l'éclair
        FinishAnimations();
        _opal.HideSweep();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, name + ".png"));
    }
}
