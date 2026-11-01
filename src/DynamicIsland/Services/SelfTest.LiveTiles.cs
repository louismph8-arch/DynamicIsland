using System.Windows;
using DynamicIsland.Settings;
using DynamicIsland.Tiles;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des tuiles vivantes (O6, lot « vivantes A ») : Musique (état vide, pochette floutée calculée une fois, visualiseur),
/// Météo (ciel d'après le code WMO, particules bornées, « −3° »), Horloge analogique (angles continus, trotteuse), et SURTOUT :
/// rien ne tourne pour une tuile hors de la page affichée ni île fermée. Sur le thread de l'interface ; rien n'est affiché, rien
/// n'est demandé au réseau (contexte « hors ligne », météo d'exemple).
/// </summary>
internal static partial class SelfTest
{
    private static void LiveTileChecks(List<string> lines, TileContext baseContext)
    {
        var media = new MediaService(Application.Current.Dispatcher);
        media.UseDemo(); // morceau inventé avec pochette : rien de ce qui joue sur le PC
        var context = new TileContext
        {
            Media = media,
            Timer = baseContext.Timer,
            ToolAvailable = baseContext.ToolAvailable,
            ExistingView = baseContext.ExistingView,
            MeterLevel = () => 0.6, // vumètre simulé
            Offline = true,
        };
        var music = TileRegistry.Find(IslandTabItem.Music)!;
        var widgets = TileRegistry.Find(IslandTabItem.Widgets)!;
        var clock = TileRegistry.Find(TileRegistry.AnalogClock)!;

        // Construites dans toutes leurs tailles, avec la bonne vue vivante.
        var built = new List<string>();
        foreach (var (definition, type) in new[] { (music, typeof(MusicTileFace)), (widgets, typeof(WeatherTileFace)), (clock, typeof(AnalogClockFace)) })
            foreach (var size in definition.Sizes)
            {
                var face = definition.Create(context, size);
                face.Refresh();
                face.Measure(new Size(face.Width is > 0 ? face.Width : 400, 400));
                if (face.GetType() == type) built.Add($"{definition.Id}:{size}");
            }
        Check(lines, built.Count == 8 && clock.DefaultSize == TileSize.L && !clock.Allows(TileSize.W),
            $"tuiles vivantes construites : {string.Join(", ", built)} (Horloge : L par défaut, S possible)");

        // Musique : état vide, puis morceau d'exemple ; la pochette floutée est calculée hors du fil de l'interface, une fois.
        var empty = (MusicTileFace)music.Create(context, TileSize.W);
        empty.SampleEmpty = true;
        empty.Refresh();
        var playing = (MusicTileFace)music.Create(context, TileSize.L);
        playing.Refresh();
        bool blurReady = CoverBlur.WhenReady().Wait(TimeSpan.FromSeconds(10));
        playing.Refresh();
        var again = (MusicTileFace)music.Create(context, TileSize.S);
        again.Refresh(); // même pochette : l'image floutée est déjà là (pas de 2e calcul)
        Check(lines, empty.ShowsEmpty && !empty.ShowsArt && blurReady && playing.ShowsArt && !playing.ShowsEmpty && again.ShowsArt,
            "Musique : état vide sans pochette, pochette floutée prête hors du fil de l'interface puis réutilisée par les autres tailles");

        // Visualiseur : vumètre à 60 % → barres plus hautes que le repos ; en pause → barres basses.
        playing.SampleLevel = 0.6;
        playing.Pose(1.3);
        bool raised = playing.BarLevels.All(l => l > Themes.LiveTiles.BarRest);
        Check(lines, raised && playing.BarLevels.All(l => l <= 1), "Musique : visualiseur piloté par le niveau du son (barres entre le repos et le haut)");

        // Météo : codes WMO → ciel, signe moins, particules bornées pour tous les ciels et toutes les tailles.
        bool skies = WeatherTileFace.SkyOf(0) == WeatherSky.Clear && WeatherTileFace.SkyOf(2) == WeatherSky.Partly && WeatherTileFace.SkyOf(3) == WeatherSky.Cloudy
                     && WeatherTileFace.SkyOf(45) == WeatherSky.Fog && WeatherTileFace.SkyOf(63) == WeatherSky.Rain && WeatherTileFace.SkyOf(81) == WeatherSky.Rain
                     && WeatherTileFace.SkyOf(73) == WeatherSky.Snow && WeatherTileFace.SkyOf(95) == WeatherSky.Storm
                     && WeatherTileFace.Degrees(-3.4) == "−3°" && WeatherTileFace.Degrees(21.5) == "22°";
        int most = 0;
        var wrongSky = new List<string>();
        foreach (var code in new[] { 0, 2, 3, 45, 63, 73, 95 })
            foreach (bool day in new[] { true, false })
                foreach (var size in widgets.Sizes)
                {
                    var face = (WeatherTileFace)widgets.Create(context, size);
                    face.Sample = new WeatherReport("Montréal", -3.4, -8, 1, code, day, DateTime.Now);
                    face.SampleTime = new DateTime(2026, 11, 1, day ? 13 : 22, 0, 0);
                    face.Pose(4.2);
                    most = Math.Max(most, face.Particles);
                    if (face.ShownSky != WeatherTileFace.SkyOf(code)) wrongSky.Add($"{code}:{size}");
                }
        Check(lines, skies && wrongSky.Count == 0 && most <= Themes.LiveTiles.MaxParticles,
            $"Météo : ciel d'après le code WMO (soleil, nuages, brouillard, pluie, neige, orage, nuit), « −3° », au plus {most} particules (≤ {Themes.LiveTiles.MaxParticles})");

        // Horloge : angles continus (la trotteuse glisse : 100 ms = 0,6°), 10:09:30,5 → 304,75° / 57,05° / 183°.
        var (h, m, s) = AnalogClockFace.Angles(new DateTime(2026, 11, 1, 10, 9, 30, 500));
        var (_, _, s2) = AnalogClockFace.Angles(new DateTime(2026, 11, 1, 10, 9, 30, 600));
        var clockFace = (AnalogClockFace)clock.Create(context, TileSize.L);
        clockFace.SampleTime = new DateTime(2026, 11, 1, 22, 45, 15);
        clockFace.Refresh();
        var shown = clockFace.ShownAngles;
        Check(lines, Math.Abs(h - 304.75) < 0.01 && Math.Abs(m - 57.05) < 0.01 && Math.Abs(s - 183) < 0.01 && Math.Abs(s2 - s - 0.6) < 0.001
                     && Math.Abs(shown.Hour - (10 * 30 + 45.25 / 2)) < 0.01 && Math.Abs(shown.Second - 90) < 0.01 && clockFace.ShowsSecondHand,
            "Horloge analogique : aiguilles d'après l'heure, trotteuse continue (pas de saut à la seconde)");

        // Rien ne tourne hors de la vue : grille par défaut, île fermée → 0 ; page 1 → Musique / Météo seulement ; page 2 →
        // l'Horloge seulement ; île refermée → plus rien, minuterie arrêtée.
        int baseline = LiveTicker.Count;
        var grid = new TileGridView();
        grid.Show(TileLayout.Place(IslandGrid.Default().Ordered()), (d, size) =>
        {
            var face = d.Create(context, size);
            if (face is WeatherTileFace weather) weather.Sample = new WeatherReport("Montréal", 6, 2, 9, 61, true, DateTime.Now);
            return face;
        });
        var gridMusic = grid.FaceOf(IslandTabItem.Music) as LiveTileFace;
        var gridClock = grid.FaceOf(TileRegistry.AnalogClock) as LiveTileFace;
        var gridWeather = grid.FaceOf(IslandTabItem.Widgets) as LiveTileFace;
        bool closedIdle = LiveTicker.Count == baseline && gridMusic is { IsLive: false } && gridClock is { IsLive: false };
        grid.IsActive = true; // île ouverte, page 1
        bool page1 = gridMusic is { IsLive: true } && gridWeather is { IsLive: true } && gridClock is { IsLive: false };
        bool allowed = LiveTicker.Allowed;
        bool page1Running = !allowed || gridMusic!.IsAnimating && gridWeather!.IsAnimating && !gridClock!.IsAnimating;
        grid.Turn(1, animate: false);
        bool page2 = gridMusic is { IsLive: false } && gridWeather is { IsLive: false } && gridClock is { IsLive: true }
                     && (!allowed || gridClock!.IsAnimating && !gridMusic!.IsAnimating);
        grid.IsActive = false; // île refermée
        bool closed = LiveTicker.Count == baseline && gridClock is { IsLive: false } && gridMusic is { IsLive: false }
                      && (baseline > 0 || !LiveTicker.IsRunning);
        Check(lines, closedIdle && page1 && page1Running && page2 && closed,
            $"tuiles vivantes : rien ne tourne île fermée ni hors de la page affichée (page 1 : Musique + Météo, page 2 : Horloge ; animations {(allowed ? "permises" : "coupées par Windows / le verre : tout figé")})");
    }
}
