using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using DynamicIsland.Design;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>Auto-test du « verre opalin » (refonte O1) : réglage, teintes voisines de l'accent, grain déterministe.</summary>
internal static partial class SelfTest
{
    private static void OpalGlass(List<string> lines)
    {
        // Réglage : « Équilibré » par défaut, enregistré par son nom, relu à l'identique ; ancien fichier sans le champ = défaut.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var look = new AppearanceSettings();
        bool byDefault = look.OpalStyle == OpalStyle.Balanced;
        look.OpalStyle = OpalStyle.Intense;
        var json = JsonSerializer.Serialize(look, options);
        var back = JsonSerializer.Deserialize<AppearanceSettings>(json, options)!;
        var old = JsonSerializer.Deserialize<AppearanceSettings>("{\"Seasons\":true}", options)!;
        Check(lines, byDefault && json.Contains("\"OpalStyle\":\"Intense\"") && back.OpalStyle == OpalStyle.Intense && old.OpalStyle == OpalStyle.Balanced,
            "style opalin : « Balanced » par défaut, enregistré par son nom, relu à l'identique");
        Check(lines, Opal.For(OpalStyle.Discreet) == Opal.Discreet && Opal.For(OpalStyle.Intense) == Opal.Intense
                     && Opal.Discreet.IridAlpha < Opal.Balanced.IridAlpha && Opal.Balanced.IridAlpha < Opal.Intense.IridAlpha
                     && Opal.Discreet.Grain >= 0.02 && Opal.Intense.Grain <= 0.06,
            "3 intensités croissantes, grain entre 2 et 6 %");

        // Teintes voisines : ± décalage autour de l'accent (roue fermée), plus pâles que l'accent.
        var cases = new (Color Accent, double Shift)[] { (Swatch.Indigo, 26), (Color.FromRgb(0xFF, 0x20, 0x40), 34), (Swatch.Green, 18) };
        var wrong = new List<string>();
        foreach (var (accent, shift) in cases)
        {
            var (h, s, _) = ColorTools.ToHsv(accent);
            var irid = OpalPalette.Iridescent(accent, shift, 0.3);
            var hues = irid.Select(c => ColorTools.ToHsv(c).H).ToArray();
            bool ok = ColorTools.HueDistance(hues[0], h - shift) < 4 && ColorTools.HueDistance(hues[1], h) < 4
                      && ColorTools.HueDistance(hues[2], h + shift) < 4 && irid.All(c => ColorTools.ToHsv(c).S <= s + 0.01);
            if (!ok) wrong.Add($"{Swatch.Hex(accent)} → {string.Join(" / ", hues.Select(x => x.ToString("0")))}");
        }
        Check(lines, wrong.Count == 0, wrong.Count == 0 ? "reflets irisés : teintes voisines de l'accent (± décalage, 350° + 34° → 24°), pâles"
                                                        : "teintes voisines fausses : " + string.Join(", ", wrong));

        // Grain : même graine = même image, autre graine = autre image, gris moyen proche de 128.
        var a = OpalPalette.Noise(Opal.GrainSize, Opal.GrainSeed);
        var b = OpalPalette.Noise(Opal.GrainSize, Opal.GrainSeed);
        var c2 = OpalPalette.Noise(Opal.GrainSize, Opal.GrainSeed + 1);
        double mean = Enumerable.Range(0, a.Length / 4).Average(i => a[i * 4]);
        Check(lines, a.AsSpan().SequenceEqual(b) && !a.AsSpan().SequenceEqual(c2) && Math.Abs(mean - 128) < 6 && a.Length == Opal.GrainSize * Opal.GrainSize * 4,
            $"grain déterministe ({Opal.GrainSize}² pixels, gris moyen {mean:0})");

        // Orientation du dégradé : 0° = de gauche à droite, 90° = de haut en bas.
        var (s0, e0) = OpalPalette.Direction(0);
        var (s90, e90) = OpalPalette.Direction(90);
        Check(lines, Math.Abs(s0.X) < 1e-9 && Math.Abs(e0.X - 1) < 1e-9 && Math.Abs(s90.Y) < 1e-9 && Math.Abs(e90.Y - 1) < 1e-9,
            "orientation des reflets selon la souris");

        // Application à l'app (O1) : composition des voiles, pinceaux partagés des tuiles et des menus présents.
        var over = OpalTheme.Over(Color.FromArgb(128, 255, 255, 255), Color.FromArgb(255, 0, 0, 0));
        var clear = OpalTheme.Over(Colors.Transparent, Color.FromArgb(64, 10, 20, 30));
        var resources = System.Windows.Application.Current?.Resources;
        bool brushes = resources != null && new[] { OpalTheme.TileKey, OpalTheme.TileHoverKey, OpalTheme.TileRimKey, OpalTheme.PopupKey, OpalTheme.PopupRimKey }
            .All(key => resources[key] is Brush);
        Check(lines, over.A == 255 && Math.Abs(over.R - 128) <= 1 && clear == Color.FromArgb(64, 10, 20, 30) && brushes,
            "verre opalin appliqué : voiles composés, pinceaux des tuiles et des menus prêts");
    }
}
