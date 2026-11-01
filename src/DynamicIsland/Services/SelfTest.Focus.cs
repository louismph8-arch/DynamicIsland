using System.IO;
using DynamicIsland.Services.NightLight;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la concentration (7F) : Ne pas déranger (durées, « jusqu'à demain matin »), courbe de la veilleuse (valeurs
/// pures), aller-retour sauvegarde / remise des rampes avec de FAUX écrans dans un fichier de test, programmation. Les vrais
/// écrans sont seulement LUS (rampe, HDR) : leur rampe n'est jamais changée par ce test.
/// </summary>
internal static partial class SelfTest
{
    private sealed class FakeGammaDevice(string name, ushort[]? ramp, bool refuses = false) : IGammaDevice
    {
        public string Name { get; } = name;
        public ushort[]? Ramp { get; private set; } = ramp;
        public int Writes { get; private set; }

        public bool TryRead(out ushort[] value)
        {
            value = Ramp is { } r ? (ushort[])r.Clone() : new ushort[GammaRamp.Length];
            return Ramp != null;
        }

        public bool TryWrite(ushort[] value)
        {
            if (refuses) return false;
            Ramp = (ushort[])value.Clone();
            Writes++;
            return true;
        }
    }

    private static void Focus(List<string> lines)
    {
        // Ne pas déranger : prochain 8 h, minutes restantes.
        var evening = new DateTime(2026, 10, 31, 22, 0, 0);
        var night = new DateTime(2026, 11, 1, 3, 0, 0);
        var eight = new DateTime(2026, 11, 1, 8, 0, 0);
        Check(lines, FocusService.NextMorning(evening) == new DateTime(2026, 11, 1, 8, 0, 0)
                     && FocusService.NextMorning(night) == new DateTime(2026, 11, 1, 8, 0, 0)
                     && FocusService.NextMorning(eight) == new DateTime(2026, 11, 2, 8, 0, 0),
            "Ne pas déranger « jusqu'à demain matin » : 22 h → lendemain 8 h, 3 h → 8 h le jour même, 8 h pile → lendemain");
        var now = new DateTime(2026, 10, 31, 14, 0, 0);
        Check(lines, FocusService.MinutesLeft(null, now) == 0 && FocusService.MinutesLeft(now.AddMinutes(-1), now) == 0
                     && FocusService.MinutesLeft(DateTime.MaxValue, now) == -1 && FocusService.MinutesLeft(now.AddMinutes(25), now) == 25
                     && FocusService.MinutesLeft(now.AddMinutes(24.5), now) == 25 && FocusService.MinutesLeft(now.AddHours(1), now) == 60,
            "Ne pas déranger : temps restant (arrêté, sans fin, 25 min, 24,5 → 25 min, 1 h)");
        Check(lines, FocusService.Choices.Length == 4 && FocusService.Choices.All(c => FocusService.Label(c).Length > 0),
            "Ne pas déranger : 4 durées proposées (30 min, 1 h, demain matin, jusqu'à ce qu'on l'arrête), toutes nommées");

        // Courbe de la veilleuse.
        var identity = GammaRamp.Identity();
        var (r0, g0, b0) = GammaRamp.Multipliers(GammaRamp.NeutralKelvin);
        var (r1, g1, b1) = GammaRamp.Multipliers(GammaRamp.WarmestKelvin);
        var (_, _, b3400) = GammaRamp.Multipliers(3400);
        Check(lines, GammaRamp.Kelvin(0) == 6500 && GammaRamp.Kelvin(1) == 2700 && Math.Abs(GammaRamp.Kelvin(0.5) - 4600) < 0.01,
            "veilleuse : intensité 0 / 50 / 100 % = 6500 / 4600 / 2700 K");
        Check(lines, Math.Abs(r0 - 1) < 1e-9 && Math.Abs(g0 - 1) < 1e-9 && Math.Abs(b0 - 1) < 1e-9
                     && Math.Abs(r1 - 1) < 1e-9 && g1 is > 0.6 and < 0.72 && b1 is > 0.28 and < 0.42 && b3400 > b1 && g1 > b1,
            $"veilleuse : couleurs à 2700 K (rouge {r1:0.00}, vert {g1:0.00}, bleu {b1:0.00}), 6500 K = neutre");
        Check(lines, GammaRamp.Same(GammaRamp.Build(identity, 0), identity), "veilleuse : intensité 0 = rampe d'origine exacte");
        double max = GammaRamp.MaxStrength(identity);
        Check(lines, !GammaRamp.WithinWindowsLimit(GammaRamp.Build(identity, 1)) && max is > 0.6 and < 0.95
                     && GammaRamp.WithinWindowsLimit(GammaRamp.Build(identity, max)),
            $"veilleuse : limite de Windows respectée (2700 K refusé, au plus {max:P0} ≈ {GammaRamp.Kelvin(max):0} K)");
        var zeros = new ushort[GammaRamp.Length];
        Check(lines, GammaRamp.IsSane(identity) && !GammaRamp.IsSane(zeros) && !GammaRamp.IsSane(new ushort[10])
                     && GammaRamp.Same(GammaRamp.OriginalOrIdentity(zeros), identity),
            "veilleuse : une rampe noire / abîmée n'est jamais réécrite (rampe neutre à la place)");
        var calibrated = Calibrated();
        Check(lines, GammaRamp.Decode(GammaRamp.Encode(calibrated)) is { } decoded && GammaRamp.Same(decoded, calibrated)
                     && GammaRamp.Decode("pas une rampe") == null,
            "veilleuse : rampe enregistrée puis relue à l'identique");

        // Aller-retour avec de faux écrans et un fichier de test (jamais veilleuse.txt).
        var file = Path.Combine(Path.GetTempPath(), $"opaline-veilleuse-test-{Environment.ProcessId}.txt");
        try
        {
            if (File.Exists(file)) File.Delete(file);
            var store = new GammaStore(file);
            var left = new FakeGammaDevice(@"\\.\DISPLAY1", Calibrated());
            var main = new FakeGammaDevice(@"\\.\DISPLAY2", GammaRamp.Identity());
            var broken = new FakeGammaDevice(@"\\.\DISPLAY3", null);
            var stubborn = new FakeGammaDevice(@"\\.\DISPLAY4", GammaRamp.Identity(), refuses: true);
            var originalLeft = (ushort[])left.Ramp!.Clone();

            var savedLeft = store.EnsureOriginal(left);
            bool writtenFirst = File.Exists(file) && store.Load().ContainsKey(left.Name); // gardée AVANT tout changement
            left.TryWrite(GammaRamp.Build(savedLeft!, 0.5));
            var again = store.EnsureOriginal(left); // déjà gardée : l'originale, pas la rampe chaude
            var savedMain = store.EnsureOriginal(main);
            main.TryWrite(GammaRamp.Build(savedMain!, 0.5));
            var savedBroken = store.EnsureOriginal(broken);
            store.EnsureOriginal(stubborn);
            Check(lines, writtenFirst && again != null && GammaRamp.Same(again, originalLeft) && savedBroken != null
                         && GammaRamp.Same(savedBroken, GammaRamp.Identity()) && store.Load().Count == 4,
                "veilleuse : rampe d'origine écrite avant le premier changement, jamais écrasée (écran illisible = neutre)");

            bool allBack = store.RestoreAll([left, stubborn]); // DISPLAY2 et 3 débranchés, DISPLAY4 refuse
            Check(lines, !allBack && GammaRamp.Same(left.Ramp!, originalLeft) && store.Load().Keys.Order().SequenceEqual(
                    new[] { main.Name, broken.Name, stubborn.Name }.Order(), StringComparer.OrdinalIgnoreCase),
                "veilleuse : écran présent remis à l'identique, écrans absents ou qui refusent gardés pour plus tard");
            stubborn = new FakeGammaDevice(stubborn.Name, GammaRamp.Identity());
            bool done = store.RestoreAll([left, main, broken, stubborn]);
            Check(lines, done && !File.Exists(file) && GammaRamp.Same(main.Ramp!, GammaRamp.Identity()) && GammaRamp.Same(broken.Ramp!, GammaRamp.Identity()),
                "veilleuse : tout remis = fichier d'état effacé");
            Check(lines, store.RestoreAll([left]) && left.Writes == 2, "veilleuse : rien à remettre = aucun écran touché");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }

        // Programmation.
        TimeOnly T(int h, int m = 0) => new(h, m);
        Check(lines, NightLightService.InRange(T(22), T(21), T(7)) && NightLightService.InRange(T(6, 59), T(21), T(7))
                     && !NightLightService.InRange(T(7), T(21), T(7)) && !NightLightService.InRange(T(12), T(21), T(7))
                     && NightLightService.InRange(T(14), T(13), T(15)) && !NightLightService.InRange(T(16), T(13), T(15))
                     && !NightLightService.InRange(T(9), T(9), T(9)),
            "veilleuse : heures fixes (21:00 → 7:00 passe minuit, 13:00 → 15:00, début = fin = jamais)");
        var montreal = (SunCalculator.MontrealLatitude, SunCalculator.MontrealLongitude);
        Check(lines, !NightLightService.ScheduleOn(NightLightSchedule.Sun, "21:00", "07:00", new DateTime(2026, 6, 21, 12, 0, 0), montreal)
                     && NightLightService.ScheduleOn(NightLightSchedule.Sun, "21:00", "07:00", new DateTime(2026, 6, 21, 23, 30, 0), montreal)
                     && NightLightService.ScheduleOn(NightLightSchedule.Sun, "21:00", "07:00", new DateTime(2026, 6, 21, 22, 0, 0), null)
                     && NightLightService.ScheduleOn(NightLightSchedule.Always, "21:00", "07:00", new DateTime(2026, 6, 21, 12, 0, 0), null),
            "veilleuse : du coucher au lever du soleil (Montréal), sans ville = 21:00 → 7:00, « toujours »");
        Check(lines, !NightLightService.Decide(false, true, true, false, true) && !NightLightService.Decide(true, true, null, true, true)
                     && NightLightService.Decide(true, true, null, true, false) && NightLightService.Decide(true, false, true, false, true)
                     && !NightLightService.Decide(true, true, false, false, true) && NightLightService.Decide(true, true, null, false, true),
            "veilleuse : décision (option coupée, pause en jeu, choix manuel jusqu'au prochain passage)");
        Check(lines, Settings.SettingsWindow.ParseHour("21h") == "21:00" && Settings.SettingsWindow.ParseHour("9:30") == "09:30"
                     && Settings.SettingsWindow.ParseHour("abc") == null && Settings.SettingsWindow.ParseHour("25:00") == null,
            "veilleuse : heures tapées (« 21h », « 9:30 », refus de « abc » et « 25:00 »)");
        var defaults = new NightLightSettings();
        Check(lines, !defaults.Enabled && defaults.Strength == 50 && defaults.PauseInGames && defaults.Schedule == NightLightSchedule.Always,
            "veilleuse : désactivée par défaut (50 %, coupée en jeu)");

        // Vrais écrans : LECTURE seulement (rampe, HDR, veilleuse de Windows).
        var screens = GdiGammaDevice.All();
        int readable = screens.Count(d => d.TryRead(out var ramp) && GammaRamp.IsSane(ramp));
        var hdr = DisplayFacts.HdrScreens();
        var windows = DisplayFacts.WindowsNightLightOn();
        Check(lines, screens.Count > 0,
            $"vrais écrans lus sans rien changer : {screens.Count} écran(s), {readable} rampe(s) lisible(s), HDR : {(hdr.Count == 0 ? "aucun" : string.Join(", ", hdr))}, "
            + $"veilleuse de Windows : {(windows is { } on ? (on ? "active" : "inactive") : "inconnue")}");
    }

    /// <summary>Rampe d'un écran « calibré » (gamma un peu différent par canal), pour vérifier qu'elle est gardée.</summary>
    private static ushort[] Calibrated()
    {
        var ramp = new ushort[GammaRamp.Length];
        double[] gammas = [0.95, 1.0, 1.05];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < GammaRamp.Size; i++)
                ramp[c * GammaRamp.Size + i] = (ushort)Math.Round(Math.Pow(i / 255.0, gammas[c]) * 65535);
        return ramp;
    }
}
