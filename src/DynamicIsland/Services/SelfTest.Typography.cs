using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test de la typographie et des icônes (refonte O4) : police des grands chiffres embarquée et vraiment chargée (Inter
/// Display, trois graisses), chiffres 0–9 de même largeur avec la mise en forme tabulaire (mesurés par WPF, pas seulement dans
/// le fichier), licence OFL copiée à côté de l'exe, style partagé « icône active ». Rien n'est écrit.
/// </summary>
internal static partial class SelfTest
{
    private static void TypographyO4(List<string> lines)
    {
        // Mesures WPF : sur le fil de l'interface (l'auto-test tourne à côté).
        var app = Application.Current;
        if (app != null && !app.Dispatcher.CheckAccess()) { app.Dispatcher.Invoke(() => TypographyO4(lines)); return; }

        // 1. Police trouvée : la famille embarquée existe dans les ressources, en trois graisses, avec des glyphes de chiffres.
        var faces = Fonts.GetTypefaces(new Uri("pack://application:,,,/"), "./Assets/Fonts/")
            .Where(t => t.FontFamily.Source.Contains(FontStack.NumbersFamily, StringComparison.OrdinalIgnoreCase)
                        || (t.TryGetGlyphTypeface(out var g) && g.FamilyNames.Values.Any(n => n.Contains(FontStack.NumbersFamily))))
            .ToList();
        var weights = new HashSet<int>();
        bool digits = faces.Count > 0;
        foreach (var face in faces)
        {
            if (!face.TryGetGlyphTypeface(out var glyphs)) { digits = false; continue; }
            if (face.IsBoldSimulated || face.IsObliqueSimulated) continue; // variantes simulées par WPF : pas des fichiers
            weights.Add(glyphs.Weight.ToOpenTypeWeight());
            digits &= "0123456789:%".All(c => glyphs.CharacterToGlyphMap.ContainsKey(c));
        }
        int[] expected = [NumberWeight.Hairline.ToOpenTypeWeight(), NumberWeight.Light.ToOpenTypeWeight(), NumberWeight.Text.ToOpenTypeWeight()];
        Check(lines, digits && expected.All(weights.Contains),
            $"police des chiffres « {FontStack.NumbersFamily} » embarquée : graisses {string.Join(" / ", weights.Order())} (attendu {string.Join(" / ", expected)}), chiffres 0–9 présents");

        // 2. Le texte posé par Numerals.Apply utilise VRAIMENT Inter (pas le repli Segoe) et ses chiffres sont tabulaires.
        var face0 = new Typeface(FontStack.Numbers, FontStyles.Normal, NumberWeight.Hairline, FontStretches.Normal);
        bool usesInter = face0.TryGetGlyphTypeface(out var gt) && gt.FamilyNames.Values.Any(n => n.Contains(FontStack.NumbersFamily));
        double[] tabular = Widths(TypeRamp.Hero, tabular: true);
        double[] proportional = Widths(TypeRamp.Hero, tabular: false);
        double spread = tabular.Max() - tabular.Min();
        double natural = proportional.Max() - proportional.Min();
        Check(lines, usesInter && spread < 0.01 && natural > 1,
            $"chiffres tabulaires mesurés par WPF à {TypeRamp.Hero} DIP : écart 0–9 = {spread:0.###} DIP (sans tnum : {natural:0.#} DIP), police utilisée = {(usesInter ? FontStack.NumbersFamily : "repli")}");

        // 3. Deux secondes différentes de l'horloge / du minuteur ont exactement la même largeur (rien ne « danse »).
        double a = Width("10:11:11", TypeRamp.Hero), b = Width("18:48:58", TypeRamp.Hero);
        double c = Width("11:11", TypeRamp.Headline), d = Width("20:08", TypeRamp.Headline);
        Check(lines, Math.Abs(a - b) < 0.01 && Math.Abs(c - d) < 0.01,
            $"« 10:11:11 » = « 18:48:58 » ({a:0.##} / {b:0.##} DIP), « 11:11 » = « 20:08 » ({c:0.##} / {d:0.##} DIP)");

        // 4. Licence OFL copiée avec l'exe (dans Assets\Fonts, comme celle de Lucide).
        string fonts = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"); // copiées comme LICENSE-lucide.txt
        string license = Path.Combine(fonts, "LICENSE-inter.txt");
        bool ofl = File.Exists(license) && File.ReadAllText(license).Contains("SIL Open Font License, Version 1.1");
        Check(lines, ofl && File.Exists(Path.Combine(fonts, "LICENSE-lucide.txt")),
            "licences des polices copiées avec l'exe (Assets\\Fonts) : LICENSE-inter.txt (OFL 1.1) et LICENSE-lucide.txt");

        // 5. Style partagé « icône active » : défini, appliqué d'office à IconChip, pastille + dégradé d'accent quand actif.
        var style = Application.Current?.TryFindResource("ActiveIcon") as Style;
        var chip = new IconChip { Glyph = Icons.DarkMode, IsActive = true };
        var host = new Grid();
        host.Children.Add(chip);
        bool applied = chip.ApplyTemplate();
        host.Measure(new Size(100, 100));
        host.Arrange(new Rect(0, 0, 100, 100));
        var halo = chip.Template?.FindName("Halo", chip) as FrameworkElement;
        var glyph = chip.Template?.FindName("Glyph", chip) as TextBlock;
        bool accent = glyph?.Foreground == Application.Current?.TryFindResource(AppearanceService.AccentGradientKey);
        double haloOn = halo?.Opacity ?? 0;
        chip.IsActive = false;
        double haloOff = halo?.Opacity ?? 1;
        Check(lines, style != null && applied && Math.Abs(haloOn - Interaction.ActiveTint) < 1e-6 && accent && haloOff == 0,
            $"style « ActiveIcon » : défini, pastille d'accent {haloOn:0.##} quand actif / {haloOff:0.##} au repos, glyphe en dégradé d'accent = {accent}");
    }

    private static double[] Widths(double size, bool tabular) =>
        "0123456789".Select(ch => Width(ch.ToString(), size, tabular)).ToArray();

    private static double Width(string text, double size, bool tabular = true)
    {
        var block = new TextBlock { Text = text };
        Numerals.Apply(block, size);
        if (!tabular) System.Windows.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Proportional);
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }
}
