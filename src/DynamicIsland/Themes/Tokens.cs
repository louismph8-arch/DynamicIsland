using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DynamicIsland.Themes;

// =======================================================================================================
// Système de design de l'app (étape 5A) : TOUTES les valeurs de style sont ici, et seulement ici.
//   XAML : {x:Static t:Paint.Surface}, {x:Static t:TypeRamp.Body}, {x:Static t:Corners.Card},
//          {ui:Space s4,s2} (marges), {x:Static t:Icons.Play}…
//   C#   : Paint.Surface, TypeRamp.Body, Radius.Card, Space.S4, Springs.Open, Motion.Fast, Icons.Play…
// Les couleurs qui changent en direct (accent, fond de l'île selon l'opacité) sont des ressources
// dynamiques calculées par AppearanceService à partir d'ici. Seule la mise en page propre à un composant
// (largeur d'une colonne de grille…) reste dans le composant.
// =======================================================================================================

/// <summary>Couleurs de base (opaques ou voiles de blanc), d'où sont tirées toutes les brosses.</summary>
public static class Swatch
{
    // Fonds sombres
    public static readonly Color IslandBase = Color.FromRgb(0x0B, 0x0B, 0x0D); // île et dock (alpha = réglage d'opacité)
    public static readonly Color Window = Color.FromRgb(0x16, 0x16, 0x18);     // fenêtre de paramètres sans Mica
    public static readonly Color Raised = Color.FromRgb(0x1C, 0x1C, 0x1E);     // navigation, menus, étiquettes
    public static readonly Color Preview = Color.FromRgb(0x1E, 0x1E, 0x20);    // aperçus des fenêtres
    public static readonly Color Ink = Color.FromRgb(0x11, 0x11, 0x13);        // texte sur fond blanc
    public static readonly Color White = Colors.White;
    public static readonly Color Black = Colors.Black;

    // Couleurs vives (proches de celles d'iOS, sans les copier à l'identique)
    public static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Indigo = Color.FromRgb(0x4F, 0x7B, 0xFF);
    public static readonly Color Violet = Color.FromRgb(0xA0, 0x5C, 0xFF);
    public static readonly Color Purple = Color.FromRgb(0x9B, 0x5C, 0xFF);
    public static readonly Color Pink = Color.FromRgb(0xFF, 0x4F, 0xA3);
    public static readonly Color Magenta = Color.FromRgb(0xFF, 0x2D, 0x95);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x45, 0x3A);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color Teal = Color.FromRgb(0x40, 0xC8, 0xE0);
    public static readonly Color Lilac = Color.FromRgb(0x78, 0x65, 0xFF);       // accent de départ (avant les réglages)
    public static readonly Color Pearl = Color.FromRgb(0xF2, 0xF2, 0xF7);       // accent neutre (pas de pochette)
    public static readonly Color DiscordGreen = Color.FromRgb(0x23, 0xA5, 0x5A); // « en train de parler »
    public static readonly Color Elevated = Color.FromRgb(0x2C, 0x2C, 0x2E);    // pastille au-dessus d'une tuile

    // Rendus --snapshot : dégradé de fond de test
    public static readonly Color SnapshotSky = Color.FromRgb(0x3E, 0x5C, 0x8A);
    public static readonly Color SnapshotSand = Color.FromRgb(0xC9, 0x8B, 0x6B);

    /// <summary>Accent par défaut (dégradé indigo → violet).</summary>
    public static readonly Color AccentStart = Indigo;
    public static readonly Color AccentEnd = Violet;

    /// <summary>Pastilles de couleur d'accent proposées : clé du nom (fichiers de langue), début, fin du dégradé.</summary>
    public static readonly (string Name, Color Start, Color End)[] AccentPresets =
    [
        ("swatch.blue_violet", Indigo, Violet), ("swatch.blue", Blue, Blue), ("swatch.violet", Purple, Purple),
        ("swatch.pink", Pink, Pink), ("swatch.red", Red, Red), ("swatch.orange", Orange, Orange),
        ("swatch.green", Green, Green), ("swatch.teal", Teal, Teal), ("swatch.sunset", Orange, Pink),
    ];

    /// <summary>Voile de blanc (alpha 0–255) : surfaces, survols, séparateurs sur fond sombre.</summary>
    public static Color WhiteA(byte alpha) => Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    public static Color InkA(byte alpha) => Color.FromArgb(alpha, Ink.R, Ink.G, Ink.B);
    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>« #RRGGBB » (réglages, thèmes partagés).</summary>
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>Brosses (gelées) prêtes à l'emploi, nommées par leur rôle.</summary>
public static class Paint
{
    // Texte
    public static readonly Brush White = Frozen(Swatch.White);
    public static readonly Brush TextPrimary = Frozen(Swatch.White);
    public static readonly Brush TextSecondary = Frozen(Swatch.WhiteA(0xA8));
    public static readonly Brush TextTertiary = Frozen(Swatch.WhiteA(0x66));
    public static readonly Brush TextFaint = Frozen(Swatch.WhiteA(0x38));
    public static readonly Brush TextMuted = Frozen(Swatch.WhiteA(0xB3));        // pourcentages sur fond sombre
    public static readonly Brush Ink = Frozen(Swatch.Ink);                       // texte sur fond blanc
    public static readonly Brush InkMuted = Frozen(Swatch.InkA(0xB3));

    // Surfaces (voiles de blanc sur fond sombre)
    public static readonly Brush CardBorder = Frozen(Swatch.WhiteA(0x0F));
    public static readonly Brush HoverSubtle = Frozen(Swatch.WhiteA(0x12));
    public static readonly Brush Card = Frozen(Swatch.WhiteA(0x14));
    public static readonly Brush Surface = Frozen(Swatch.WhiteA(0x17));
    public static readonly Brush Divider = Frozen(Swatch.WhiteA(0x1A));
    public static readonly Brush Raised = Frozen(Swatch.WhiteA(0x1C));
    public static readonly Brush Control = Frozen(Swatch.WhiteA(0x1F));
    public static readonly Brush SurfaceHover = Frozen(Swatch.WhiteA(0x26));
    public static readonly Brush Selected = Frozen(Swatch.WhiteA(0x26));
    public static readonly Brush SelectedStrong = Frozen(Swatch.WhiteA(0x2E));
    public static readonly Brush Track = Frozen(Swatch.WhiteA(0x33));
    public static readonly Brush ScrollThumb = Frozen(Swatch.WhiteA(0x38));
    public static readonly Brush ToggleOff = Frozen(Swatch.WhiteA(0x40));
    public static readonly Brush ScrollThumbHover = Frozen(Swatch.WhiteA(0x66));
    public static readonly Brush Inverse = Frozen(Swatch.WhiteA(0xF2));          // tuile active, curseur rempli
    public static readonly Brush PopupBackground = Frozen(Swatch.WithAlpha(Swatch.Raised, 0xF5));
    public static readonly Brush LabelBackground = Frozen(Swatch.WithAlpha(Swatch.Raised, 0xF2));
    public static readonly Brush PopupBorder = Frozen(Swatch.WhiteA(0x26));
    public static readonly Brush ThumbStroke = Frozen(Color.FromArgb(0x33, 0, 0, 0));
    public static readonly Brush Indicator = Frozen(Swatch.WhiteA(0xE6));     // point « appli ouverte »
    public static readonly Brush FolderTile = Frozen(Swatch.WhiteA(0x3D));    // fond d'une icône de dossier
    public static readonly Brush ActiveBorder = Frozen(Swatch.WhiteA(0x59));  // aperçu de la fenêtre active
    public static readonly Brush Badge = Frozen(Swatch.WithAlpha(Swatch.Elevated, 0xE6)); // « × » d'une tuile
    /// <summary>Presque transparent (alpha 1) : capte la souris dans une fenêtre « layered » sans se voir.</summary>
    public static readonly Brush HitTarget = Frozen(Color.FromArgb(1, 0, 0, 0));
    public static readonly Brush Window = Frozen(Swatch.Window);
    public static readonly Brush Preview = Frozen(Swatch.Preview);
    public static readonly Brush TextSelection = Frozen(Swatch.Blue);
    public static readonly Brush Sidebar = Frozen(Swatch.Raised);
    public static readonly Brush SidebarOverMica = Frozen(Swatch.WithAlpha(Swatch.Raised, 0x55));

    // Couleurs de sens
    public static readonly Brush Timer = Frozen(Swatch.Orange);
    public static readonly Brush TimerSoft = Frozen(Swatch.WithAlpha(Swatch.Orange, 0x2E));
    public static readonly Brush Today = Frozen(Swatch.Red);
    public static readonly Brush Break = Frozen(Swatch.Green);
    public static readonly Brush Camera = Frozen(Swatch.Green);
    public static readonly Brush Microphone = Frozen(Swatch.Orange);
    public static readonly Brush Danger = Frozen(Swatch.Red);
    public static readonly Brush Speaking = Frozen(Swatch.DiscordGreen);

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Brush Gradient(Color start, Color end, double angle = 0)
    {
        var brush = new LinearGradientBrush(start, end, angle);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Rayons des coins (en DIP). Tous les coins sont dessinés en « squircle » (voir Controls/SquircleBorder).</summary>
public static class Radius
{
    /// <summary>Lissage du squircle : 0 = arc de cercle classique, 1 = courbure la plus continue.</summary>
    public const double Smoothing = 0.6;

    public const double None = 0;
    public const double Marker = 1.5;   // repère de la navigation
    public const double XS = 6;         // élément de menu
    public const double S = 8;          // champ de saisie, vignette, pochette réduite
    public const double M = 10;         // ligne de liste, bouton de navigation, menu
    public const double L = 12;         // carte de réglage, carte de choix
    public const double XL = 14;        // tuile (étagère, dossier, profil)
    public const double XXL = 18;       // pochette, grande vignette
    public const double Tile = 20;      // tuile du centre de contrôle
    public const double Window = 22;    // fenêtres flottantes (recherche, palette)
    public const double Pill = 999;     // pilule : le rayon est ramené à la moitié de la hauteur

    // Formes réglables (réglage « Arrondi des coins » : 0 % = carré, 100 % = arrondi)
    public static readonly RadiusRange IslandCompact = new(6, 18);
    public static readonly RadiusRange IslandExpanded = new(8, 30);
    public static readonly RadiusRange DockFloating = new(6, 22);
    public static readonly RadiusRange Palette = new(10, 22);
    public static readonly RadiusRange Folder = new(10, 24);
}

/// <summary>Rayon entre « carré » et « arrondi » selon le réglage d'arrondi (0 → 1).</summary>
public readonly record struct RadiusRange(double Square, double Round)
{
    public double At(double roundness) => Square + (Round - Square) * roundness;
}

/// <summary>Les mêmes rayons, en <see cref="CornerRadius"/> pour le XAML.</summary>
public static class Corners
{
    public static readonly CornerRadius None = new(Radius.None);
    public static readonly CornerRadius Marker = new(Radius.Marker);
    public static readonly CornerRadius XS = new(Radius.XS);
    public static readonly CornerRadius S = new(Radius.S);
    public static readonly CornerRadius M = new(Radius.M);
    public static readonly CornerRadius L = new(Radius.L);
    public static readonly CornerRadius XL = new(Radius.XL);
    public static readonly CornerRadius XXL = new(Radius.XXL);
    public static readonly CornerRadius Tile = new(Radius.Tile);
    public static readonly CornerRadius Window = new(Radius.Window);
    public static readonly CornerRadius Pill = new(Radius.Pill);
}

/// <summary>Épaisseurs de trait.</summary>
public static class Stroke
{
    public const double Hairline = 1;
    public const double Selected = 1.5;
    public const double Focus = 2;
}

/// <summary>Les mêmes épaisseurs, en <see cref="Thickness"/> pour les bordures en XAML.</summary>
public static class Borders
{
    public static readonly Thickness None = new(0);
    public static readonly Thickness Hairline = new(Stroke.Hairline);
    public static readonly Thickness Selected = new(Stroke.Selected);
    public static readonly Thickness Focus = new(Stroke.Focus);
    /// <summary>Liseré du haut seulement (barre du dock).</summary>
    public static readonly Thickness TopHairline = new(0, Stroke.Hairline, 0, 0);
    /// <summary>Liseré du bas seulement (barre du dock en haut de l'écran).</summary>
    public static readonly Thickness BottomHairline = new(0, 0, 0, Stroke.Hairline);
    /// <summary>Liseré de droite seulement (colonne de navigation des paramètres).</summary>
    public static readonly Thickness RightHairline = new(0, 0, Stroke.Hairline, 0);
}

/// <summary>Ombres portées et halo « néon ».</summary>
public static class Shadows
{
    /// <summary>Lumière venant du haut : l'ombre tombe vers le bas.</summary>
    public const double Direction = 270;

    public static readonly ShadowSpec Island = new(22, 3, 0.55);
    public static readonly ShadowSpec Dock = new(24, 4, 0.5);
    public static readonly ShadowSpec Window = new(28, 6, 0.55);
    public static readonly ShadowSpec Menu = new(16, 3, 0.45);

    // Halo néon (AppearanceService.ApplyGlow) : de l'ombre noire (0 %) à la lueur d'accent (100 %)
    public const double GlowBlurExtra = 16;     // flou ajouté à 100 %
    public const double GlowOpacityBase = 0.55;
    public const double GlowOpacityRange = 0.4;
    public const double GlowColorBoost = 1.6;   // la couleur atteint l'accent avant 100 %
    public const double GlowDepthMax = 3;       // décalage vers le bas à 0 %
    public const double GlowMinWithCover = 0.45; // halo minimal quand il prend la couleur de la pochette
    public const double WindowMargin = 20;       // marge autour d'une fenêtre flottante pour son ombre
    public const byte RimAlphaRest = 0x1F;      // liseré au repos
    public const byte RimAlphaGlow = 0xB0;      // liseré à 100 % de halo
}

/// <summary>Ombre : flou, décalage vers le bas, opacité.</summary>
public sealed record ShadowSpec(double Blur, double Depth, double Opacity, double Direction = Shadows.Direction)
{
    public DropShadowEffect Create() => new()
    {
        BlurRadius = Blur,
        ShadowDepth = Depth,
        Opacity = Opacity,
        Direction = Direction,
        Color = Swatch.Black,
        RenderingBias = RenderingBias.Performance,
    };
}

/// <summary>Ombres partagées (gelées : un même effet sert à plusieurs fenêtres).</summary>
public static class Effects
{
    public static readonly DropShadowEffect Window = Freeze(Shadows.Window.Create());
    public static readonly DropShadowEffect Menu = Freeze(Shadows.Menu.Create());

    private static DropShadowEffect Freeze(DropShadowEffect effect)
    {
        effect.Freeze();
        return effect;
    }
}

/// <summary>États interactifs : voile au survol / à l'appui, opacité désactivée, légère réduction à l'appui.</summary>
public static class Interaction
{
    public const double HoverOverlay = 0.12;
    public const double PressedOverlay = 0.2;
    public const double SubtleHoverOverlay = 0.08;
    public const double DisabledOpacity = 0.3;
    public const double DimmedOpacity = 0.45;
    public const double MutedOpacity = 0.55;              // personne qui a coupé son son (Discord)
    public const double SecondaryTextOpacity = 0.65; // texte secondaire sur une tuile qui change de couleur
    public const double PressedScale = 0.92;
    public const double SoftPressedScale = 0.96;
    public const double CollapsedContentScale = 0.92; // contenu de l'île quand elle se referme
}

/// <summary>
/// Couleurs tirées du fond d'écran (5C) : histogramme des teintes vives, deux couleurs pour le dégradé
/// d'accent, une teinte très sombre mêlée au fond de l'île et du dock.
/// </summary>
public static class AutoColor
{
    public const int SampleWidth = 96;          // l'image est réduite à cette largeur avant l'analyse
    public const int HueBins = 36;              // cases de 10°
    public const double MinSaturation = 0.15;   // pixels plus gris : ignorés
    public const double MinValue = 0.15;        // pixels plus sombres : ignorés
    public const double MinColorfulShare = 0.04; // moins de 4 % de pixels colorés : fond gris, pas de couleur
    public const double AccentMinSaturation = 0.5;
    public const double AccentMaxSaturation = 0.9;
    public const double AccentMinValue = 0.85;  // l'accent reste lumineux sur l'île sombre
    public const double SecondHueGap = 40;      // écart minimal (degrés) entre les deux couleurs du dégradé
    public const double SecondMinShare = 0.2;   // la 2e couleur doit peser au moins 20 % de la 1re…
    public const double AnalogousShift = 28;    // … sinon : teinte voisine de la 1re
    public const double SurfaceSaturation = 0.5;
    public const double SurfaceValue = 0.14;
    public const double SurfaceMix = 0.55;      // part de la teinte dans le fond de l'île et du dock
    public const byte WindowsColorizationAlpha = 0xC4; // DWM « ColorizationColor » (valeur de Windows)
}

/// <summary>Ambiance de couleurs d'un fond d'écran généré (du plus sombre au plus lumineux).</summary>
public readonly record struct Mood(Color Deep, Color Base, Color Accent1, Color Accent2, Color Glow)
{
    public static Mood Lerp(Mood a, Mood b, double t) => new(
        Mix(a.Deep, b.Deep, t), Mix(a.Base, b.Base, t), Mix(a.Accent1, b.Accent1, t), Mix(a.Accent2, b.Accent2, t), Mix(a.Glow, b.Glow, t));

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

/// <summary>
/// Fonds d'écran générés (5D) : ambiances du jour et réglages du fond dynamique et du fond animé.
/// La nuit est l'ambiance du pack (noir / bleu / violet).
/// </summary>
public static class Wallpapers
{
    public static readonly Mood Night = new(Hex("#04040A"), Hex("#0C1236"), Hex("#3A5BFF"), Hex("#8A3DFF"), Hex("#C9B6FF"));
    public static readonly Mood Morning = new(Hex("#0E1030"), Hex("#2B3275"), Hex("#6C8CFF"), Hex("#FF8FB8"), Hex("#FFD6C2"));
    public static readonly Mood Day = new(Hex("#081A44"), Hex("#1A4BB5"), Hex("#3FA9FF"), Hex("#9B6BFF"), Hex("#E6F4FF"));
    public static readonly Mood Sunset = new(Hex("#13081F"), Hex("#3B1460"), Hex("#B63CFF"), Hex("#FF7A45"), Hex("#FFC48A"));

    /// <summary>Fond dynamique : moments clés autour du lever et du coucher du soleil (minutes), interpolés entre eux.</summary>
    public const double DawnStart = -60, Sunrise = 0, MorningEnd = 120, AfternoonEnd = -120, Dusk = -30, NightStart = 60;
    /// <summary>Fond dynamique : nouvelle image au plus toutes les 10 minutes, et seulement si les couleurs ont bougé.</summary>
    public static readonly TimeSpan DynamicStep = TimeSpan.FromMinutes(10);
    public const int MinColorChange = 3;        // écart minimal (0-255) sur une couleur pour refaire l'image

    /// <summary>Fond animé : images par seconde et vitesse du mouvement (1 = lent et calme).</summary>
    public const int AnimationFrameRate = 30;
    /// <summary>Un peu moins d'1/30 s : la minuterie de Windows arrondit par pas de 15,6 ms (33 ms donnerait 21 images/s).</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / AnimationFrameRate * 0.94);
    public const double AnimationSpeed = 1;
    public static readonly TimeSpan PauseCheck = TimeSpan.FromSeconds(1);
    /// <summary>Fond animé en pause après ce temps sans clavier ni souris (personne ne le regarde, 7A).</summary>
    public static readonly TimeSpan IdlePause = TimeSpan.FromMinutes(5);

    public const int ThumbnailWidth = 320, ThumbnailHeight = 180;

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>Icônes harmonisées du dock (5G), dessinées à <see cref="Size"/> px puis réduites par le dock.</summary>
public static class DockIconArt
{
    public const int Size = 128;
    public const double TileInset = 6;             // marge autour du cadre (le grossissement ne le coupe pas)
    public const double TileRadius = 0.225;        // rayon du cadre = 22,5 % de son côté
    public const double GlyphScale = 0.62;         // place de l'icône dans le cadre
    public static readonly Color TileTop = Color.FromRgb(0x2E, 0x2E, 0x36);
    public static readonly Color TileBottom = Color.FromRgb(0x17, 0x17, 0x1C);
    public const double TintBase = 0.62;           // icône teintée : part de l'accent dans ses zones sombres
    public const double TintHighlight = 0.75;      // au-delà de cette luminosité, reflets vers le blanc
    public const int CustomIconSize = 256;         // image perso : réduite à cette taille et gardée en PNG
}

/// <summary>
/// Logo d'Opaline (5H) : un anneau d'opale (verre laiteux aux reflets changeants) sur une tuile squircle sombre.
/// Mesures en fraction du côté de l'image.
/// </summary>
public static class LogoArt
{
    /// <summary>Reflets de l'opale le long de l'anneau, en partant du haut dans le sens des aiguilles d'une montre.</summary>
    public static readonly Color[] Opal =
    [
        Color.FromRgb(0x7F, 0xA8, 0xFF), // bleu
        Color.FromRgb(0xA9, 0x84, 0xFF), // lavande
        Color.FromRgb(0xF0, 0x8C, 0xD6), // rose
        Color.FromRgb(0xFF, 0xC4, 0x9A), // pêche
        Color.FromRgb(0x8E, 0xE8, 0xD8), // menthe
    ];
    public static readonly Color TileTop = Color.FromRgb(0x2A, 0x26, 0x36);
    public static readonly Color TileBottom = Color.FromRgb(0x10, 0x0F, 0x16);
    public const double TileMargin = 0.035;        // marge autour de la tuile (icône Windows)
    public const double TileExponent = 5;          // superellipse : forme squircle
    public const double RingOuter = 0.335;
    public const double RingInner = 0.195;
    public const double Milk = 0.18;               // part de blanc laiteux dans les reflets
    public const double LightAngle = 0.875;        // reflet vers le haut à gauche (fraction de tour depuis le haut)
    public const double GlowReach = 0.045;         // halo autour de l'anneau
    public const double GlowStrength = 0.42;
    public const int Supersampling = 4;            // 4 × 4 échantillons par pixel
    public static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
    // Images de l'installeur (tailles d'Inno Setup à 100 %)
    public const int WizardWidth = 164;
    public const int WizardHeight = 314;
    public const int WizardSmall = 55;
}

/// <summary>Assistant de bienvenue (5H, premier lancement).</summary>
public static class WelcomeShape
{
    public const double Width = 760;
    public const double Height = 600;
    public const double Margin = 28;               // autour de la carte (halo)
    public const int Logo = 112;                   // logo de la première page
    public const int SmallLogo = 56;               // logo de la dernière page
    public const double Dot = 7;                   // points de progression
    public const double ActiveDot = 22;
    public const double Slide = 44;                // décalage des pages qui entrent / sortent
    public const double Thumbnail = 84;            // vignette d'un fond d'écran (largeur ; 16:9) : les 6 sur une ligne
    public const double PreviewWidth = 220;        // aperçu de l'île (page Style)
    public const double PreviewHeight = 40;
    public const double TipIcon = 34;              // pastille d'icône des astuces
    public const double NextWidth = 150;           // bouton « Suivant »
}

/// <summary>Curseur de souris Dynamic Island (5G) : formes dessinées sur une grille de 32 unités.</summary>
public static class CursorArt
{
    public static readonly int[] Sizes = [32, 48, 64, 96, 128]; // Windows prend la taille adaptée à l'écran
    public const double Outline = 1.7;             // liseré blanc
    public const double ShadowBlur = 3;
    public const double ShadowDepth = 1.4;
    public const double ShadowOpacity = 0.45;
    public const int PreviewSize = 48;             // aperçu dans les paramètres
}

/// <summary>Barre de menus façon macOS (5F), en haut de l'écran principal.</summary>
public static class MenuBarShape
{
    public const double Height = 28;
    public const double SidePadding = 10;           // marge à gauche et à droite de la barre
    public const double ItemHeight = 22;            // zone cliquable d'un élément (pilule au survol)
    public const double ItemPadding = 8;            // marge intérieure d'un élément
    public const double AppIcon = 16;
    public const double TitleMaxWidth = 460;        // titre de la fenêtre active
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2); // heure, volume, batterie
    public const double VolumeWheelStep = 0.02;     // molette sur l'icône du volume
}

/// <summary>Widgets du bureau (5E) : tailles de base (échelle 1), grille, estompage.</summary>
public static class Desk
{
    public const double Grid = 16;                 // aimantation à la grille (DIP)
    public const double Margin = 48;               // marge au bord de l'écran pour la place par défaut
    public const double FadedOpacity = 0.4;        // une appli est au premier plan : widgets estompés
    public const double MinScale = 0.6, MaxScale = 2.2;
    public const double CardPadding = 18;
    public const byte CardAlpha = 0xA8;            // fond des cartes (teinte de l'île)
    public static readonly Size Clock = new(460, 190);
    public static readonly Size Weather = new(220, 220);
    public static readonly Size Music = new(380, 136);
    public static readonly Size Calendar = new(248, 232);
    public static readonly Size Stats = new(268, 196);
    public const double ClockDay = 78;             // « SAMEDI »
    public const double ClockDayTracking = 7;      // espace entre les lettres du jour
    public const double ClockLine = 21;            // « 24 OCTOBRE · 22:45 »
    public const double ClockLineTracking = 3;
    public const double WeatherTemperature = 54;
    public const double MusicCover = 100;
    public const double CalendarCell = 28;
    public static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(350);
}

/// <summary>
/// Verre liquide (Themes/Shaders/LiquidGlass.fx). Les intensités sont celles du réglage à 100 % ; le réglage
/// « Intensité du verre » les multiplie. Longueurs en DIP.
/// </summary>
public static class Glass
{
    public const double Band = 12;            // largeur de la bande du bord (réfraction + liseré)
    public const double Refraction = 5;       // décalage maximal du contenu sur le bord
    public const double Aberration = 0.35;    // écart entre rouge et bleu dans la réfraction (0 à 1)
    public const double Rim = 0.6;            // liseré lumineux (blanc ajouté) côté lumière
    public const double RimAtRest = 0.6;      // part du liseré quand le curseur est loin
    public const double Sheen = 0.18;         // reflet sous le curseur
    public const double SheenRadius = 80;
    public const double TopLight = 0.05;      // lumière du haut qui traverse l'épaisseur
    public const double DefaultIntensity = 0.8;

    /// <summary>Distance (DIP) à partir de laquelle la lumière suit le curseur.</summary>
    public const double LightRange = 220;
    /// <summary>Lumière au repos : au-dessus du centre de la forme (fraction de la largeur, hauteurs au-dessus).</summary>
    public const double RestX = 0.5;
    public const double RestHeights = 1.5;
    /// <summary>Lissage du suivi (part du chemin parcourue à chaque image).</summary>
    public const double Follow = 0.35;
    /// <summary>Lecture du curseur : rapide quand il est proche ou que la lumière bouge, sinon lente.</summary>
    public static readonly TimeSpan ActiveTick = TimeSpan.FromMilliseconds(33);
    public static readonly TimeSpan IdleTick = TimeSpan.FromMilliseconds(150);
}

/// <summary>
/// Espacements : pas de 2 px (S1 = 2, S2 = 4… S18 = 36) + trait d'un pixel (Px). En XAML : {ui:Space …}
/// avec les mêmes noms en minuscules (s4, px, 0), « -s3 » pour un négatif.
/// </summary>
public static class Space
{
    public const double Unit = 2;
    public const double Px = 1;
    public const double S1 = 1 * Unit;
    public const double S2 = 2 * Unit;
    public const double S3 = 3 * Unit;
    public const double S4 = 4 * Unit;
    public const double S5 = 5 * Unit;
    public const double S6 = 6 * Unit;
    public const double S7 = 7 * Unit;
    public const double S8 = 8 * Unit;
    public const double S9 = 9 * Unit;
    public const double S10 = 10 * Unit;
    public const double S12 = 12 * Unit;
    public const double S14 = 14 * Unit;
    public const double S16 = 16 * Unit;
    public const double S18 = 18 * Unit;

    /// <summary>« s4 » → 8, « px » → 1, « 0 » → 0, « -s3 » → -6.</summary>
    public static double Parse(string token)
    {
        token = token.Trim().ToLowerInvariant();
        bool negative = token.StartsWith('-');
        if (negative) token = token[1..];
        double value = token switch
        {
            "0" => 0,
            "px" => Px,
            _ when token.StartsWith('s') && int.TryParse(token[1..], out int steps) => steps * Unit,
            _ => throw new FormatException($"Espacement inconnu : « {token} » (attendu : 0, px, s1…s18)"),
        };
        return negative ? -value : value;
    }
}

/// <summary>Polices. Interface : Segoe UI Variable (Windows 11), repli Segoe UI (Windows 10). Icônes : Lucide (licence ISC).</summary>
public static class FontStack
{
    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Icons = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#lucide");
}

/// <summary>Échelle typographique (tailles en DIP), du plus grand au plus petit.</summary>
public static class TypeRamp
{
    public const double Hero = 38;        // chiffres du minuteur
    public const double Display = 34;
    public const double LargeTitle = 26;  // titre d'une page de paramètres
    public const double Title1 = 22;
    public const double Title2 = 18;      // champ de recherche
    public const double Title3 = 16;      // titre d'un morceau
    public const double Headline = 14;    // titre d'une carte, heure de l'île
    public const double Body = 13;
    public const double Callout = 12.5;   // libellés d'onglets, artiste
    public const double Footnote = 12;    // descriptions
    public const double Caption = 11.5;   // légendes, petits boutons
    public const double Caption2 = 10.5;  // étiquettes en capitales, durées
    public const double Micro = 8;
}

/// <summary>Tailles des icônes (police Lucide).</summary>
public static class IconSize
{
    public const double XS = 9;
    public const double S = 11;
    public const double M = 13;
    public const double L = 15;
    public const double XL = 17;
    public const double XXL = 24;
    public const double Huge = 34;
}

/// <summary>Tailles des contrôles.</summary>
public static class Sizes
{
    public const double IconButton = 36;
    public const double SmallIconButton = 26;
    public const double TinyButton = 20;     // « × » des tuiles
    public const double PlayButton = 44;
    public const double Chip = 24;
    public const double SoftButton = 34;
    public const double Tab = 30;
    public const double NavItem = 38;
    public const double Button = 32;         // boutons et champs des paramètres
    public const double SwitchWidth = 42;
    public const double SwitchHeight = 24;
    public const double SwitchKnob = 20;
    public const double SliderThumb = 18;
    public const double LineKnob = 10;       // pastille du curseur fin (volume de l'appli musique)
    public const double Cover = 112;         // pochette de l'onglet Musique
    public const double Bar = 4;             // pistes de curseur et de progression
    public const double ScrollBar = 12;
    public const double ScrollThumb = 6;
    public const double FillSlider = 56;
    public const double GlyphTile = 30;      // vignette d'icône dans une liste
    public const double AppIcon = 32;        // icône d'appli dans un aperçu de fenêtre
    public const double Swatch = 40;         // pastille de couleur d'accent (Apparence)
    public const double SwatchDot = 28;
    public const double Choice = 38;         // bouton de choix (icône ou couleur d'un profil)
    public const double ChoiceDot = 24;
    public const double ProfileTileWidth = 116;
    public const double ProfileTileHeight = 104;
    public const double ProfileChip = 30;    // pastille de profil de l'onglet Contrôles
    public const double CalendarDayWidth = 26;
    public const double CalendarDayHeight = 22;
    public const double ShelfTile = 78;
    public const double ShelfThumb = 48;
    public const double Avatar = 30;         // avatar Discord de l'onglet Vocal
    public const double CompactAvatar = 20;  // … dans l'île réduite
    public const double VoiceRow = 200;
    public const double ProfileDot = 36;     // pastille de profil
    public const double PaletteWidth = 640;  // palette de commandes ouverte
}

/// <summary>Géométrie de l'île (en DIP).</summary>
public static class IslandShape
{
    public static readonly Size Compact = new(124, 36);
    public static readonly Size CompactWide = new(220, 36);
    public static readonly Size Hud = new(250, 36);
    public static readonly Size Gaming = new(64, 6);
    public const double IntroDot = 8;            // intro : le point lumineux d'où l'île se déploie
    public static readonly Size Music = new(440, 218);   // + curseur du volume de l'appli
    public static readonly Size Calendar = new(488, 262);
    public static readonly Size Control = new(440, 238);
    public static readonly Size Panel = new(440, 196);   // widgets, étagère, vocal
    public static readonly Size Claude = new(480, 262);  // mini Claude : la conversation défile
    public static readonly Size Health = new(488, 272);  // santé du PC : mesures, disques, 3 applis
    public const double WideTabs = 512;          // l'île peut s'élargir jusque-là si la rangée d'onglets est longue (fenêtre de 560 moins le halo)
    public const double HealthContentWidth = 454;
    public const double MinExpandedInnerWidth = 438;
    public const double ContentWidth = 406;
    public const double MusicContentHeight = 150;
    public const double TopMargin = 8;           // entre le bord de l'écran et la pilule (place par défaut)
    public const double NoticeMinWidth = 180;
    public const double NoticeMaxWidth = 420;
    public const double NoticeTallHeight = 52;   // annonce sur deux lignes (appareil connecté)
    public const double ClockCardWidth = 94;     // horloges du monde : une carte par ville (4 au plus)
    public const double ClockCardHeight = 128;
    // Placement libre (5I)
    public static readonly double MaxExpandedWidth = Math.Max(WideTabs, new[] { Music, Calendar, Control, Panel, Claude, Health }.Max(s => s.Width));
    public const double ClaudeBubble = 340;      // largeur max d'une question dans la conversation
    public const double ClaudeContentWidth = 444; // contenu de l'onglet Claude (île de 480 moins ses marges)
    public const double ClaudeContentHeight = 192;
    public const double EdgeMargin = 24;         // place du halo quand l'île part du bord gauche / droit de sa fenêtre
    public const double SnapDistance = 16;       // aimantation au bord de l'écran / au centre
}

/// <summary>Géométrie du dock pour des icônes de <see cref="ReferenceIcon"/> px : tout suit la taille choisie.</summary>
public static class DockShape
{
    public const double ReferenceIcon = 48;
    public const double GapRatio = 1 / 12.0;        // marge de chaque côté d'une icône
    public const double DotRatio = 0.25;            // zone du point sous l'icône (dock flottant)
    public const double BarDotRatio = 0.2;          // … en mode barre
    public const double Dot = 4;                    // point « appli ouverte »
    public const double TopInset = 8;               // au-dessus des icônes (dock flottant)
    public const double BarTopInset = 5;            // … en mode barre
    public const double FloatingGap = 8;            // sous le dock flottant
    public const double ReservedExtra = 4;          // espace réservé en plus en mode Fixe
    public const double MinWindowHeight = 170;      // fenêtre : place pour le grossissement + le nom
    public const double WindowIcons = 2.6;
    public const double WindowExtra = 45;
    public const double HiddenExtra = 50;           // décalage en plus pour cacher aussi le halo
    public const double MagnificationRange = 2.6;   // portée du grossissement, en largeurs d'icône
    public const double MergeZone = 0.28;           // zone de dépôt (dossier), en largeur d'icône
    public const double HighlightRadius = 0.28;     // arrondi du cadre de dépôt, en fraction de sa taille
    public const double HighlightPadding = 1.4;     // marge du cadre de dépôt, en marges d'icône
    public const double Separator = 40;
    public const double SearchWidth = 128;
    public const double SearchHeight = 40;
    public const double SearchMinHeight = 30;
    public const double Profile = 34;
    public const double ProfileMin = 28;
    public const double ProfileGlyph = 15;
    public const double ProfileGlyphMin = 12;
    public const double LabelOffset = 10;           // étiquette du nom au-dessus de l'icône
    public const double TrayCell = 22;
    public const double TrayIcon = 16;
    public const double FolderTileWidth = 96;       // case de la grille d'un dossier ouvert
    public const double FolderTileIcon = 52;
    public const double FolderMinWidth = 220;
    public const double FolderAnchorGap = 14;       // entre la grille et l'icône du dossier
    public const int FolderColumns = 4;
    public const double SnapDistance = 24;          // dock flottant déplacé : aimanté au centre du bord
    public const double StatusCell = 36;            // case de la zone d'état (6B), pour des icônes de 48
    public const double StatusCellMin = 28;
    public const double StatusGlyph = 18;           // icône Lucide d'une case
    public const double StatusText = 12.5;          // « FR » (langue du clavier)
    public const double StatusBadge = 15;           // pastille du nombre (mises à jour)
    public const double StatusDot = 6;              // point « nouveau » / « pleine »
    public const double PoofDistance = 26;          // effet « poof » de la corbeille : distance des bulles
    public const int DownloadsShown = 12;           // pile Téléchargements : éléments affichés
    public const double MovingOpacity = 0.75;       // dock pendant qu'on le déplace
}

/// <summary>Ressorts (raideur, amortissement &lt; 1 = léger rebond). Voir Animation/Spring.</summary>
public static class Springs
{
    public static readonly SpringSpec Open = new(230, 0.74);    // ouverture de l'île, de la palette
    public static readonly SpringSpec Close = new(320, 0.9);    // retour à la pilule
    public static readonly SpringSpec Resize = new(260, 0.78);  // changement d'onglet
    public static readonly SpringSpec Settle = new(260, 0.8);   // la pilule change de taille
    public static readonly SpringSpec Snap = new(300, 0.85);    // réglage appliqué
    public static readonly SpringSpec Pop = new(300, 0.75);     // annonce
    public static readonly SpringSpec Hud = new(320, 0.8);      // volume
    public static readonly SpringSpec Reflow = new(320, 0.85);  // la palette change de hauteur
    public static readonly SpringSpec Magnify = new(520, 0.82); // grossissement du dock
    public static readonly SpringSpec Appear = new(380, 0.7);   // icône qui arrive dans le dock
    public static readonly SpringSpec Vanish = new(300, 1);     // icône qui part
    public static readonly SpringSpec DockHide = new(260, 1);
    public static readonly SpringSpec DockShow = new(320, 0.78);
    public static readonly SpringSpec IntroBubble = new(340, 0.62); // intro : le point devient une bulle
    public static readonly SpringSpec IntroStretch = new(210, 0.66); // intro : la bulle s'étire en pilule
    public static readonly SpringSpec Welcome = new(260, 0.72);     // assistant de bienvenue (fenêtre, logo)
}

public readonly record struct SpringSpec(double Stiffness, double Damping);

/// <summary>Durées des animations (fondus, rebonds…). Les délais de comportement (survol, minuteries) n'en font pas partie.</summary>
public static class Motion
{
    public static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Brisk = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan Short = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan Swift = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Base = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Medium = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Long = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan Scale = TimeSpan.FromMilliseconds(420);  // contenu de l'île qui grandit
    public static readonly TimeSpan Bounce = TimeSpan.FromMilliseconds(600); // rebond d'une icône au lancement
    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(650);  // alerte de fin du minuteur
    public static readonly TimeSpan Genie = TimeSpan.FromMilliseconds(500);  // réduction façon macOS
    public static readonly TimeSpan IntroStretch = TimeSpan.FromMilliseconds(170); // intro : la bulle attend avant de s'étirer
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(320);   // assistant : changement de page
    public static readonly TimeSpan DelayShort = TimeSpan.FromMilliseconds(40);
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(60);
    public static readonly TimeSpan DelayLong = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan DelayLonger = TimeSpan.FromMilliseconds(120);
    /// <summary>Images par seconde des animations décoratives en boucle (égaliseur).</summary>
    public const int DecorativeFrameRate = 30;
    /// <summary>Hauteur du rebond d'une icône du dock.</summary>
    public const double BounceHeight = 18;
}

/// <summary>Icônes de l'interface (police Lucide, licence ISC — Assets/Fonts/LICENSE-lucide.txt).</summary>
public static class Icons
{
    public const string Music = "\uE122"; // music
    public const string Calendar = "\uE063"; // calendar
    public const string Controls = "\uE29A"; // sliders-horizontal
    public const string Widgets = "\uE1C1"; // layout-dashboard
    public const string Shelf = "\uE0F7"; // inbox
    public const string Voice = "\uE118"; // mic
    public const string Island = "\uE435"; // panel-top
    public const string Dock = "\uE42C"; // panel-bottom
    public const string Appearance = "\uE1DD"; // palette
    public const string Settings = "\uE154"; // settings
    public const string Profiles = "\uE19F"; // user
    public const string Share = "\uE156"; // share-2
    public const string Info = "\uE0F9"; // info
    public const string Sparkles = "\uE412"; // sparkles (Claude)
    public const string NewChat = "\uE172"; // square-pen
    public const string Stop = "\uE167"; // square
    public const string LogIn = "\uE10D"; // log-in
    public const string Copy = "\uE09E"; // copy
    public const string Alert = "\uE077"; // circle-alert
    public const string Search = "\uE151"; // search
    public const string Plus = "\uE13D"; // plus
    public const string Minus = "\uE11C"; // minus
    public const string Close = "\uE1B2"; // x
    public const string Check = "\uE06C"; // check
    public const string Reset = "\uE148"; // rotate-ccw
    public const string Trash = "\uE18E"; // trash-2
    public const string Move = "\uE121"; // move
    public const string Download = "\uE0B2"; // download
    public const string Pencil = "\uE1F9"; // pencil
    public const string Globe = "\uE0E8"; // globe
    public const string Folder = "\uE0D7"; // folder
    public const string Calculator = "\uE1BC"; // calculator
    public const string Timer = "\uE1E0"; // timer
    public const string Monitor = "\uE11D"; // monitor
    public const string ControlPanel = "\uE245"; // settings-2
    public const string Activity = "\uE038"; // activity
    public const string Play = "\uE13C"; // play
    public const string Pause = "\uE12E"; // pause
    public const string Previous = "\uE15F"; // skip-back
    public const string Next = "\uE160"; // skip-forward
    public const string ChevronLeft = "\uE06E"; // chevron-left
    public const string ChevronRight = "\uE06F"; // chevron-right
    public const string VolumeMute = "\uE1AC"; // volume-x
    public const string VolumeLow = "\uE1A9"; // volume
    public const string VolumeMedium = "\uE1AA"; // volume-1
    public const string VolumeHigh = "\uE1AB"; // volume-2
    public const string Brightness = "\uE178"; // sun
    public const string DarkMode = "\uE11E"; // moon
    public const string MicOff = "\uE119"; // mic-off
    public const string HeadphoneOff = "\uE629"; // headphone-off
    public const string WeatherSun = "\uE178"; // sun
    public const string WeatherMoon = "\uE11E"; // moon
    public const string WeatherPartly = "\uE216"; // cloud-sun
    public const string WeatherCloud = "\uE088"; // cloud
    public const string WeatherFog = "\uE214"; // cloud-fog
    public const string WeatherRain = "\uE08E"; // cloud-rain
    public const string WeatherSnow = "\uE165"; // snowflake
    public const string WeatherStorm = "\uE08C"; // cloud-lightning
    public const string Wallpaper = "\uE44B"; // wallpaper
    public const string Resize = "\uE1C5"; // move-diagonal-2
    public const string ArrowDown = "\uE042"; // arrow-down
    public const string ArrowUp = "\uE04A"; // arrow-up
    public const string MapPin = "\uE111"; // map-pin
    public const string Cpu = "\uE0A9"; // cpu
    public const string Memory = "\uE445"; // memory-stick
    public const string Wifi = "\uE1AE"; // wifi
    public const string WifiOff = "\uE1AF"; // wifi-off
    public const string Ethernet = "\uE620"; // ethernet-port
    public const string Battery = "\uE053"; // battery
    public const string BatteryCharging = "\uE054"; // battery-charging
    public const string BatteryLow = "\uE056"; // battery-low
    public const string BatteryMedium = "\uE057"; // battery-medium
    public const string BatteryFull = "\uE055"; // battery-full
    public const string Power = "\uE140"; // power
    public const string Lock = "\uE10B"; // lock
    public const string Connected = "\uE3A2"; // monitor-smartphone (page « Connecté », 6D)
    public const string Smartphone = "\uE163"; // smartphone
    public const string Friends = "\uE1A4"; // users
    public const string Health = "\uE36E"; // heart-pulse (onglet « Santé », 6E)
    public const string Ghost = "\uE20E"; // ghost (Halloween, 6F)
    public const string Heart = "\uE0F2"; // heart
    public const string Smile = "\uE164"; // smile
    public const string Plane = "\uE1DE"; // plane
    public const string Seasons = "\uE165"; // snowflake
    public const string HardDrive = "\uE0ED"; // hard-drive
    public const string Thermometer = "\uE186"; // thermometer
    public const string Sleep = "\uE410"; // moon-star
    public const string Restart = "\uE149"; // rotate-cw
    public const string Minimize = "\uE11B"; // minimize-2
    public const string Maximize = "\uE113"; // maximize-2
    public const string AppWindow = "\uE426"; // app-window
    public const string Keyboard = "\uE284"; // keyboard
    public const string WindowsUpdate = "\uE145"; // refresh-cw
    public const string FolderDown = "\uE332"; // folder-down
    public const string Headphones = "\uE0F1"; // headphones
    public const string Bluetooth = "\uE05C"; // bluetooth
    public const string Usb = "\uE356"; // usb
    public const string Gamepad = "\uE0DF"; // gamepad-2
    public const string FileDown = "\uE318"; // file-down
    public const string Clock = "\uE087"; // clock

    /// <summary>Icônes proposées pour un profil : nom Lucide (enregistré) → clé du libellé (fichiers de langue).</summary>
    public static readonly (string Name, string Label)[] ProfileChoices =
    [
        ("user", "icon.user"), ("gamepad-2", "icon.gamepad"), ("briefcase", "icon.briefcase"), ("graduation-cap", "icon.graduation"), ("star", "icon.star"), ("music", "icon.music"), ("house", "icon.house"), ("pencil", "icon.pencil"), ("code", "icon.code"), ("sun", "icon.sun"),
    ];

    private static readonly Dictionary<string, string> ByName = new()
    {
        ["user"] = "\uE19F",
        ["gamepad-2"] = "\uE0DF",
        ["briefcase"] = "\uE062",
        ["graduation-cap"] = "\uE234",
        ["star"] = "\uE176",
        ["music"] = "\uE122",
        ["house"] = "\uE0F5",
        ["pencil"] = "\uE1F9",
        ["code"] = "\uE093",
        ["sun"] = "\uE178",
    };

    /// <summary>Icônes de profil d'avant la 5A (police Segoe Fluent Icons) → nom Lucide.</summary>
    private static readonly Dictionary<char, string> Legacy = new()
    {
        ['\uE77B'] = "user",
        ['\uE7FC'] = "gamepad-2",
        ['\uE821'] = "briefcase",
        ['\uE7BE'] = "graduation-cap",
        ['\uE734'] = "star",
        ['\uE8D6'] = "music",
        ['\uE80F'] = "house",
        ['\uE70F'] = "pencil",
        ['\uE943'] = "code",
        ['\uE706'] = "sun",
    };

    /// <summary>Glyphe d'une icône de profil enregistrée (nom Lucide, ou ancien glyphe d'avant la 5A).</summary>
    public static string Glyph(string? name) =>
        ByName.TryGetValue(Normalize(name), out var glyph) ? glyph : ByName["star"];

    /// <summary>Nom Lucide d'une icône de profil (convertit les anciens glyphes ; inconnu → « star »).</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "star";
        if (name.Length == 1 && Legacy.TryGetValue(name[0], out var converted)) return converted;
        return ByName.ContainsKey(name) ? name : "star";
    }
}
