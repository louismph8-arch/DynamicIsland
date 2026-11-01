using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using DynamicIsland.Design;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des couleurs (refonte O2) : interpolation OKLCH, contraste WCAG (valeurs connues), réglage automatique d'un texte,
/// d'un accent et d'un voile, palettes sombre / claire lisibles, thème Auto selon le soleil, réglage enregistré. Calculs purs.
/// </summary>
internal static partial class SelfTest
{
    private static void OpalColors(List<string> lines)
    {
        // Contraste WCAG : blanc / noir = 21, une couleur sur elle-même = 1, #767676 sur blanc = 4,54 (valeur de référence).
        double bw = OpalColor.Contrast(Colors.White, Colors.Black);
        double same = OpalColor.Contrast(Swatch.Indigo, Swatch.Indigo);
        double gray = OpalColor.Contrast(Color.FromRgb(0x76, 0x76, 0x76), Colors.White);
        Check(lines, Math.Abs(bw - 21) < 1e-9 && Math.Abs(same - 1) < 1e-9 && Math.Abs(gray - 4.54) < 0.01,
            $"contraste WCAG : blanc / noir {bw:0.##}, même couleur {same:0.##}, #767676 sur blanc {gray:0.00}");

        // OKLCH : aller-retour exact à ±1, extrémités exactes, pas de gris au milieu de bleu → orange, teinte par le plus court chemin.
        var samples = new[] { Swatch.Indigo, Swatch.Orange, Swatch.Green, Swatch.Pink, Color.FromRgb(0x0B, 0x0B, 0x0D), InkPalette.PearlBase, Colors.White, Colors.Black };
        bool roundTrip = samples.All(c => Near(OpalColor.FromOklch(OpalColor.ToOklch(c)), c, 1));
        var blue = Swatch.Blue;
        var orange = Swatch.Orange;
        var mid = OpalColor.Mix(blue, orange, 0.5);
        var rgbMid = OpalPalette.Mix(blue, orange, 0.5);
        double chromaOk = OpalColor.ToOklch(mid).C, chromaRgb = OpalColor.ToOklch(rgbMid).C;
        var red1 = ColorTools.FromHsv(350, 0.8, 0.9);
        var red2 = ColorTools.FromHsv(10, 0.8, 0.9);
        double hueMid = ColorTools.ToHsv(OpalColor.Mix(red1, red2, 0.5)).H;
        Check(lines, roundTrip && OpalColor.Mix(blue, orange, 0) == blue && OpalColor.Mix(blue, orange, 1) == orange
                     && chromaOk > chromaRgb * 1.5 && ColorTools.HueDistance(hueMid, 0) < 6,
            $"interpolation OKLCH : aller-retour exact, bleu → orange saturé au milieu (chroma {chromaOk:0.000} contre {chromaRgb:0.000} en RVB), 350° ↔ 10° passe par {hueMid:0}°");
        Check(lines, OpalColor.Ease(0) == 0 && OpalColor.Ease(1) == 1 && Math.Abs(OpalColor.Ease(0.5) - 0.5) < 1e-9 && OpalColor.Ease(0.25) < 0.25,
            "courbe de transition douce (lente au début et à la fin)");
        var a = new OpalShade.State(blue, blue, Swatch.IslandBase, 0);
        var b = new OpalShade.State(orange, orange, InkPalette.PearlBase, 1);
        var half = OpalShade.Between(a, b, 0.5);
        Check(lines, OpalShade.Between(a, b, 0) == a && OpalShade.Between(a, b, 1) == b && Math.Abs(half.Light - 0.5) < 1e-9 && half.AccentStart == mid,
            "transition d'un état à l'autre : départ, arrivée et milieu corrects");

        // Lisibilité : texte rendu assez opaque, puis éclairci / assombri ; accent clair assombri sur l'opale blanche.
        var wrong = new List<string>();
        var readable = new (Color Text, Color Background, double Min)[]
        {
            (Swatch.WhiteA(0x66), Color.FromRgb(0x30, 0x30, 0x34), OpalColor.TextContrast),  // texte tertiaire sombre → plus opaque
            (Color.FromRgb(0xFF, 0xD6, 0x0A), InkPalette.PearlBase, OpalColor.IconContrast),   // jaune sur blanc nacré → assombri
            (Swatch.Teal, InkPalette.PearlBase, OpalColor.IconContrast),
            (Color.FromRgb(0x30, 0x20, 0x80), Swatch.IslandBase, OpalColor.TextContrast),     // indigo foncé sur noir → éclairci
            (Swatch.WithAlpha(InkPalette.LightInk, 0x60), Color.FromRgb(0xE8, 0xE8, 0xEC), OpalColor.TextContrast),
        };
        foreach (var (text, background, min) in readable)
        {
            var fixedColor = OpalColor.Readable(text, background, min);
            double ratio = OpalColor.ContrastOn(fixedColor, background);
            bool hueKept = text.A < 255 || ColorTools.HueDistance(ColorTools.ToHsv(fixedColor).H, ColorTools.ToHsv(text).H) < 12;
            if (ratio < min - 0.01 || !hueKept) wrong.Add($"{Swatch.Hex(text)} sur {Swatch.Hex(background)} → {Swatch.Hex(fixedColor)} ({ratio:0.00})");
        }
        var already = OpalColor.Readable(Colors.White, Colors.Black, OpalColor.TextContrast);
        Check(lines, wrong.Count == 0 && already == Colors.White,
            wrong.Count == 0 ? "texte / accent ajustés automatiquement jusqu'à 4,5 (texte) ou 3 (icônes), teinte gardée, rien à faire s'il est déjà lisible"
                             : "lisibilité non atteinte : " + string.Join(", ", wrong));

        // Voile : le plus léger qui rend le texte lisible sur un fond flouté très clair (et pas plus).
        var white = Colors.White;
        double veil = OpalColor.VeilFor(white, Swatch.IslandBase, Color.FromRgb(0xF0, 0xF0, 0xF0), 0.3, OpalColor.TextContrast);
        Color Surface(double alpha) => OpalTheme.Over(OpalPalette.WithOpacity(Swatch.IslandBase, alpha), Color.FromRgb(0xF0, 0xF0, 0xF0));
        bool minimal = OpalColor.Contrast(white, Surface(veil)) >= 4.5 && OpalColor.Contrast(white, Surface(veil - 0.01)) < 4.5;
        double kept = OpalColor.VeilFor(white, Swatch.IslandBase, Color.FromRgb(0x10, 0x10, 0x20), 0.3, OpalColor.TextContrast);
        double darkInk = OpalColor.VeilFor(InkPalette.LightInk, InkPalette.PearlBase, Color.FromRgb(0x10, 0x10, 0x18), 0.5, OpalColor.TextContrast);
        bool lightOk = OpalColor.ContrastOn(InkPalette.LightInk, OpalTheme.Over(OpalPalette.WithOpacity(InkPalette.PearlBase, darkInk), Color.FromRgb(0x10, 0x10, 0x18))) >= 4.5;
        Check(lines, minimal && kept == 0.3 && lightOk,
            $"voile automatique : {veil:0.00} sur un fond blanc (le minimum pour 4,5), 0,30 gardé sur un fond sombre, opale blanche lisible sur un fond noir ({darkInk:0.00})");

        // Palettes : déjà lisibles sur leur surface de référence, et toujours après correction.
        var issues = new List<string>();
        foreach (var light in new[] { 0.0, 1.0 })
        {
            var state = new OpalShade.State(Swatch.Indigo, Swatch.Violet, light > 0 ? InkPalette.PearlBase : Swatch.IslandBase, light);
            var reference = OpalShade.ReferenceSurface(state, 0.94);
            var raw = light > 0 ? InkPalette.Light : InkPalette.Dark;
            var palette = OpalShade.Palette(light, reference);
            string name = light > 0 ? "clair" : "sombre";
            foreach (var (role, min) in new[] { (InkRole.TextPrimary, 4.5), (InkRole.TextSecondary, 4.5), (InkRole.TextTertiary, 3.0) })
            {
                double before = OpalColor.ContrastOn(raw[(int)role], reference), after = OpalColor.ContrastOn(palette[(int)role], reference);
                if (before < min || after < min) issues.Add($"{name} {role} {before:0.0} → {after:0.0}");
            }
            // À l'opacité la plus basse (50 %) sur le pire décor, la correction doit quand même atteindre les seuils.
            var worst = OpalShade.ReferenceSurface(state, 0.5);
            var fixedPalette = OpalShade.Palette(light, worst);
            if (OpalColor.ContrastOn(fixedPalette[(int)InkRole.TextSecondary], worst) < 4.5) issues.Add($"{name} 50 % non corrigé");
        }
        Check(lines, issues.Count == 0, issues.Count == 0 ? "palettes sombre et opale blanche lisibles (texte ≥ 4,5, légendes ≥ 3), corrigées à 50 % d'opacité"
                                                          : "palette peu lisible : " + string.Join(", ", issues));

        // Opale blanche : voile nacré plus présent, ombre plus douce ; mélange 0 = sombre, 1 = clair.
        var dark = Opal.Balanced;
        var lightLook = Opal.Light(dark);
        Check(lines, lightLook.Smoke >= Opal.LightSmokeMin && lightLook.Shadow.Opacity < dark.Shadow.Opacity && Opal.Blend(dark, lightLook, 0) == dark
                     && Opal.Blend(dark, lightLook, 1) == lightLook && Math.Abs(Opal.Blend(dark, lightLook, 0.5).Smoke - (dark.Smoke + lightLook.Smoke) / 2) < 1e-9,
            $"opale blanche : voile {lightLook.Smoke:0.00}, ombre {lightLook.Shadow.Opacity:0.00} (contre {dark.Shadow.Opacity:0.00}), transition continue");

        // Thème Auto selon le soleil (lever 7 h, coucher 17 h simulés), Comme Windows, fixes ; prochaine bascule.
        var day = new DateTime(2026, 11, 1);
        var sun = new SunCalculator.SunTimes(day.AddHours(7), day.AddHours(17), SunCalculator.Polar.None);
        bool auto = OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(12), sun, false)
                    && !OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(6), sun, true)
                    && !OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(17), sun, true)
                    && OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(7), sun, false)
                    && OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(3), sun with { Polar = SunCalculator.Polar.AlwaysDay }, false)
                    && !OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(12), sun with { Polar = SunCalculator.Polar.AlwaysNight }, true)
                    && OpalColor.IsLight(OpalThemeMode.Auto, day.AddHours(12), null, true);
        bool fixedModes = OpalColor.IsLight(OpalThemeMode.Light, day, sun, false) && !OpalColor.IsLight(OpalThemeMode.Dark, day.AddHours(12), sun, true)
                          && OpalColor.IsLight(OpalThemeMode.Windows, day, sun, true) && !OpalColor.IsLight(OpalThemeMode.Windows, day.AddHours(12), sun, false);
        bool next = OpalColor.NextSwitch(day.AddHours(6), sun) == day.AddHours(7) && OpalColor.NextSwitch(day.AddHours(12), sun) == day.AddHours(17)
                    && OpalColor.NextSwitch(day.AddHours(20), sun) == day.AddDays(1) && OpalColor.NextSwitch(day.AddHours(9), null) == day.AddDays(1);
        Check(lines, auto && fixedModes && next, "thème Auto : clair de 7 h à 17 h (soleil simulé), nuit polaire / jour polaire, Comme Windows, prochaine bascule (une seule minuterie)");

        // Réglage : « Dark » par défaut, enregistré par son nom, relu à l'identique ; ancien fichier sans le champ = sombre.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var settings = new AppearanceSettings();
        bool byDefault = settings.OpalTheme == OpalThemeMode.Dark;
        settings.OpalTheme = OpalThemeMode.Auto;
        var json = JsonSerializer.Serialize(settings, options);
        var back = JsonSerializer.Deserialize<AppearanceSettings>(json, options)!;
        var old = JsonSerializer.Deserialize<AppearanceSettings>("{\"OpalStyle\":\"Intense\"}", options)!;
        Check(lines, byDefault && json.Contains("\"OpalTheme\":\"Auto\"") && back.OpalTheme == OpalThemeMode.Auto && old.OpalTheme == OpalThemeMode.Dark,
            "thème d'Opaline : « Dark » par défaut, enregistré par son nom, relu à l'identique");

        // Fond flouté : quart le plus clair / le plus sombre (pire cas du texte), moyenne.
        var pixels = new byte[400 * 4];
        for (int i = 0; i < 400; i++)
        {
            byte v = (byte)(i < 200 ? 0 : 255);
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = v;
            pixels[i * 4 + 3] = 255;
        }
        var (mean, brightest, darkest) = OpalBackdrop.Measure(pixels);
        Check(lines, Math.Abs(mean - 0.5) < 1e-9 && brightest == Colors.White && darkest == Colors.Black,
            "fond flouté mesuré : moyenne, quart le plus clair et le plus sombre");

        // Brosses du thème : jamais figées (sinon elles ne suivraient plus), et la couleur affichée suit le thème visé.
        // (lues sur le fil de l'interface : ces brosses lui appartiennent, l'auto-test tourne à côté)
        var frozen = new List<string>();
        bool resources = false;
        var app = System.Windows.Application.Current;
        app?.Dispatcher.Invoke(() =>
        {
            frozen = Paint.Themed.Select((brush, i) => (brush, i)).Where(x => x.brush.IsFrozen).Select(x => ((InkRole)x.i).ToString()).ToList();
            resources = Enum.GetValues<InkRole>().All(role => app.Resources[Paint.Key(role)] is SolidColorBrush);
        });
        Check(lines, frozen.Count == 0 && resources,
            frozen.Count == 0 ? $"brosses du thème modifiables et ressources « Paint.* » prêtes (thème {(OpalShade.IsLight ? "clair" : "sombre")})"
                              : "brosses du thème figées : " + string.Join(", ", frozen));
    }

    private static bool Near(Color a, Color b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance && a.A == b.A;
}
