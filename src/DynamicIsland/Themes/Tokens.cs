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

    /// <summary>Couleurs des notes (identifiant enregistré, clé du nom, couleur) ; « » = sans couleur.</summary>
    public static readonly (string Id, string Name, Color Color)[] NoteColors =
    [
        ("", "notes.color.none", WhiteA(0x40)), ("yellow", "notes.color.yellow", Color.FromRgb(0xFF, 0xD6, 0x0A)),
        ("orange", "notes.color.orange", Orange), ("red", "notes.color.red", Red), ("green", "notes.color.green", Green),
        ("blue", "notes.color.blue", Blue), ("violet", "notes.color.violet", Purple), ("pink", "notes.color.pink", Pink),
    ];

    public static Color NoteColor(string? id) => NoteColors.FirstOrDefault(c => c.Id == (id ?? ""), NoteColors[0]).Color;

    /// <summary>Voile de blanc (alpha 0–255) : surfaces, survols, séparateurs sur fond sombre.</summary>
    public static Color WhiteA(byte alpha) => Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
    public static Color InkA(byte alpha) => Color.FromArgb(alpha, Ink.R, Ink.G, Ink.B);
    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>« #RRGGBB » (réglages, thèmes partagés).</summary>
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>
/// Couleurs qui changent avec le thème d'Opaline (refonte O2 : verre sombre ou « opale blanche »). Côté C#, les brosses
/// <see cref="Paint"/> de ces rôles sont modifiables (jamais figées) : leur couleur change en direct. Côté XAML, utiliser
/// <c>{DynamicResource Paint.Nom}</c> (ressource refaite par <c>Design.OpalShade</c>) — jamais <c>{x:Static t:Paint.Nom}</c>
/// pour ces rôles : un style ou un modèle figerait la brosse partagée.
/// </summary>
public enum InkRole
{
    TextPrimary, TextSecondary, TextTertiary, TextFaint, TextMuted,
    CardBorder, HoverSubtle, Card, Surface, Divider, Raised, Control, SurfaceHover, Selected, SelectedStrong, Track,
    ScrollThumb, ToggleOff, ScrollThumbHover, Inverse, OnInverse, PopupBackground, LabelBackground, PopupBorder, Indicator,
    FolderTile, ActiveBorder, Badge, Window, Preview, Sidebar, SidebarOverMica,
    /// <summary>Texte orange des boutons « doux » (Démarrer le minuteur…) : plus foncé en clair pour rester lisible.</summary>
    TimerText,
    /// <summary>Texte rouge d'alerte (disque presque plein…) : rouge foncé en clair, lisible sur une tuile posée sur un décor coloré.</summary>
    DangerText,
}

/// <summary>Les deux palettes du thème (refonte O2) : mêmes rôles, valeurs sombres (d'origine) et claires (« opale blanche »).</summary>
public static class InkPalette
{
    /// <summary>Encre du thème clair (texte sombre légèrement bleuté).</summary>
    public static readonly Color LightInk = Color.FromRgb(0x15, 0x15, 0x1A);
    /// <summary>Fond « opale blanche » de l'île et du dock (blanc nacré à peine lavande).</summary>
    public static readonly Color PearlBase = Color.FromRgb(0xF5, 0xF4, 0xF9);

    public static readonly Color[] Dark = Build(new()
    {
        [InkRole.TextPrimary] = Swatch.White,
        [InkRole.TextSecondary] = Swatch.WhiteA(0xA8),
        [InkRole.TextTertiary] = Swatch.WhiteA(0x66),
        [InkRole.TextFaint] = Swatch.WhiteA(0x38),
        [InkRole.TextMuted] = Swatch.WhiteA(0xB3),        // pourcentages sur fond sombre
        [InkRole.CardBorder] = Swatch.WhiteA(0x0F),
        [InkRole.HoverSubtle] = Swatch.WhiteA(0x12),
        [InkRole.Card] = Swatch.WhiteA(0x14),
        [InkRole.Surface] = Swatch.WhiteA(0x17),
        [InkRole.Divider] = Swatch.WhiteA(0x1A),
        [InkRole.Raised] = Swatch.WhiteA(0x1C),
        [InkRole.Control] = Swatch.WhiteA(0x1F),
        [InkRole.SurfaceHover] = Swatch.WhiteA(0x26),
        [InkRole.Selected] = Swatch.WhiteA(0x26),
        [InkRole.SelectedStrong] = Swatch.WhiteA(0x2E),
        [InkRole.Track] = Swatch.WhiteA(0x33),
        [InkRole.ScrollThumb] = Swatch.WhiteA(0x38),
        [InkRole.ToggleOff] = Swatch.WhiteA(0x40),
        [InkRole.ScrollThumbHover] = Swatch.WhiteA(0x66),
        [InkRole.Inverse] = Swatch.WhiteA(0xF2),           // tuile active, curseur rempli
        [InkRole.OnInverse] = Swatch.Ink,                  // texte d'une tuile active
        [InkRole.PopupBackground] = Swatch.WithAlpha(Swatch.Raised, 0xF5),
        [InkRole.LabelBackground] = Swatch.WithAlpha(Swatch.Raised, 0xF2),
        [InkRole.PopupBorder] = Swatch.WhiteA(0x26),
        [InkRole.Indicator] = Swatch.WhiteA(0xE6),         // point « appli ouverte »
        [InkRole.FolderTile] = Swatch.WhiteA(0x3D),        // fond d'une icône de dossier
        [InkRole.ActiveBorder] = Swatch.WhiteA(0x59),      // aperçu de la fenêtre active
        [InkRole.Badge] = Swatch.WithAlpha(Swatch.Elevated, 0xE6), // « × » d'une tuile
        [InkRole.Window] = Swatch.Window,
        [InkRole.Preview] = Swatch.Preview,
        [InkRole.Sidebar] = Swatch.Raised,
        [InkRole.SidebarOverMica] = Swatch.WithAlpha(Swatch.Raised, 0x55),
        [InkRole.TimerText] = Swatch.Orange,
        [InkRole.DangerText] = Swatch.Red,
    });

    public static readonly Color[] Light = Build(new()
    {
        [InkRole.TextPrimary] = LightInk,
        [InkRole.TextSecondary] = Ink(0xB4),
        [InkRole.TextTertiary] = Ink(0x8E),
        [InkRole.TextFaint] = Ink(0x52),
        [InkRole.TextMuted] = Ink(0xC0),
        [InkRole.CardBorder] = Ink(0x14),
        [InkRole.HoverSubtle] = Ink(0x0F),
        [InkRole.Card] = Ink(0x0C),
        [InkRole.Surface] = Ink(0x0F),
        [InkRole.Divider] = Ink(0x1A),
        [InkRole.Raised] = Ink(0x12),
        [InkRole.Control] = Ink(0x17),
        [InkRole.SurfaceHover] = Ink(0x1F),
        [InkRole.Selected] = Ink(0x1C),
        [InkRole.SelectedStrong] = Ink(0x26),
        [InkRole.Track] = Ink(0x24),
        [InkRole.ScrollThumb] = Ink(0x38),
        [InkRole.ToggleOff] = Ink(0x33),
        [InkRole.ScrollThumbHover] = Ink(0x5C),
        [InkRole.Inverse] = Color.FromArgb(0xEB, 0x1C, 0x1C, 0x22),
        [InkRole.OnInverse] = Swatch.White,
        [InkRole.PopupBackground] = Color.FromArgb(0xF5, 0xFA, 0xFA, 0xFC),
        [InkRole.LabelBackground] = Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFC),
        [InkRole.PopupBorder] = Ink(0x1F),
        [InkRole.Indicator] = Ink(0xCC),
        [InkRole.FolderTile] = Ink(0x1A),
        [InkRole.ActiveBorder] = Ink(0x59),
        [InkRole.Badge] = Color.FromArgb(0xE6, 0xE9, 0xE9, 0xEE),
        [InkRole.Window] = Color.FromRgb(0xF3, 0xF3, 0xF6),
        [InkRole.Preview] = Color.FromRgb(0xEE, 0xEE, 0xF2),
        [InkRole.Sidebar] = Color.FromRgb(0xE9, 0xE9, 0xEE),
        [InkRole.SidebarOverMica] = Swatch.WhiteA(0x66),
        [InkRole.TimerText] = Color.FromRgb(0xA0, 0x46, 0x06),
        // ≥ 4,5 jusque sur une tuile assez foncée (luminance ≈ 0,47, voir Opal.LightTileWorst) : 4,7 mesuré
        [InkRole.DangerText] = Color.FromRgb(0x8A, 0x16, 0x10),
    });

    private static Color Ink(byte alpha) => Swatch.WithAlpha(LightInk, alpha);

    private static Color[] Build(Dictionary<InkRole, Color> values)
    {
        var colors = new Color[Enum.GetValues<InkRole>().Length];
        foreach (var (role, color) in values) colors[(int)role] = color;
        return colors;
    }
}

/// <summary>Brosses prêtes à l'emploi, nommées par leur rôle (gelées, sauf celles qui suivent le thème : voir <see cref="InkRole"/>).</summary>
public static class Paint
{
    /// <summary>Brosses qui suivent le thème (une par rôle), modifiées en direct par <c>Design.OpalShade</c>.</summary>
    public static readonly SolidColorBrush[] Themed = Enum.GetValues<InkRole>().Select(r => new SolidColorBrush(InkPalette.Dark[(int)r])).ToArray();

    /// <summary>Nom de la ressource dynamique (XAML) d'un rôle : « Paint.TextPrimary ».</summary>
    public static string Key(InkRole role) => "Paint." + role;

    // Texte
    public static readonly Brush White = Frozen(Swatch.White);
    /// <summary>Forme opaque sous l'île qui porte le halo : noire, elle ne se voit qu'au bord adouci de l'île (comme l'ombre d'avant).</summary>
    public static readonly Brush HaloSource = Frozen(Swatch.Black);
    public static readonly Brush TextPrimary = Themed[(int)InkRole.TextPrimary];
    public static readonly Brush TextSecondary = Themed[(int)InkRole.TextSecondary];
    public static readonly Brush TextTertiary = Themed[(int)InkRole.TextTertiary];
    public static readonly Brush TextFaint = Themed[(int)InkRole.TextFaint];
    public static readonly Brush TextMuted = Themed[(int)InkRole.TextMuted];
    public static readonly Brush Ink = Frozen(Swatch.Ink);                       // texte sur fond blanc (toujours)
    public static readonly Brush InkMuted = Frozen(Swatch.InkA(0xB3));
    public static readonly Brush OnInverse = Themed[(int)InkRole.OnInverse];     // texte d'une tuile active (Inverse)

    // Surfaces (voiles de blanc sur fond sombre, d'encre sur fond clair)
    public static readonly Brush CardBorder = Themed[(int)InkRole.CardBorder];
    public static readonly Brush HoverSubtle = Themed[(int)InkRole.HoverSubtle];
    public static readonly Brush Card = Themed[(int)InkRole.Card];
    public static readonly Brush Surface = Themed[(int)InkRole.Surface];
    public static readonly Brush Divider = Themed[(int)InkRole.Divider];
    public static readonly Brush Raised = Themed[(int)InkRole.Raised];
    public static readonly Brush Control = Themed[(int)InkRole.Control];
    public static readonly Brush SurfaceHover = Themed[(int)InkRole.SurfaceHover];
    public static readonly Brush Selected = Themed[(int)InkRole.Selected];
    public static readonly Brush SelectedStrong = Themed[(int)InkRole.SelectedStrong];
    public static readonly Brush Track = Themed[(int)InkRole.Track];
    public static readonly Brush ScrollThumb = Themed[(int)InkRole.ScrollThumb];
    public static readonly Brush ToggleOff = Themed[(int)InkRole.ToggleOff];
    public static readonly Brush ScrollThumbHover = Themed[(int)InkRole.ScrollThumbHover];
    public static readonly Brush Inverse = Themed[(int)InkRole.Inverse];          // tuile active, curseur rempli
    public static readonly Brush PopupBackground = Themed[(int)InkRole.PopupBackground];
    public static readonly Brush LabelBackground = Themed[(int)InkRole.LabelBackground];
    public static readonly Brush PopupBorder = Themed[(int)InkRole.PopupBorder];
    public static readonly Brush ThumbStroke = Frozen(Color.FromArgb(0x33, 0, 0, 0));
    public static readonly Brush Indicator = Themed[(int)InkRole.Indicator];     // point « appli ouverte »
    public static readonly Brush FolderTile = Themed[(int)InkRole.FolderTile];   // fond d'une icône de dossier
    public static readonly Brush ActiveBorder = Themed[(int)InkRole.ActiveBorder]; // aperçu de la fenêtre active
    public static readonly Brush Badge = Themed[(int)InkRole.Badge];             // « × » d'une tuile
    /// <summary>Presque transparent (alpha 1) : capte la souris dans une fenêtre « layered » sans se voir.</summary>
    public static readonly Brush HitTarget = Frozen(Color.FromArgb(1, 0, 0, 0));
    public static readonly Brush Window = Themed[(int)InkRole.Window];
    public static readonly Brush Preview = Themed[(int)InkRole.Preview];
    public static readonly Brush TextSelection = Frozen(Swatch.Blue);
    public static readonly Brush Sidebar = Themed[(int)InkRole.Sidebar];
    public static readonly Brush SidebarOverMica = Themed[(int)InkRole.SidebarOverMica];

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
    /// <summary>Pastille d'une icône active (O4) : rang « bouton » de la hiérarchie panneau &gt; tuile &gt; bouton.</summary>
    public static readonly CornerRadius IconChip = new(Radius.M);
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
    // Icône active (O4) : glyphe en dégradé d'accent + pastille squircle teintée d'accent derrière
    public const double ActiveTint = 0.2;      // opacité de la pastille d'accent
    public const double ActiveTintLight = 0.16; // pastille sur une tuile claire / inverse
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
/// <summary>Fenêtres « carte » (7C) : « Donner mon avis » et « Nouveautés ».</summary>
public static class CardShape
{
    public const double Width = 600;
    public const double FeedbackHeight = 720;
    public const double NewsHeight = 690;
    public const double TextHeight = 100;          // zone de texte de l'avis
    public const double Thumbnail = 128;           // aperçu de la capture d'écran (largeur)
    public const double ButtonWidth = 140;
    public const int Logo = 56;
    public const double Bullet = 7;                // pastille des nouveautés
    public const double Rise = 14;                 // montée à l'apparition (DIP)
    public const double FirstReveal = 0.18;        // s avant la première ligne
    public const double Stagger = 0.055;           // s entre deux lignes
    public const double EditorWidth = 640;         // éditeur de règle (7D)
    public const double EditorHeight = 740;
    public const double FieldWidth = 220;          // champ de texte d'une règle
    public const double PickerMaxWidth = 220;      // texte d'un bouton de choix
    public const double VaultWidth = 520;          // fenêtre d'une entrée du coffre
    public const double VaultHeight = 640;
    public const double NoteHeight = 70;           // note d'une entrée du coffre
    public const double ClipWidth = 520;           // « Nouveau texte » du presse-papiers
    public const double ClipHeight = 520;
    public const double ClipTextHeight = 160;
    public const double NoteEditorWidth = 560;     // éditeur d'une note (redimensionnable)
    public const double NoteEditorHeight = 600;
    public const double NoteEditorMinWidth = 420;
    public const double NoteEditorMinHeight = 380;
    public const double Swatch = 18;               // pastille de couleur d'une note
    public const double Grip = 22;                 // coin pour agrandir la fenêtre
}

/// <summary>Fenêtre des paramètres : notre propre barre de titre (correctif 10) et la recherche (correctif 11).</summary>
public static class SettingsShape
{
    public const double TitleBar = 40;            // hauteur de la barre (zone de déplacement)
    public static readonly GridLength TitleBarRow = new(TitleBar); // la même, pour une ligne de grille (XAML)
    public const double SearchBox = 34;
    public const double SearchPopup = 320;
    public const int SearchResults = 8;
    public const double SearchScrollMargin = 24;  // le réglage trouvé s'arrête un peu sous le haut
}

public static class WelcomeShape
{
    public const double Width = 760;
    public const double Height = 656;              // la page Modules tient sans défiler, ville de la météo comprise
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
    public const double CityBox = 180;             // champ « Ville de la météo » (page Modules)
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
    public const double MusicVolume = 60;          // curseur du volume de l'appli qui joue (à droite des boutons)
    public const double CalendarCell = 28;
    public static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(350);
}

/// <summary>Widgets des potes (7H) : marges, tailles dans l'île et dans les paramètres.</summary>
public static class PluginShape
{
    public const double Padding = 16;          // marge intérieure d'un widget avec carte
    public const double LineHeight = 1.35;     // hauteur d'une ligne de texte (en tailles de police)
    public const double RingLabel = 0.22;      // texte au centre d'un anneau, en fraction de sa taille
    public const double ImageOversample = 2;   // images décodées à 2× leur taille (nettes à 200 %)
    public const double IslandTile = 80;       // hauteur d'un widget en bas de l'onglet Widgets de l'île
    public const double IslandRow = 88;        // rangée ajoutée à l'île (tuile + marge)
    public const double SettingsPreview = 72;  // aperçu dans la liste des paramètres
    public const double InstallPreview = 120;  // aperçu avant d'installer
    public const double PreviewMaxRatio = 2.6; // un aperçu très large est réduit (largeur ≤ 2,6 × hauteur)
    public const double DesktopCascade = 32;   // décalage de la place par défaut d'un widget à l'autre
    /// <summary>Temps entre deux mises à jour : widget qui montre des mesures (processeur, musique…) ou seulement l'heure.</summary>
    public static readonly TimeSpan FastRefresh = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SlowRefresh = TimeSpan.FromSeconds(15);
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
    /// <summary>
    /// Lissage du suivi : part du chemin parcourue en <see cref="ActiveTick"/> (recalculée pour la durée réelle de chaque image,
    /// le suivi est le même quelle que soit la cadence). 0,5 en 33 ms ≈ constante de temps de 48 ms (avant : 0,35 ≈ 77 ms).
    /// </summary>
    public const double Follow = 0.5;
    /// <summary>Durée de référence du lissage, et cadence de --glass-bench (mesure historique).</summary>
    public static readonly TimeSpan ActiveTick = TimeSpan.FromMilliseconds(33);
    /// <summary>
    /// Lumière qui bouge : suivie aux images de WPF (CompositionTarget.Rendering), au plus une mise à jour toutes les 9 ms
    /// (100 Hz et moins : chaque image ; 120 Hz : 60/s ; 144 Hz : 72/s ; un écran rapide ne coûte pas 2 à 3 fois plus). Avant : minuterie
    /// de 33 ms que Windows arrondissait à 46,8 ms (pas de 15,6 ms) → 21 mises à jour/s seulement, d'où le halo « qui lague ».
    /// </summary>
    public static readonly TimeSpan MinFrame = TimeSpan.FromMilliseconds(9);
    /// <summary>Curseur loin : simple lecture de sa position, lente.</summary>
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

    /// <summary>
    /// Grands chiffres (O4) : Inter Display 4.1 (licence SIL OFL 1.1, Assets/Fonts/LICENSE-inter.txt), sous-ensemble latin
    /// embarqué en trois graisses (<see cref="NumberWeight"/>). Toujours avec des chiffres à largeur fixe
    /// (<see cref="Numerals.Apply"/> ou le style XAML « Text.Numbers ») : sinon les chiffres « dansent » quand ils changent.
    /// Un caractère absent du sous-ensemble passe à Segoe UI Variable Display.
    /// </summary>
    public static readonly FontFamily Numbers = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter Display, Segoe UI Variable Display, Segoe UI");

    /// <summary>Nom de la famille embarquée (vérifié par --self-test).</summary>
    public const string NumbersFamily = "Inter Display";
}

/// <summary>Graisses des grands chiffres : fines, et SEULEMENT celles qui sont embarquées (sinon WPF simule une graisse).</summary>
public static class NumberWeight
{
    public static readonly FontWeight Hairline = FontWeights.ExtraLight; // très grands chiffres (minuteur, horloge du bureau)
    public static readonly FontWeight Light = FontWeights.Light;         // titres chiffrés (tuiles, horloges du monde, « Ma semaine »)
    public static readonly FontWeight Text = FontWeights.Medium;         // petits chiffres (heure de l'île, compte à rebours)

    public const double HairlineFrom = 30; // à partir de cette taille : ExtraLight
    public const double LightFrom = 17;    // à partir de cette taille : Light ; en dessous : Medium (lisible sur le verre)

    public static FontWeight For(double size) => size >= HairlineFrom ? Hairline : size >= LightFrom ? Light : Text;
}

/// <summary>Pose la police des chiffres (taille, graisse selon la taille, chiffres tabulaires) sur un texte.</summary>
public static class Numerals
{
    public static T Apply<T>(T text, double size) where T : System.Windows.Controls.TextBlock
    {
        text.FontFamily = FontStack.Numbers;
        text.FontSize = size;
        text.FontWeight = NumberWeight.For(size);
        System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }

    /// <summary>Pour un conteneur (les textes enfants héritent police + chiffres tabulaires ; graisse fixée par la taille).</summary>
    public static void ApplyInherited(System.Windows.Controls.Control control, double size)
    {
        control.FontFamily = FontStack.Numbers;
        control.FontWeight = NumberWeight.For(size);
        System.Windows.Documents.Typography.SetNumeralAlignment(control, FontNumeralAlignment.Tabular);
    }
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
    // Grands chiffres (O4, police FontStack.Numbers) : un peu plus grands que le texte, car plus fins
    public const double NumberTile = 18;   // valeur d'une tuile (Widgets, Santé)
    public const double NumberClock = 22;  // heure d'une horloge du monde
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
    public const double CaptionButton = 40;         // réduire / fermer des paramètres
    public const double TabIcon = 26;               // onglet de l'île en colonne (icône seule) : 9 onglets tiennent sans défiler
    public const double CaptionButtonHeight = 30;
    public const double IconButton = 36;
    public const double SmallIconButton = 26;
    public const double TinyButton = 20;     // « × » des tuiles
    public const double IconChip = 24;       // pastille d'une icône active (O4) : coche d'une carte de choix, icône d'une tuile
    public const double IconChipLarge = 30;  // pastille d'une grande icône active (tuiles « Ne pas déranger » / Stage)
    public const double PlayButton = 44;
    public const double Chip = 24;
    public const double SoftButton = 34;
    public const double Tab = 30;
    public const double NavItem = 35; // 12 pages dans 560 DIP de haut (hauteur minimale des paramètres)
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
    public const double MenuMinWidth = 180;
    public const double MenuMaxHeight = 360; // menu sombre long (sons du minuteur) : il défile au-delà
    public const double TimerStepButton = 22; // − / + d'un compteur du minuteur « Perso »
    public const double TimerStepColumn = 44; // largeur d'un compteur (heures, minutes, secondes)
    public const double TimerDigits = 52;     // rangée des chiffres du minuteur (« 05:00 » en Hero)
    public const double TimerStart = 112;     // bouton « Démarrer » du minuteur
}

/// <summary>Géométrie de l'île (en DIP).</summary>
public static class IslandShape
{
    public static readonly Size Compact = new(124, 36);
    public static readonly Size CompactWide = new(220, 36);
    public static readonly Size CompactWideHours = new(252, 36); // minuteur d'une heure ou plus (« 1:30:00 ») : l'heure reste dégagée
    // Pilule réduite (O5) : largeur calculée d'après son contenu (LivePill.Width), entre ces bornes
    public const double CompactMin = 96;
    public const double CompactMax = 300;
    public const double PillRestPad = 24;    // air de chaque côté de l'heure au repos
    public const double PillPad = 8;         // bord de la pilule → anneau / pochette / valeur
    public const double PillGap = 8;         // anneau / valeur → heure
    public const double LiveRing = 20;       // anneau de progression d'une activité
    public const double LiveArt = 22;        // pochette (musique) dans la pilule
    public static readonly Size Hud = new(250, 36);
    public static readonly Size Gaming = new(64, 6);
    public const double IntroDot = 8;            // intro : le point lumineux d'où l'île se déploie
    public static readonly Size Music = new(440, 218);   // + curseur du volume de l'appli
    public static readonly Size Calendar = new(488, 262);
    public static readonly Size Control = new(440, 238);
    /// <summary>Rangée « Appareils connectés » sous les profils (centre de contrôle), ajoutée seulement s'il y en a.</summary>
    public const double DevicesRow = 38;
    /// <summary>Colonne des tuiles du centre de contrôle (7F) : mode sombre + veilleuse, puis « Ne pas déranger » en large.</summary>
    public static readonly GridLength ControlTilesColumn = new(224);
    public static readonly Size Panel = new(440, 196);   // widgets, étagère, vocal
    /// <summary>Contenu de l'onglet Widgets sans les widgets des potes (7H), qui ajoutent <see cref="PluginShape.IslandRow"/> en dessous.</summary>
    public const double WidgetsContentHeight = 128;
    public static readonly GridLength WidgetsContentRow = new(WidgetsContentHeight);
    public static readonly Size Claude = new(480, 262);  // mini Claude : la conversation défile
    public static readonly Size Voice = new(480, 262);   // vocal Discord : une ligne par personne (volume, couper pour moi), la liste défile
    public const double VoiceContentWidth = 444;
    public const double VoiceContentHeight = 192;
    public const double VoiceRowHeight = 36;
    public const double VoiceName = 118;           // colonne du nom (les noms longs sont coupés)
    public const double VoiceWheelStep = 5;        // molette sur le volume d'une personne : ±5 points (0 à 200 %)
    public static readonly int[] VoiceMenuLevels = [50, 100, 150]; // clic droit : volumes proposés
    public static readonly Size Health = new(488, 272);  // santé du PC : mesures, disques, 3 applis
    public static readonly Size Clipboard = new(480, 272); // presse-papiers : historique ou coffre (la liste défile)
    public const double ClipboardContentWidth = 444;
    public const double ClipboardListHeight = 168;
    public const double ClipboardRow = 40;
    public const double ClipboardThumb = 30;
    public static readonly Size Notes = new(480, 272);    // notes : la liste défile
    /// <summary>Traduire : texte d'origine et traduction côte à côte (chacun défile), langues et boutons.</summary>
    public static readonly Size Translate = new(480, 272);
    public const double TranslateContentWidth = 444;
    public const double TranslateContentHeight = 206;
    public const double TranslatePaneGap = 8;    // entre les deux cadres (origine | traduction)
    public const double NotesContentWidth = 444;
    public const double NotesListHeight = 176;
    public const double NoteStripe = 3;          // liseré de couleur d'une note
    public const int NotePreviewLines = 3;       // lignes d'aperçu sous le titre
    public const double WideTabs = 552;          // l'île peut s'élargir jusque-là si la rangée d'onglets est longue (fenêtre de 600 moins le halo)
    public const double HealthContentWidth = 454;
    public static readonly Size Mixer = new(440, 266);   // mélangeur : volume général + 4 applis (la liste défile au-delà)
    public const double MixerListHeight = 140;   // liste des applis du mélangeur
    public const double MixerRow = 34;           // une appli du mélangeur
    public const double MixerName = 112;         // colonne du nom (le curseur prend le reste)
    public const double MixerIcon = 20;          // icône de l'appli (exe) dans le mélangeur
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
    public static readonly double MaxExpandedWidth = Math.Max(WideTabs, new[] { Music, Calendar, Control, Panel, Claude, Voice, Health, Mixer, Clipboard, Notes, Translate }.Max(s => s.Width));
    public const double ClaudeBubble = 340;      // largeur max d'une question dans la conversation
    public const double ClaudeContentWidth = 444; // contenu de l'onglet Claude (île de 480 moins ses marges)
    public const double ClaudeContentHeight = 192;
    public const double EdgeMargin = 24;         // place du halo quand l'île part du bord gauche / droit de sa fenêtre
    /// <summary>Fenêtre de l'île à la taille 100 % (multipliée par le réglage « Taille de l'île ») ; assez large pour les onglets en colonne.</summary>
    public static readonly Size Window = new(600, 340);
    /// <summary>Onglets en colonne : l'île peut s'élargir de la largeur de la colonne (fenêtre moins la place du halo).</summary>
    public const double MaxExpandedWidthVertical = 552;
    public const double MaxExpandedHeightVertical = 290;   // = hauteur de la vue ouverte (ExpandedView)
    public const double TabScrollBar = 3;         // barre de défilement fine des onglets (trop d'onglets)
    public const double SnapDistance = 16;       // aimantation au bord de l'écran / au centre
}

/// <summary>
/// Grille de tuiles de l'île (refonte O6) : cases carrées de <see cref="Cell"/> DIP séparées de <see cref="Gap"/>, 4 colonnes ×
/// 2 lignes par page ; S = 1 case, W = 2 × 1, L = 2 × 2. L'île entière (grille comprise) suit le réglage « Taille de l'île ».
/// </summary>
public static class TileGridShape
{
    public const int Columns = 4;
    public const int Rows = 2;
    public const double Cell = 92;               // côté d'une case (tuile S)
    public const double Gap = 8;                 // entre deux tuiles
    public const double Footer = 18;             // rangée du bas : nom de la tuile survolée (centre), points des pages (droite)
    public const double Header = 32;             // = hauteur minimale de l'en-tête de l'île ouverte (date / « ← Nom », heure)
    public const double Dot = 6;                 // point d'une page
    public const double DotActive = 16;          // point de la page affichée (pilule)
    public const double DotGap = 5;
    public const double DotHit = 14;             // zone cliquable d'un point
    public const double SwipeZone = 96;          // largeur de la zone des points où l'on peut glisser pour changer de page
    public const double SwipeThreshold = 24;     // glisser de plus que ça (DIP) = page suivante / précédente
    public const double Art = 96;                // pochette d'une tuile L
    public const double ArtSmall = 22;           // pochette d'une tuile W (à droite)
    public const double LineMaxHeight = 30;      // ligne d'information d'une tuile L : 2 lignes au plus
    public static readonly Thickness Padding = new(10);
    public static readonly Thickness PaddingSmall = new(8, 9, 8, 8); // tuile S : un peu plus de place pour le nom
    public const double LineHeight = 1.33;      // hauteur d'une ligne de texte, en fois la taille de la police
    public static double PageWidth => Columns * (Cell + Gap);
    public static double PageHeight => Rows * (Cell + Gap);
    /// <summary>Île ouverte sur la grille : marges de la vue ouverte (s8, s5 / s8), en-tête, page, rangée du bas.</summary>
    public static Size Island => new(PageWidth + 2 * Space.S8, Space.S5 + Header + Space.S4 + PageHeight + Footer + Space.S8);
}

/// <summary>
/// Tuiles vivantes de la grille (O6, lot « vivantes A ») : Musique (pochette floutée + visualiseur), Météo (ciel animé),
/// Horloge analogique. Les animations tournent à <see cref="Frame"/> (≤ 30 i/s), seulement page visible et île ouverte.
/// </summary>
public static class LiveTiles
{
    /// <summary>Intervalle des animations (0,94 × 1/30 s : la minuterie de Windows arrondit à 15,6 ms → 30 i/s au plus).</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromSeconds(0.94 / Motion.DecorativeFrameRate);
    public static readonly Thickness Padding = new(10);
    public static readonly Thickness PaddingLarge = new(12);
    public static readonly Thickness PaddingSmall = new(9);

    // Texte posé sur une image (pochette floutée, ciel) : blanc, avec un voile sombre dessous (lisible dans les deux thèmes)
    public static readonly Brush OnArt = Paint.Frozen(Swatch.White);
    public static readonly Brush OnArtSecondary = Paint.Frozen(Swatch.WhiteA(0xC8));
    public static readonly Brush OnArtTertiary = Paint.Frozen(Swatch.WhiteA(0x99));
    /// <summary>Voile du haut vers le bas (alpha 0–255) : léger en haut, plus dense là où sont les textes.</summary>
    public const byte ScrimTop = 0x26;
    public const byte ScrimBottom = 0x9E;
    public static readonly Color ScrimColor = Swatch.Black;

    // Musique
    public const double CoverBlur = 9;             // rayon du flou (pixels de la copie réduite à Opal.BlurWidth)
    public const double CoverVibrancy = 1.4;       // couleurs un peu plus vives sous le voile
    public const double CoverL = 64;               // pochette nette d'une tuile L
    public const double CoverW = 48;               // … d'une tuile W
    public const double CoverS = 30;               // … d'une tuile S
    public static readonly Brush CoverRim = Paint.Frozen(Swatch.WhiteA(0x24));
    public const int Bars = 5;                     // barres du visualiseur
    public const double BarWidth = 3;
    public const double BarGap = 2;
    public const double BarHeight = 16;            // hauteur maximale d'une barre (L, W)
    public const double BarHeightSmall = 12;       // … en S
    public const double BarRest = 0.18;            // hauteur au repos (part de la hauteur maximale)
    public static readonly double[] BarPause = [0.32, 0.55, 0.42, 0.62, 0.36]; // morceau en pause : barres figées
    public const double ControlButton = 26;        // précédent / suivant
    public const double ControlButtonSmall = 24;   // … en W
    public const double PlayButton = 32;           // lecture / pause (L)
    public const double PlayButtonSmall = 28;      // … en W
    public const double EmptyChip = 48;            // état vide : pastille de l'icône (L)

    // Météo : grands chiffres (police des chiffres, fins)
    public const double TempS = 30;
    public const double TempW = 34;
    public const double TempL = 46;
    public const int RainDrops = 26;               // tuile L (W : 70 %, S : 45 %) — jamais plus de 40 particules
    public const int SnowFlakes = 22;
    public const int Stars = 18;
    public const int MaxParticles = 40;
    public const double RainSpeed = 150;           // DIP / s
    public const double RainLength = 9;
    public const double RainSlant = 0.22;          // décalage horizontal par DIP de chute
    public const double SnowSpeed = 16;
    public const double SnowSway = 4;              // balancement des flocons (DIP)
    public const double CloudDrift = 5;            // DIP / s
    public const double SunSpin = 5;               // rayons du soleil : degrés / s
    public const double SunBreath = 0.04;          // le halo grossit / rapetisse de 4 %
    public const int SunRays = 12;
    public static readonly TimeSpan FlashEvery = TimeSpan.FromSeconds(9);   // orage : un éclair toutes les 9 s environ
    public static readonly TimeSpan FlashLength = TimeSpan.FromMilliseconds(260);
    public const double FlashOpacity = 0.32;

    // Couleurs du ciel (haut, bas) : assez profondes pour un texte blanc
    public static readonly (Color Top, Color Bottom) SkyClear = (Color.FromRgb(0x1A, 0x5C, 0xC2), Color.FromRgb(0x3F, 0x8C, 0xDB));
    public static readonly (Color Top, Color Bottom) SkyGolden = (Color.FromRgb(0x33, 0x47, 0x9E), Color.FromRgb(0xD9, 0x7C, 0x55));
    public static readonly (Color Top, Color Bottom) SkyCloudy = (Color.FromRgb(0x44, 0x57, 0x73), Color.FromRgb(0x74, 0x85, 0x9C));
    public static readonly (Color Top, Color Bottom) SkyRain = (Color.FromRgb(0x2E, 0x3D, 0x52), Color.FromRgb(0x4E, 0x5E, 0x74));
    public static readonly (Color Top, Color Bottom) SkyStorm = (Color.FromRgb(0x1A, 0x1D, 0x2E), Color.FromRgb(0x36, 0x3A, 0x52));
    public static readonly (Color Top, Color Bottom) SkySnow = (Color.FromRgb(0x52, 0x68, 0x88), Color.FromRgb(0x8C, 0x9F, 0xB9));
    public static readonly (Color Top, Color Bottom) SkyFog = (Color.FromRgb(0x56, 0x61, 0x6E), Color.FromRgb(0x86, 0x91, 0x9D));
    public static readonly (Color Top, Color Bottom) SkyNight = (Color.FromRgb(0x08, 0x0E, 0x29), Color.FromRgb(0x1F, 0x2B, 0x58));
    public static readonly (Color Top, Color Bottom) SkyNightCloudy = (Color.FromRgb(0x10, 0x14, 0x1F), Color.FromRgb(0x25, 0x2C, 0x3D));
    public static readonly Color SunCore = Color.FromRgb(0xFF, 0xD8, 0x6E);
    public static readonly Color SunCenter = Color.FromRgb(0xFF, 0xF2, 0xC4);   // reflet du soleil (haut gauche)
    public const double SunRadius = 20;            // tuile L (W : 15, S : 12)
    public const double SunRadiusW = 15;
    public const double SunRadiusS = 12;
    public const double RayWidth = 1.8;
    public const double RayWidthSmall = 1.4;
    public const double RainWidth = 1.2;
    public static readonly Color SunGlow = Color.FromArgb(0x70, 0xFF, 0xC2, 0x5A);
    public static readonly Color SunRay = Color.FromArgb(0x66, 0xFF, 0xE3, 0x9E);
    public static readonly Color Moon = Color.FromRgb(0xF1, 0xEE, 0xE2);
    public static readonly Color MoonGlow = Color.FromArgb(0x30, 0xD8, 0xE2, 0xFF);
    public static readonly Color Star = Swatch.White;
    public static readonly Color CloudDay = Swatch.WhiteA(0xE0);
    public static readonly Color CloudGrey = Color.FromArgb(0xC8, 0xB4, 0xBE, 0xCE);
    public static readonly Color CloudNight = Color.FromArgb(0x5C, 0xA8, 0xB4, 0xCC);
    public static readonly Color CloudStorm = Color.FromArgb(0xD0, 0x5A, 0x60, 0x78);
    public static readonly Color Rain = Color.FromArgb(0xA0, 0xCF, 0xE3, 0xFF);
    public static readonly Color Snow = Swatch.WhiteA(0xE8);
    public static readonly Color Bolt = Color.FromRgb(0xFF, 0xF1, 0xA8);
    public static readonly Color FogBand = Swatch.WhiteA(0x3A);
    public const double CloudFar = 0.55;           // nuage lointain (derrière le soleil)
    public const double CloudSmallTile = 0.68;     // tuiles S et W : nuages plus voilés derrière la température
    public const int GoldenMorningUntil = 9;       // avant 9 h et après 17 h (de jour) : ciel doré
    public const int GoldenEveningFrom = 17;

    // Horloge analogique (proportions du rayon du cadran)
    public const double DialInset = 4;             // marge entre la tuile et le cadran
    public const double HourHand = 0.5;
    public const double MinuteHand = 0.74;
    public const double SecondHand = 0.84;
    public const double SecondTail = 0.18;         // contrepoids de la trotteuse
    public const double HourWidth = 3.6;
    public const double MinuteWidth = 2.4;
    public const double SecondWidth = 1.2;
    public const double HubSize = 7;               // axe des aiguilles
    public const double HubSecond = 3;             // axe de la trotteuse (accent)
    public const double TickMajor = 0.1;           // longueur des repères des heures
    public const double TickMinor = 0.045;         // … des minutes (tuile L)
    public const double TickMajorWidth = 1.6;
    public const double TickMinorWidth = 1;
    public const double Numbers = 0.72;            // rayon où sont posés les chiffres (tuile L)
    public const double DateOffset = 0.36;         // date sous l'axe (tuile L)
    public const double SmallDial = 0.72;          // tuile S : traits plus fins
    public const double TickQuarter = 1.35;        // repères de 12, 3, 6 et 9 h : plus longs
    public const double SecondMinScale = 0.85;     // la trotteuse ne s'amincit presque pas en S (elle resterait invisible)
}

/// <summary>Tuiles de tous les jours de la grille (O6, lot A) : minuteur, café, date, disques, compteur, notes et « À faire ».</summary>
public static class EverydayTileShape
{
    // Minuteur : anneau de progression (bouton marche / pause en son centre)
    public const double RingSmall = 30;          // tuile S : en haut à droite
    public const double RingWide = 56;           // tuile W : à gauche
    public const double RingLarge = 88;          // tuile L : au centre, le temps dedans
    public const double RingStrokeSmall = 3;
    public const double RingStroke = 4.5;
    public const double RingStrokeLarge = 6;
    public const double PresetHeight = 22;       // pastilles 5 / 10 / 25 / Pomodoro
    public const double PresetMinWidth = 24;
    public const double PresetGap = 3;
    // Café
    public const double CupLarge = 48;           // tasse de la tuile L
    public const double SteamHeight = 14;        // hauteur de la fumée au-dessus de la tasse
    public const double SteamRise = 7;           // la fumée monte de tant en un cycle
    public const double SteamStroke = 1.6;
    public const double SteamMaxOpacity = 0.55;
    public static readonly TimeSpan SteamCycle = TimeSpan.FromSeconds(2.4);
    // Date
    public const double DayHero = 44;            // grand numéro du jour (W, L)
    public const double MonthCellHeight = 15;    // mini-mois de la tuile L
    public const double TodayDot = 15;           // pastille du jour dans le mini-mois
    public const double TodayDotOpacity = 0.5;
    public const double MonthLabel = 9;          // initiales des jours du mini-mois
    // Compteur
    public const double CounterLarge = 58;       // chiffre de la tuile L
    public const double CounterBump = 1.08;      // le chiffre sursaute à chaque clic
    // Disques
    public const double DiskBar = 4;
    public const double DiskRowGap = 4;
    // Listes (notes, à faire)
    public const double TaskRowHeight = 18;
    public const double MiniButton = 22;         // « + » en haut à droite d'une tuile
}

/// <summary>
/// Tuiles-outils de la grille (O6, lot B : fond d'écran, micro, capture, couleur, calculatrice, QR code, mémo vocal, téléphone,
/// paramètres) et loupe du sélecteur de couleur.
/// </summary>
public static class ToolTileShape
{
    public const double Button = 52;              // gros bouton rond d'une tuile W (micro, mémo)
    public const double ButtonLarge = 84;         // … d'une tuile L
    public const double StopRatio = 0.34;         // carré « arrêter » du mémo, en part du bouton
    public const double SwatchSmall = 22;         // dernière couleur (tuile S)
    public const double SwatchLarge = 40;         // dernière couleur (tuile W)
    public const double SwatchHero = 56;          // dernière couleur (tuile L)
    public const double HistorySwatch = 14;       // couleurs d'avant (W, L)
    public const double HistoryGap = 4;
    public const int History = 5;                 // couleurs d'avant montrées (W, L)
    public const int HistoryKept = 10;            // couleurs gardées en mémoire
    public const double Qr = 72;                  // QR code d'une tuile W
    public const double QrLarge = 116;            // … d'une tuile L
    public const double QrPadding = 4;            // marge blanche autour du QR (la norme veut un fond clair)
    public const int QrModule = 4;                // pixels par module de l'image (agrandie sans flou)
    public const int ThumbWidth = 384;            // miniature du fond d'écran (pixels, décodée une fois)
    public static readonly Color ScrimBottom = Color.FromArgb(0xA0, 0, 0, 0);
    public static readonly Color ScrimTop = Color.FromArgb(0x00, 0, 0, 0);
    public static readonly Color ThumbChip = Color.FromArgb(0x55, 0, 0, 0); // pastille de l'icône posée sur la miniature
    public static readonly Color OnImageText = Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF); // ligne d'info posée sur la miniature
    public const double ScrimStart = 0.35;
    public const double DropDash = 3;             // pointillés de la zone de dépôt (tuile Téléphone), en épaisseurs de trait        // le voile commence à ce point de la hauteur
    // Loupe du sélecteur de couleur.
    public const int LoupePixels = 13;            // pixels de l'écran montrés (côté, impair : un pixel au centre)
    public const double LoupeCell = 9;            // DIP par pixel agrandi
    public static double Loupe => LoupePixels * LoupeCell;
    public const double LoupeOffset = 26;         // écart entre la souris et la loupe
    public static readonly Color LoupeRim = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
    public static readonly Color LoupeShade = Color.FromArgb(0x8C, 0, 0, 0);
    public const double LoupeRimWidth = 2;
}

/// <summary>Géométrie du dock pour des icônes de <see cref="ReferenceIcon"/> px : tout suit la taille choisie.</summary>
public static class DockShape
{
    public const double ReferenceIcon = 48;
    public const double MinIcon = 24;               // réglage « Taille du dock » (correctif 15b : barre de ~31 px)
    public const double MaxIcon = 72;
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
    public const double MergeZone = 0.22;           // zone de dépôt (dossier), en largeur d'icône
    public const double HighlightRadius = 0.28;     // arrondi du cadre de dépôt, en fraction de sa taille
    public const double HighlightPadding = 1.4;     // marge du cadre de dépôt, en marges d'icône
    public const double Separator = 40;
    public const double SearchWidth = 128;
    public const double SearchHeight = 40;
    public const double SearchMinHeight = 22;       // minimums : tout tient encore dans la barre à 24 px
    public const double Profile = 34;
    public const double ProfileMin = 20;
    public const double ProfileGlyph = 15;
    public const double ProfileGlyphMin = 12;
    public const double LabelOffset = 10;           // étiquette du nom au-dessus de l'icône
    public const double TrayCell = 22;
    public const double TrayCellMin = 18;           // 2 lignes seulement si des cases de 18 tiennent, sinon une seule
    public const double TrayIcon = 16;              // taille des icônes de Windows : jamais réduite
    public const double EdgeGap = 6;                // applis alignées à gauche / à droite : écart avec la recherche ou la zone d'état
    public const double PreviewCloseSmall = 16;     // « × » d'une fenêtre dans l'aperçu (correctif 15c)
    public const double PreviewCloseMedium = 20;
    public const double PreviewCloseLarge = 26;
    public const double PreviewCloseGlyphSmall = 8;
    public const double PreviewCloseGlyphMedium = 9;
    public const double PreviewCloseGlyphLarge = 12;
    public const double PreviewThumbMaxWidth = 320;  // miniature d'une fenêtre dans l'aperçu : la carte suit le format de la
    public const double PreviewThumbMaxHeight = 200; // fenêtre dans ces bornes (bandes vides seulement si une borne mini joue)
    public const double PreviewThumbMinWidth = 168;  // assez large pour le titre et le « × »
    public const double PreviewThumbMinHeight = 84;
    public const double PreviewThinSource = 60;      // hauteur (px) en dessous de laquelle DWM rend la barre d'une fenêtre réduite
    public const double FolderTileWidth = 96;       // case de la grille d'un dossier ouvert
    public const double FolderTileIcon = 52;
    public const double FolderMinWidth = 220;
    public const double FolderAnchorGap = 14;       // entre la grille et l'icône du dossier
    public const int FolderColumns = 4;
    public const double SnapDistance = 24;          // dock flottant déplacé : aimanté au centre du bord
    public const double StatusCell = 36;            // case de la zone d'état (6B), pour des icônes de 48
    public const double StatusCellMin = 22;
    public const double StatusGlyph = 18;           // icône Lucide d'une case
    public const double StatusGlyphMin = 12;
    public const double StatusText = 12.5;          // « FR » (langue du clavier)
    public const double StatusTextMin = 10;
    public const double DesktopTuckDot = 5;         // flèche des icônes du bureau : petites icônes qui rentrent / sortent
    public const double DesktopTuckDistance = 16;
    public const double DesktopArrowSlide = 3;      // la flèche glisse un peu pendant qu'elle pivote
    public const double StatusBadge = 15;           // pastille du nombre (mises à jour)
    public const double StatusDot = 6;              // point « nouveau » / « pleine »
    public const double StatusVolumeStep = 0.02;    // molette sur la case « Mélangeur » : volume général ±2 %
    public const double PoofDistance = 26;          // effet « poof » de la corbeille : distance des bulles
    public const int DownloadsShown = 12;           // pile Téléchargements : éléments affichés
    public const double MovingOpacity = 0.75;       // dock pendant qu'on le déplace
}

/// <summary>Stage Manager (7G) : colonne de miniatures à gauche de chaque écran (en DIP).</summary>
public static class StageShape
{
    public const double ThumbWidth = 168;      // miniature DWM d'un groupe (format de la fenêtre gardé)
    public const double ThumbHeight = 100;
    public const double ChipWidth = 44;        // petite miniature d'une autre fenêtre de la pile (cliquable)
    public const double ChipHeight = 28;
    public const int MaxChips = 3;
    public const double PileOffset = 5;        // décalage des cartes de la pile derrière la miniature
    public const int PileCards = 2;
    public const double ScreenMargin = 12;     // entre le bord de l'écran et la colonne
    public const double StageGap = 16;         // entre la colonne et la fenêtre au centre
    public const double GroupGap = 6;          // entre deux groupes de la colonne
    public const double GroupHeight = 150;     // place d'un groupe (miniature + nom + pile), pour compter combien tiennent
    public const int MaxGroups = 6;
    public const double Shrink = 0.8;          // fenêtre trop grande pour la scène : ramenée à 80 % de la place
    public const double ControlTile = 56;      // tuile « Stage Manager » du centre de contrôle (icône seule)
    public const double HandleWidth = 6;       // poignée de la colonne auto-masquée (pilule visible, collée au bord gauche)
    public const double HandleHeight = 72;
    public const double HandleHitWidth = 14;   // zone de survol de la poignée (transparente, alpha 1)
    public const double HandleHitHeight = 120;
    public const double HandleInset = 2;       // entre le bord de l'écran et la pilule
    public const byte HandleAlpha = 0x8C;      // opacité de la pilule (couleur d'accent)
    public const double SwapLeavingOpacity = 0.72; // échange : la fenêtre qui part vers la colonne s'estompe un peu (en retrait)
    public const double SwapShadowBlur = 36;   // échange : ombre légère sous la fenêtre qui arrive
    public const double SwapShadowDepth = 10;
    public const double SwapShadowOpacity = 0.38;
    /// <summary>Largeur totale de la colonne (miniature + marges de la carte et de la fenêtre).</summary>
    public const double ColumnWidth = ThumbWidth + 2 * 8 + 2 * 8 + 2 * PileOffset;
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
    // Mouvement (refonte O3)
    public static readonly SpringSpec MorphWidth = new(290, 0.6);   // ouverture « liquide » : la largeur part d'abord et dépasse un peu
    public static readonly SpringSpec MorphHeight = new(215, 0.72); // … puis la hauteur, un peu plus molle
    public static readonly SpringSpec MorphRadius = new(160, 0.86); // … et les coins se détendent en dernier
    public static readonly SpringSpec CascadeIn = new(420, 0.68);   // un élément du contenu qui arrive (échelle, décalage, flou)
    public static readonly SpringSpec CascadeOut = new(900, 1);     // fermeture : inverse, plus rapide
    public static readonly SpringSpec Tilt = new(260, 0.9);         // inclinaison d'une tuile vers la souris (lissée)
    public static readonly SpringSpec PressDown = new(1100, 0.95);  // la tuile s'enfonce
    public static readonly SpringSpec PressRelease = new(520, 0.42); // … puis rebondit
    public static readonly SpringSpec Page = new(300, 0.8);         // page / onglet qui glisse (avec élan)
    public static readonly SpringSpec PageExit = new(600, 1);       // la page qui part
    public static readonly SpringSpec Elastic = new(260, 0.62);     // bout d'une rangée : petit rebond élastique
    public static readonly SpringSpec MorphOpen = new(240, 0.8);    // une tuile s'agrandit en outil (et revient)
    public static readonly SpringSpec Calm = new(1400, 1);           // « Réduire les animations » : tout ressort devient court, sans rebond
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
    public static readonly TimeSpan Highlight = TimeSpan.FromMilliseconds(1600); // réglage trouvé par la recherche (2 clignements)
    public static readonly TimeSpan Base = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Medium = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Long = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan Scale = TimeSpan.FromMilliseconds(420);  // contenu de l'île qui grandit
    public static readonly TimeSpan Bounce = TimeSpan.FromMilliseconds(600); // rebond d'une icône au lancement
    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(650);  // alerte de fin du minuteur
    public static readonly TimeSpan Genie = TimeSpan.FromMilliseconds(500);  // réduction façon macOS
    public static readonly TimeSpan StageSwap = TimeSpan.FromMilliseconds(360); // Stage Manager : échange colonne ↔ centre
    public static readonly TimeSpan StageSwapFade = TimeSpan.FromMilliseconds(100); // fin de l'échange : l'image animée s'efface sur la vraie fenêtre
    public static readonly TimeSpan StageColumnFade = TimeSpan.FromMilliseconds(160); // nouvelle miniature de la colonne après un échange
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
    // Mouvement (refonte O3)
    public static readonly TimeSpan CascadeStagger = TimeSpan.FromMilliseconds(32);       // décalage entre deux éléments (25–40 ms)
    public static readonly TimeSpan CascadeStart = TimeSpan.FromMilliseconds(70);         // la forme a commencé à s'ouvrir
    public static readonly TimeSpan CascadeReverseStagger = TimeSpan.FromMilliseconds(12); // fermeture : plus rapide, ordre inverse
    public static readonly TimeSpan MorphHeightLag = TimeSpan.FromMilliseconds(45);       // la hauteur part après la largeur
}

/// <summary>Valeurs du mouvement (refonte O3) : cascade, inclinaison des tuiles, pages, élastique, outils. Voir Animation/.</summary>
public static class Kinetics
{
    // Activité en direct indéterminée (O5) : arc de l'anneau qui tourne doucement, à faible cadence
    public const double LiveSpinArc = 0.28;      // longueur de l'arc (part du tour)
    public static readonly TimeSpan LiveSpin = TimeSpan.FromSeconds(1.6); // un tour
    public const int LiveFrameRate = 20;

    public const int CascadeMaxItems = 12;       // au-delà, les éléments arrivent avec le dernier (une longue liste ne traîne pas)
    public const double CascadeScale = 0.94;     // échelle de départ d'un élément
    public const double CascadeShift = 8;        // décalage de départ (DIP, vers le bas)
    public const double CascadeBlur = 6;         // flou de départ (rayon), retiré à la fin
    public const double CascadeOpaqueAt = 0.6;   // l'élément est entièrement visible à 60 % de son arrivée
    public const double CascadeRow = 12;         // éléments à moins de 12 DIP de hauteur l'un de l'autre = même rangée (gauche → droite)
    public const double TiltMax = 2.5;           // inclinaison maximale (degrés)
    public const double TiltHover = 1.012;       // la tuile survolée grossit à peine
    public const double TiltShift = 1.2;         // et glisse d'un rien vers la souris (DIP)
    public const double PressScale = 0.97;       // tuile enfoncée
    public const double ShineAlpha = 0.16;       // reflet qui suit le curseur (blanc, au centre)
    public const double ShineAlphaLight = 0.42;  // … en opale blanche (blanc sur nacre : plus présent)
    public const double ShineRadius = 0.75;      // rayon du reflet, en part du plus grand côté de la tuile
    public const double PageDistance = 36;       // glissement d'une page (DIP)
    public const double PageFling = 260;         // élan de la page qui arrive (DIP / s)
    public const double PageExit = 0.5;          // la page qui part glisse moitié moins loin
    public const double ElasticMax = 14;         // bout d'une rangée : jamais plus de 14 DIP
    public const double ElasticGain = 1.6;       // élan donné par un cran de molette (vitesse / delta)
    public const double OpaqueVeil = 0.95;       // « Réduire la transparence » : voile du verre opalin
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
    public const string ChevronDown = "\uE06D"; // chevron-down (fl\u00E8che des ic\u00F4nes du bureau, correctif 16)
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
    public const string Feedback = "\uE56F"; // message-square-heart (« Donner mon avis », 7C)
    public const string Bug = "\uE20C"; // bug
    public const string Send = "\uE152"; // send
    public const string News = "\uE343"; // party-popper (« Nouveautés », 7C)
    public const string Beta = "\uE0D5"; // flask-conical (canal Bêta)
    public const string Picture = "\uE0F6"; // image
    public const string Clipboard = "\uE085"; // clipboard (onglet \u00AB Presse-papiers \u00BB)
    public const string ClipboardPlus = "\uE5BF"; // clipboard-plus (ajouter un texte au presse-papiers)
    public const string Notes = "\uE303"; // sticky-note (onglet \u00AB Notes \u00BB)
    public const string NotePen = "\uE596"; // notebook-pen (nouvelle note)
    public const string Translate = "\uE0FE"; // languages (onglet \u00AB Traduire \u00BB)
    public const string SwapLanguages = "\uE24A"; // arrow-left-right (inverser les langues)
    public const string Paste = "\uE3E8"; // clipboard-paste (traduire ce qui a \u00E9t\u00E9 copi\u00E9)
    public const string TaskOpen = "\uE167"; // square (case \u00E0 cocher vide d'une note)
    public const string TaskDone = "\uE16A"; // check-square (case coch\u00E9e)
    public const string ListChecks = "\uE1D0"; // list-checks (ins\u00E9rer une case)
    public const string ResizeGrip = "\uE1C5"; // move-diagonal-2 (coin pour agrandir une fen\u00EAtre)
    public const string Key = "\uE4A3"; // key-round (coffre de mots de passe)
    public const string Vault = "\uE58F"; // vault
    public const string Pin = "\uE259"; // pin
    public const string PinOff = "\uE2B6"; // pin-off
    public const string Link = "\uE102"; // link
    public const string LockOpen = "\uE10C"; // lock-open
    public const string Eraser = "\uE28F"; // eraser
    public const string User = "\uE19F"; // user
    public const string Eye = "\uE0BA"; // eye
    public const string EyeOff = "\uE0BB"; // eye-off
    public const string TextItem = "\uE198"; // type (\u00E9l\u00E9ment texte de l'historique)
    public const string Archive = "\uE30D"; // file-archive
    public const string Automations = "\uE1B4"; // zap (automatisations, 7D)
    public const string Week = "\uE2A3"; // bar-chart-3 (page \u00AB Ma semaine \u00BB, 7F)
    public const string FolderOpen = "\uE247"; // folder-open
    public const string BellOff = "\uE05A"; // bell-off (Ne pas déranger)
    public const string Bell = "\uE059"; // bell
    public const string Mixer = "\uE162"; // sliders-vertical (m\u00E9langeur de volume : onglet de l'\u00EEle + zone d'\u00E9tat du dock)
    public const string Message = "\uE117"; // message-square
    public const string ArrowRight = "\uE049"; // arrow-right
    public const string HardDrive = "\uE0ED"; // hard-drive
    public const string Thermometer = "\uE186"; // thermometer
    public const string Sleep = "\uE410"; // moon-star
    public const string DoNotDisturb = "\uE410"; // moon-star (Ne pas d\u00E9ranger, 7F : diff\u00E9rent de la lune du mode sombre)
    public const string NightLight = "\uE17A"; // sunset (veilleuse, 7F)
    public const string Restart = "\uE149"; // rotate-cw
    public const string Minimize = "\uE11B"; // minimize-2
    public const string Maximize = "\uE113"; // maximize-2
    public const string AppWindow = "\uE426"; // app-window
    public const string Stage = "\uE470"; // layout-panel-left (Stage Manager, 7G)
    public const string Keyboard = "\uE284"; // keyboard
    public const string WindowsUpdate = "\uE145"; // refresh-cw
    public const string FolderDown = "\uE332"; // folder-down
    public const string Headphones = "\uE0F1"; // headphones
    public const string Bluetooth = "\uE05C"; // bluetooth
    public const string Usb = "\uE356"; // usb
    public const string Gamepad = "\uE0DF"; // gamepad-2
    public const string Mouse = "\uE28E"; // mouse
    public const string FileDown = "\uE318"; // file-down
    public const string Clock = "\uE087"; // clock
    public const string AnalogClock = "\uE250"; // clock-3 (tuile Horloge analogique, O6)
    // Tuiles de tous les jours (O6, lot A)
    public const string Coffee = "\uE096"; // coffee
    public const string CalendarDays = "\uE2B9"; // calendar-days
    public const string Counter = "\uE4DA"; // tally-5
    public const string ListTodo = "\uE4C3"; // list-todo
    public const string Always = "\uE1E7"; // infinity
    public const string Pomodoro = "\uE3C6"; // brain
    // Tuiles-outils de la grille (O6, lot B) : codes lus dans lucide.ttf (tables post + cmap).
    public const string Pipette = "\uE13B"; // pipette (s\u00E9lecteur de couleur)
    public const string QrCode = "\uE1DF"; // qr-code
    public const string Capture = "\uE257"; // scan (capture d'\u00E9cran)
    public const string Record = "\uE345"; // circle-dot (m\u00E9mo vocal)
    public const string NextImage = "\uE145"; // refresh-cw (fond suivant)

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


/// <summary>Page « Ma semaine » (7F) : petits graphiques de la semaine (couleurs vérifiées avec le validateur de palette, fond sombre).</summary>
public static class WeekShape
{
    public const double ChartHeight = 132;         // hauteur de la zone des barres (temps d'écran par jour)
    public const double BarWidth = 26;             // largeur d'une barre (fine)
    public const double BarRadius = 4;             // bout arrondi, ancré sur la ligne de base
    public const double SegmentGap = 2;            // espace entre la part « applis » et la part « jeux » d'une barre
    public const double MinSegment = 3;            // une part non nulle reste visible
    public const double LegendDot = 8;             // pastille de la légende
    public const double PomodoroDot = 9;           // une pastille = un Pomodoro terminé
    public const int PomodoroDotsShown = 6;        // au-delà : « +n »
    public const double TopBar = 6;                // barre horizontale des applis / jeux les plus utilisés
    public const double TopNameWidth = 170;
    public const double TopValueWidth = 72;
    public static readonly CornerRadius BarTop = new(BarRadius, BarRadius, 0, 0);
    /// <summary>Jeux (2e série) : sarcelle foncée, ΔE ≥ 15 avec l'indigo d'Opaline, même pour les daltoniens.</summary>
    public static readonly Brush Games = Paint.Frozen(Color.FromRgb(0x22, 0xA5, 0xBF));
}

/// <summary>
/// Réglages d'une intensité du « verre opalin » (refonte O1). Alphas et opacités de 0 à 1 ; longueurs en DIP, sauf
/// <see cref="Blur"/> (en pixels de la copie réduite du fond d'écran, voir <see cref="Opal.BlurWidth"/>).
/// </summary>
public sealed record OpalLook(
    double Blur, double Vibrancy,                 // flou simulé du décor, saturation de ce décor flouté (1 = inchangée)
    double Smoke, double MilkTop, double MilkBottom, double Tint, // voile sombre, voile laiteux (haut → bas), teinte d'accent
    double Grain,                                 // opacité du grain
    double IridShift, double IridPale, double IridAlpha, double IridWidth, // reflets irisés de la bordure
    double TopLine,                               // liseré lumineux du haut
    ShadowSpec Shadow,                            // ombre douce dessous
    double GlowBlur, double GlowOpacity,          // lueur opaline d'une tuile survolée
    double SweepAlpha, double SweepWidth);        // balayage nacré (largeur en fraction de la surface)

/// <summary>Identité « verre opalin » (refonte O1) : les trois intensités de la démo, rayons, grain, reflets, balayage.</summary>
public static class Opal
{
    public static readonly OpalLook Discreet = new(
        Blur: 3, Vibrancy: 1.0, Smoke: 0.66, MilkTop: 0.07, MilkBottom: 0.02, Tint: 0.07, Grain: 0.025,
        IridShift: 18, IridPale: 0.55, IridAlpha: 0.32, IridWidth: 1, TopLine: 0.30,
        Shadow: new(18, 4, 0.32), GlowBlur: 26, GlowOpacity: 0.40, SweepAlpha: 0.16, SweepWidth: 0.26);

    public static readonly OpalLook Balanced = new(
        Blur: 5, Vibrancy: 1.18, Smoke: 0.50, MilkTop: 0.13, MilkBottom: 0.04, Tint: 0.15, Grain: 0.04,
        IridShift: 26, IridPale: 0.38, IridAlpha: 0.62, IridWidth: 1.25, TopLine: 0.48,
        Shadow: new(26, 6, 0.42), GlowBlur: 36, GlowOpacity: 0.62, SweepAlpha: 0.30, SweepWidth: 0.32);

    public static readonly OpalLook Intense = new(
        Blur: 7, Vibrancy: 1.40, Smoke: 0.34, MilkTop: 0.21, MilkBottom: 0.07, Tint: 0.25, Grain: 0.055,
        IridShift: 34, IridPale: 0.25, IridAlpha: 0.88, IridWidth: 1.6, TopLine: 0.70,
        Shadow: new(34, 8, 0.48), GlowBlur: 46, GlowOpacity: 0.85, SweepAlpha: 0.46, SweepWidth: 0.38);

    public static OpalLook For(Settings.OpalStyle style) => style switch
    {
        Settings.OpalStyle.Discreet => Discreet,
        Settings.OpalStyle.Intense => Intense,
        _ => Balanced,
    };

    /// <summary>
    /// « Opale blanche » (refonte O2) : la même intensité en clair — voile nacré plus présent (c'est lui qui fait le blanc
    /// laiteux), teinte et grain plus légers, bordure irisée plus saturée (un pastel trop pâle disparaît sur du blanc),
    /// ombre et lueur plus douces.
    /// </summary>
    public static OpalLook Light(OpalLook look) => look with
    {
        Smoke = Math.Max(look.Smoke + LightSmokeExtra, LightSmokeMin),
        MilkTop = look.MilkTop * 1.3, MilkBottom = look.MilkBottom * 1.3,
        Tint = look.Tint * 0.75, Grain = look.Grain * 0.6,
        IridPale = look.IridPale * 0.55, IridAlpha = Math.Min(1, look.IridAlpha * 1.15),
        TopLine = look.TopLine * 1.2,
        Shadow = new(look.Shadow.Blur * 1.15, look.Shadow.Depth * 0.8, look.Shadow.Opacity * LightShadow),
        GlowOpacity = look.GlowOpacity * 0.7, SweepAlpha = look.SweepAlpha * 0.8,
    };

    /// <summary>Intensité entre le sombre (0) et le clair (1), pour la transition entre les deux thèmes.</summary>
    public static OpalLook Blend(OpalLook a, OpalLook b, double t)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        double L(double x, double y) => x + (y - x) * t;
        return new OpalLook(L(a.Blur, b.Blur), L(a.Vibrancy, b.Vibrancy), L(a.Smoke, b.Smoke), L(a.MilkTop, b.MilkTop),
            L(a.MilkBottom, b.MilkBottom), L(a.Tint, b.Tint), L(a.Grain, b.Grain), L(a.IridShift, b.IridShift), L(a.IridPale, b.IridPale),
            L(a.IridAlpha, b.IridAlpha), L(a.IridWidth, b.IridWidth), L(a.TopLine, b.TopLine),
            new ShadowSpec(L(a.Shadow.Blur, b.Shadow.Blur), L(a.Shadow.Depth, b.Shadow.Depth), L(a.Shadow.Opacity, b.Shadow.Opacity)),
            L(a.GlowBlur, b.GlowBlur), L(a.GlowOpacity, b.GlowOpacity), L(a.SweepAlpha, b.SweepAlpha), L(a.SweepWidth, b.SweepWidth));
    }

    // Couleurs (refonte O2)
    public const double LightSmokeExtra = 0.16; // voile nacré en plus en clair
    public const double LightSmokeMin = 0.62;
    public const double LightShadow = 0.5;      // ombres (île, dock, fenêtres) moins fortes en clair
    public const double LightSurfaceTint = 0.10; // part de l'accent du fond d'écran dans le blanc nacré de l'île
    /// <summary>Changement de fond d'écran, de couleur ou de thème : couleurs interpolées (OKLCH) pendant ce temps.</summary>
    public static readonly TimeSpan ColorTransition = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan ColorTick = TimeSpan.FromMilliseconds(33); // ~30 images/s, seulement pendant une transition
    /// <summary>Copie floutée du fond : ancienne image qui s'efface en fondu quand le fond d'écran change.</summary>
    public static readonly TimeSpan BackdropFade = TimeSpan.FromMilliseconds(1200);
    // Verre liquide (shader) en clair : le liseré et la lumière ajoutent du blanc, invisible (ou délavé) sur du blanc
    public const double LightGlassRim = 0.45;
    public const double LightGlassSheen = 0.6;
    public const double LightGlassTopLight = 0.25;

    // Rayons cohérents : panneau > tuile > bouton
    public const double PanelRadius = 28;      // île ouverte, carte du bas de colonne
    public const double DockRadius = 22;
    public const double TileRadius = 18;
    public const double InnerRadius = 12;      // pochette, icône du dock
    public const double ButtonRadius = 10;

    // Grain : petite texture de bruit générée une fois (même graine = même image), posée en mosaïque
    public const int GrainSize = 128;
    public const int GrainSeed = 0x0FA1;

    // Flou simulé : copie du fond d'écran réduite à cette largeur, flou « boîte » en plusieurs passes, puis agrandie
    public const int BlurWidth = 256;
    public const int BlurPasses = 3;

    // Reflets irisés : angle de la bordure selon la souris (degrés), lissage, cadence (seulement quand la souris bouge)
    public const double IridBaseAngle = 35;
    public const double IridSwing = 150;       // de la gauche à la droite de la fenêtre
    public const double IridSlide = 0.28;      // glissement du centre du dégradé (haut → bas de la fenêtre)
    public const double Follow = 0.22;         // part du chemin parcourue à chaque image
    public const double FollowDone = 0.002;    // assez proche : la minuterie s'arrête
    public static readonly TimeSpan FollowTick = TimeSpan.FromMilliseconds(33);

    // Balayage nacré
    public static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(600);
    public const double SweepAngle = 18;
    public static readonly TimeSpan GlowFade = TimeSpan.FromMilliseconds(220);
    public const double HaloReach = 90;        // place autour d'une surface pour son ombre et sa lueur

    // Respiration de la bordure irisée pendant une activité en direct (O5) : lente, à faible cadence, jamais au repos
    public static readonly TimeSpan BreathPeriod = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan BreathFrame = TimeSpan.FromMilliseconds(80); // ≈ 12 images/s (≤ 20) : assez pour une onde de 3 s
    public const double BreathSwing = 22;      // ± degrés de rotation des reflets
    public const double BreathLow = 0.55;      // éclat le plus bas de la bordure (1 = normal)

    // Application à l'app (O1, suite) : île, dock, palette, cartes, tuiles, menus
    public const double CoveredSmoke = 0.86;   // voile quand une fenêtre (pas le bureau) est derrière l'île : pas de flou du fond
    public const double PanelSmokeMin = 0.80;  // surfaces sans flou du fond (palette, cartes) : jamais plus transparentes
    /// <summary>
    /// Opale blanche : fond le plus foncé supposé pour une TUILE (verre clair posé sur un décor coloré flouté, mesuré ≈ #BFB3D7) ;
    /// le texte d'accent des tuiles est rendu lisible dessus (≥ 4,5), pas seulement sur la surface de référence.
    /// </summary>
    public static readonly Color LightTileWorst = Color.FromRgb(0xB4, 0xB0, 0xC0);
    public const double TileLift = 0.035;      // une tuile est un peu plus laiteuse que la surface qui la porte
    public const double TileHoverLift = 0.05;  // + au survol
    public const double TileTint = 0.55;       // part de la teinte d'accent de la surface reprise par une tuile
    public const double TileRim = 0.55;        // part de l'irisation de la surface reprise par la bordure d'une tuile
    public const double TileGlow = 0.55;       // lueur sous une tuile survolée (part de OpalLook.GlowOpacity)
    public const double TileGlowBlur = 0.6;    // rayon de cette lueur (part de OpalLook.GlowBlur)
    public const double PopupTint = 0.7;       // teinte d'accent des menus clic droit (part de OpalLook.Tint)
    public const double PopupRim = 0.6;        // bordure irisée des menus (part de OpalLook.IridAlpha)
    public const double TopLineInset = 0.4;    // le liseré du haut s'arrête avant les coins (part du rayon)
    public const double SweepMinWidth = 24;
    public const double SweepHeight = 2.2;     // hauteur du reflet du balayage (part de la hauteur de la surface)
    // Lisibilité d'abord : sur un fond d'écran clair, le voile sombre posé sur la copie floutée est renforcé
    public const double BrightFrom = 0.40;     // luminosité moyenne du fond à partir de laquelle on renforce
    public const double BrightRange = 0.35;    // renfort complet à BrightFrom + BrightRange
    public const double BrightSmoke = 0.60;    // part du chemin vers un voile opaque au renfort complet
    public const int BackdropDecodeWidth = 640; // fond d'écran lu à cette largeur avant d'être flouté (assez pour 256 px)
    public static readonly TimeSpan BackdropCheck = TimeSpan.FromSeconds(60); // diaporamas, Wallpaper Engine : date du fichier
    public static readonly Size SnapshotScreen = new(1920, 1080); // --snapshot : écran supposé derrière l'île / le dock
    public const double SnapshotEdge = 8;      // --snapshot : distance de l'île / du dock au bord de cet écran

    // Choix du style : petites cartes avec un aperçu vivant (assistant, paramètres)
    public static readonly Size PreviewPill = new(112, 30);
    public const double PreviewScene = 52;     // hauteur du petit décor flouté derrière la pilule
    public const double PreviewSceneRadius = 14;

    // Fenêtre de démo (O1)
    public const double DemoWidth = 1280;
    public const double DemoHeight = 760;
    public const double DemoCanvasWidth = 1264; // mise en page fixe, mise à l'échelle avec la fenêtre
    public const double DemoCanvasHeight = 716;
    public const double ColumnWidth = 392;
    public const double TileUnit = 80;          // tuile S ; W = 2 × 1, L = 2 × 2
    public const double TileGap = 10;
    public static readonly Size Pill = new(150, 36);
    public static readonly Size IslandOpen = new(360, 128);
    public static readonly Size Dock = new(344, 60);
    public const double DockIcon = 40;
    public const double Cover = 72;
    public const double Ring = 58;              // anneaux CPU / RAM de la tuile Moniteur
    public const double RingStroke = 6;
    public static readonly Size Switch = new(42, 24);
    public const double SwitchKnob = 18;
    public const double Logo = 36;

    /// <summary>Décor « clair » de la démo (fond du pack dans des tons pâles).</summary>
    public static readonly Mood LightMood = new(Hex("#DCE4F5"), Hex("#F1F4FB"), Hex("#A9C1FF"), Hex("#F5BFDD"), Hex("#FFFFFF"));
    /// <summary>Plaque neutre (pas en verre opalin) sous l'en-tête et la note de la démo.</summary>
    public static readonly Brush Plate = Paint.Frozen(Color.FromArgb(0xB8, 0x12, 0x12, 0x16));

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// Tuiles vivantes du lot B (refonte O6) : Moniteur (anneaux CPU / RAM / GPU qui respirent), Amis (avatars + anneau de statut),
/// Profil (pastilles des profils). Tailles en DIP, à l'intérieur d'une case de <see cref="TileGridShape.Cell"/>.
/// </summary>
public static class LiveTileShape
{
    // Moniteur
    public const double RingS = 32;               // anneau d'une tuile S (2 côte à côte)
    public const double RingStrokeS = 3.5;
    public const double RingW = 44;               // tuile W : 2 anneaux + leur nom à droite
    public const double RingStrokeW = 4;
    public const double RingL = 50;               // tuile L : 3 anneaux (CPU, RAM, GPU)
    public const double RingStrokeL = 4.5;
    public const double RingBreath = 0.14;        // la « respiration » : ± 14 % de l'épaisseur du trait
    public const double RingGlowExtra = 5;        // lueur : trait plus large sous l'anneau…
    public const double RingGlowMin = 0.07;       // … dont l'opacité respire entre ces deux valeurs
    public const double RingGlowMax = 0.2;
    public const double Alert = 0.85;             // au-delà : anneau rouge
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(40); // ≈ 25 images/s (≤ 30), seulement page visible et île ouverte
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(2); // mesures (comme l'onglet Santé)
    public static readonly SpringSpec Gauge = new(170, 0.82); // l'anneau suit la valeur

    // Amis
    public const double AvatarS = 30;
    public const double AvatarW = 32;
    public const double AvatarL = 24;
    public const double AvatarRing = 2;           // anneau de statut
    public const double AvatarInset = 2;          // espace entre l'anneau et la pastille
    public const double AvatarBadge = 15;         // mini manette « en jeu »
    public const double AvatarGap = 4;            // entre deux avatars (tuile W)
    public const int AvatarsW = 4;
    public const int RowsL = 4;
    public static readonly Brush Online = Paint.Frozen(Swatch.Green);

    // Profil
    public const double Pastille = 28;
    public const double PastilleGap = 8;
    public const int PastillesW = 5;
    public const double ProfileDot = 5;           // un point par profil (tuile S)
    public const double ProfileDotActive = 12;
    public const double PopFrom = 0.78;           // la pastille choisie « saute » de 78 % à 100 %
}