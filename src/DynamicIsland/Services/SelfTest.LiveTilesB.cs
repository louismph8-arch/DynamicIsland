using System.Windows;
using DynamicIsland.Services.Friends;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des tuiles vivantes du lot B (refonte O6) : Moniteur, Amis, Profil construits dans leurs tailles avec des données
/// d'exemple (tout tient dans la case), calculs purs (respiration bornée, profils montrés, profil suivant, tri des amis), et
/// surtout : RIEN ne tourne pour une tuile d'une page cachée ni île fermée (mesures, minuterie, relecture des amis). Aucun profil
/// n'est changé, aucun ami n'est demandé au serveur. Thread de l'interface (contrôles WPF, jamais affichés).
/// </summary>
internal static partial class SelfTest
{
    private static void LiveTilesBTest(List<string> lines)
    {
        var context = new TileContext
        {
            Media = new MediaService(Application.Current.Dispatcher),
            Timer = new TimerService(),
            ToolAvailable = _ => true,
            ExistingView = _ => null,
        };
        var monitor = TileRegistry.Find(LiveTilesB.MonitorId);
        var profile = TileRegistry.Find(LiveTilesB.ProfileId);
        var friends = TileRegistry.Find(IslandTabItem.Friends);
        Check(lines, monitor is { DefaultSize: TileSize.W, CreateFace: not null } && monitor.Sizes.Length == 3
                     && profile is { DefaultSize: TileSize.W, CreateFace: not null } && profile.Sizes.SequenceEqual([TileSize.S, TileSize.W])
                     && friends?.CreateFace != null,
            "registre : Moniteur (S / W / L, W par défaut), Profil (S / W, W par défaut), Amis = tuile vivante");
        if (monitor == null || profile == null || friends == null) return;

        bool samples = TileSamples.Enabled;
        var sampleFriends = TileSamples.Friends;
        try
        {
            TileSamples.Enabled = true;
            TileSamples.Friends = SettingsWindow.SampleFriends.Friends;

            // Construites dans chaque taille, avec des données d'exemple : rien ne dépasse de la case.
            var overflow = new List<string>();
            foreach (var definition in new[] { monitor, friends, profile })
                foreach (var size in definition.Sizes)
                {
                    var face = definition.Create(context, size);
                    var (columns, rows) = size.Span();
                    double width = columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap;
                    double height = rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap;
                    face.Width = width;
                    face.Height = height;
                    if (face is MonitorTileFace m) m.Show(TileSamples.Stats);
                    face.Refresh();
                    face.SettleMotion();
                    face.Measure(new Size(width, height));
                    face.Arrange(new Rect(0, 0, width, height));
                    var inner = (FrameworkElement)face.Child;
                    inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double innerWidth = width - face.Padding.Left - face.Padding.Right - 2 * face.BorderThickness.Left;
                    double innerHeight = height - face.Padding.Top - face.Padding.Bottom - 2 * face.BorderThickness.Top;
                    if (inner.DesiredSize.Height > innerHeight + 0.5 || inner.DesiredSize.Width > innerWidth + 0.5)
                        overflow.Add($"{definition.Id} {size} ({inner.DesiredSize.Width:0} × {inner.DesiredSize.Height:0} pour {innerWidth:0} × {innerHeight:0})");
                }
            Check(lines, overflow.Count == 0, overflow.Count == 0
                ? "Moniteur S / W / L, Amis S / W / L, Profil S / W construits avec des données d'exemple : tout tient dans la case"
                : "contenu trop grand : " + string.Join(", ", overflow));

            // Moniteur : valeurs posées, alerte au-delà de 85 %, respiration bornée.
            var gauge = (MonitorTileFace)monitor.Create(context, TileSize.W);
            gauge.Show(new SystemStats(34, 88, null, null, null));
            gauge.SettleMotion();
            var rings = gauge.Rings;
            var calm = new SystemStats(10, 50, 5, 0, 0);
            gauge.Show(calm);
            gauge.SettleMotion();
            bool strokes = Enumerable.Range(-20, 41).Select(i => i / 10.0).All(b =>
                TileRing.StrokeFor(4, b) is var s && s >= 4 * (1 - LiveTileShape.RingBreath) - 1e-9 && s <= 4 * (1 + LiveTileShape.RingBreath) + 1e-9
                && TileRing.GlowFor(b) >= LiveTileShape.RingGlowMin - 1e-9 && TileRing.GlowFor(b) <= LiveTileShape.RingGlowMax + 1e-9);
            Check(lines, Math.Abs(rings.Cpu - 0.34) < 1e-6 && Math.Abs(rings.Ram - 0.88) < 1e-6 && rings.RamAlert && !gauge.Rings.RamAlert
                         && Math.Abs(gauge.Rings.Ram - 0.5) < 1e-6 && strokes,
                $"Moniteur : anneaux à 34 % / 88 % (mémoire en rouge au-delà de {LiveTileShape.Alert:P0}), retour à 50 % sans alerte ; respiration = épaisseur ± {LiveTileShape.RingBreath:P0}, lueur {LiveTileShape.RingGlowMin:0.00}–{LiveTileShape.RingGlowMax:0.00}");

            // Profil : 5 pastilles au plus, l'actif toujours dedans (ordre gardé) ; profil suivant en boucle.
            var fake = Enumerable.Range(1, 7).Select(i => new Profile { Id = $"p{i}", Name = $"P{i}" }).ToList();
            var shown = ProfileTileFace.Shown(fake, "p7", LiveTileShape.PastillesW).Select(p => p.Id).ToList();
            bool profilesOk = shown.SequenceEqual(["p1", "p2", "p3", "p4", "p7"])
                              && ProfileTileFace.Shown(fake.Take(3).ToList(), "p2", 5).Count == 3
                              && ProfileTileFace.Next(fake, "p7") == "p1" && ProfileTileFace.Next(fake, "p3") == "p4"
                              && ProfileTileFace.Next(fake, "inconnu") == "p1" && ProfileTileFace.Next([], null) == null;
            Check(lines, profilesOk, $"Profil : 7 profils → pastilles {string.Join(", ", shown)} (l'actif « p7 » gardé) ; suivant en boucle (p7 → p1)");

            // Amis : en jeu d'abord, puis en ligne, puis hors ligne.
            var sorted = FriendsTileFace.Sorted([new Friend("A", "Zoé", "offline", null), new Friend("B", "Léa", "online", null), new Friend("C", "Max", "game", "Valorant")]);
            Check(lines, sorted.Select(f => f.Code).SequenceEqual(["C", "B", "A"]), "Amis : Max (en jeu), puis Léa (en ligne), puis Zoé (hors ligne)");

            // Page cachée / île fermée : rien ne tourne. Mesures réelles du Moniteur (lecture seule) seulement page affichée.
            TileSamples.Enabled = false;
            TileSamples.Friends = null;
            var grid = new TileGridView();
            var slots = TileLayout.Place([
                Enumerable.Range(0, 8).Select(i => ($"{IslandTabItem.All[i]}", TileSize.S)).ToList(),
                [(LiveTilesB.MonitorId, TileSize.W), (IslandTabItem.Friends, TileSize.W), (LiveTilesB.ProfileId, TileSize.W)],
            ]);
            grid.Show(slots, (d, s) => d.Create(context, s));
            var live = grid.Faces.Where(f => f.Definition.Id is LiveTilesB.MonitorId or IslandTabItem.Friends or LiveTilesB.ProfileId).ToList();
            var m2 = live.OfType<MonitorTileFace>().Single();
            var f2 = live.OfType<FriendsTileFace>().Single();
            bool closed = live.All(f => !f.IsLive) && !m2.IsRunning && !f2.IsWatching;
            grid.IsActive = true; // île ouverte, mais la page 1 est affichée
            bool hidden = live.All(f => !f.IsLive) && !m2.IsRunning && !f2.IsWatching;
            grid.ShowPage(1, animate: false);
            bool shownLive = live.All(f => f.IsLive) && m2.IsRunning && f2.IsWatching;
            grid.ShowPage(0, animate: false);
            bool backHidden = live.All(f => !f.IsLive) && !m2.IsRunning && !f2.IsWatching;
            grid.ShowPage(1, animate: false);
            grid.IsActive = false; // île refermée
            bool stopped = live.All(f => !f.IsLive) && !m2.IsRunning && !f2.IsWatching;
            Check(lines, closed && hidden && shownLive && backHidden && stopped,
                $"rien ne tourne île fermée ({closed}) ni page cachée ({hidden}, {backHidden}) ; page affichée : mesures + relecture des amis ({shownLive}) ; île refermée : tout arrêté ({stopped})");
        }
        finally
        {
            TileSamples.Enabled = samples;
            TileSamples.Friends = sampleFriends;
        }
    }
}
