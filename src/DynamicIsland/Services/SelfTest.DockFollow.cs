using DynamicIsland.Dock;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>Auto-test : option « le dock suit l'écran de la souris » (décision pure, écrans simulés, rien n'est déplacé).</summary>
internal static partial class SelfTest
{
    private static void DockFollowMouse(List<string> lines)
    {
        foreach (double scale in new[] { 1.0, 1.5, 2.0 })
        {
            var screens = TestScreens(scale).ToList();
            var primary = screens[0];
            var left = screens[1];
            var top = screens[2];
            var laptop = screens[3];
            var t0 = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            static POINT Middle(ScreenInfo.Monitor m) =>
                new() { X = m.Bounds.Left + m.WidthPx / 2, Y = m.Bounds.Top + m.HeightPx / 2 };
            static bool NoGame(ScreenInfo.Monitor _) => false;
            static string Name(ScreenInfo.Monitor? m) => m?.Device ?? "rien";
            static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

            // 1. Souris sur l'écran du dock : on reste.
            var follow = new DockFollow();
            bool stay = follow.Next(Middle(primary), screens, primary.Device, NoGame, false, t0) == null
                        && follow.Next(Middle(primary), screens, primary.Device, NoGame, false, t0 + Ms(2000)) == null;

            // 2. Souris arrêtée sur l'écran du haut : rien avant le délai, puis il y va.
            follow = new DockFollow();
            var a = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0);
            var b = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(200));
            var c = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + DockFollow.Delay + Ms(10));
            bool delayed = a == null && b == null && Name(c) == top.Device;

            // 3. Traverser un écran pour aller sur un autre : le délai repart sur le dernier écran.
            follow = new DockFollow();
            follow.Next(Middle(left), screens, primary.Device, NoGame, false, t0);
            var d = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(300));
            var e = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(600));
            var f = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(800));
            bool crossed = d == null && e == null && Name(f) == top.Device;

            // 4. Glisser / menu / dossier en cours : on ne bouge pas, et l'attente repart de zéro ensuite.
            follow = new DockFollow();
            follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0);
            var g = follow.Next(Middle(top), screens, primary.Device, NoGame, true, t0 + Ms(450));
            var h = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(500));
            var i = follow.Next(Middle(top), screens, primary.Device, NoGame, false, t0 + Ms(950));
            bool busy = g == null && h == null && Name(i) == top.Device;

            // 5. Jeu sur l'écran visé : le dock reste où il est ; jeu sur SON écran : il peut partir sur l'autre.
            follow = new DockFollow();
            bool GameOnTop(ScreenInfo.Monitor m) => m.Device == top.Device;
            follow.Next(Middle(top), screens, primary.Device, GameOnTop, false, t0);
            var j = follow.Next(Middle(top), screens, primary.Device, GameOnTop, false, t0 + Ms(1000));
            follow = new DockFollow();
            bool GameOnPrimary(ScreenInfo.Monitor m) => m.Device == primary.Device;
            follow.Next(Middle(top), screens, primary.Device, GameOnPrimary, false, t0);
            var k = follow.Next(Middle(top), screens, primary.Device, GameOnPrimary, false, t0 + Ms(1000));
            bool game = j == null && Name(k) == top.Device;

            // 6. Souris hors de tout écran : rien.
            follow = new DockFollow();
            var nowhere = new POINT { X = -100000, Y = -100000 };
            follow.Next(nowhere, screens, primary.Device, NoGame, false, t0);
            bool outside = follow.Next(nowhere, screens, primary.Device, NoGame, false, t0 + Ms(1000)) == null;

            // 7. Bord exact : le pixel de gauche du portable est à lui, pas au principal.
            follow = new DockFollow();
            var edge = new POINT { X = laptop.Bounds.Left, Y = laptop.Bounds.Top + 10 };
            follow.Next(edge, screens, primary.Device, NoGame, false, t0);
            bool border = Name(follow.Next(edge, screens, primary.Device, NoGame, false, t0 + Ms(500))) == laptop.Device;

            Check(lines, stay && delayed && crossed && busy && game && outside && border,
                $"{scale:P0} : reste sur son écran {stay}, après {DockFollow.Delay.TotalMilliseconds:0} ms {delayed}, traversée {crossed}, "
                + $"glisser / menu {busy}, écran du jeu évité {game}, hors écran {outside}, bord exact {border}");
        }
    }
}
