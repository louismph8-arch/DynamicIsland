using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Thème de l'app : remplace les ressources partagées (fond de l'île/du dock, couleur d'accent) à chaque
/// changement de réglage. Les éléments qui les utilisent en <c>DynamicResource</c> se mettent à jour seuls.
/// Donne aussi les valeurs calculées (arrondis, halo, vitesse) aux fenêtres. Toutes les constantes viennent
/// des tokens (Themes/Tokens.cs).
/// </summary>
public static class AppearanceService
{
    public const string IslandBrushKey = "IslandBrush";
    public const string DockBrushKey = "DockBrush";
    public const string AccentBrushKey = "AccentBrush";
    public const string AccentGradientKey = "AccentGradientBrush";
    /// <summary>Accent des petits textes posés sur une tuile (≥ 4,5, aussi en opale blanche) — OpalShade.</summary>
    public const string AccentTextKey = "AccentTextBrush";

    private static AppearanceSettings Settings => SettingsStore.Current.Appearance;

    /// <summary>Couleurs du fond d'écran en service (option cochée et déjà extraites).</summary>
    public static bool UsesWallpaperColors =>
        Settings.AccentFromWallpaper && AppearanceColors.TryParse(Settings.WallpaperAccentStart, out _);

    /// <summary>Accent effectif : celui du fond d'écran si l'option est cochée, sinon celui du profil.</summary>
    public static Color AccentStart =>
        AppearanceColors.TryParse(UsesWallpaperColors ? Settings.WallpaperAccentStart : Settings.AccentStart, out var c) ? c : Swatch.AccentStart;
    public static Color AccentEnd =>
        AppearanceColors.TryParse(UsesWallpaperColors ? Settings.WallpaperAccentEnd : Settings.AccentEnd, out var c) ? c : AccentStart;

    /// <summary>
    /// Fond de l'île et du dock AFFICHÉ (pendant un changement de fond d'écran ou de thème : couleur de transition) :
    /// noir mêlé d'une teinte très sombre du fond d'écran, ou blanc nacré (opale blanche) à peine teinté.
    /// </summary>
    public static Color SurfaceBase => Design.OpalShade.Shown.SurfaceBase;

    /// <summary>Fond de l'île et du dock visé, en sombre ou en clair (couleurs du fond d'écran comprises).</summary>
    public static Color SurfaceFor(bool light)
    {
        bool tinted = UsesWallpaperColors && Settings.TintFromWallpaper;
        if (light)
            return tinted && AppearanceColors.TryParse(Settings.WallpaperAccentStart, out var accent)
                ? Design.OpalColor.Mix(InkPalette.PearlBase, accent, Opal.LightSurfaceTint)
                : InkPalette.PearlBase;
        return tinted && AppearanceColors.TryParse(Settings.WallpaperSurface, out var tint)
            ? Lerp(Swatch.IslandBase, tint, AutoColor.SurfaceMix)
            : Swatch.IslandBase;
    }

    /// <summary>Couleur unique représentant l'accent (milieu du dégradé).</summary>
    public static Color AccentMid => Lerp(AccentStart, AccentEnd, 0.5);

    /// <summary>Accent AFFICHÉ (milieu du dégradé), qui glisse doucement vers le nouveau quand le fond d'écran change.</summary>
    public static Color ShownAccentMid => Design.OpalColor.Mix(Design.OpalShade.Shown.AccentStart, Design.OpalShade.Shown.AccentEnd, 0.5);

    public static double Glow => Settings.GlowIntensity;
    public static double Speed => Settings.AnimationSpeed;

    /// <summary>Rayon d'une forme réglable selon le réglage « Arrondi des coins ».</summary>
    public static double Radius(RadiusRange range) => range.At(Settings.Roundness);

    /// <summary>
    /// Réglages changés : nouvelles couleurs visées (accent, fond, thème), atteintes en douceur par <see cref="Design.OpalShade"/>
    /// qui refait les ressources (IslandBrush, DockBrush, AccentBrush, AccentGradientBrush, « Paint.Nom ») et le verre opalin.
    /// </summary>
    public static void Apply() => Design.OpalShade.Retarget();

    /// <summary>
    /// Ombre / halo néon : à 0 % l'ombre <paramref name="shape"/> classique (noire), à 100 % un halo plus large
    /// de la couleur d'accent. En opale blanche, l'ombre est plus douce.
    /// </summary>
    /// <param name="color">Couleur imposée (ex. pochette de l'album) ; le halo est alors au moins à <see cref="Shadows.GlowMinWithCover"/>.</param>
    public static void ApplyGlow(DropShadowEffect effect, ShadowSpec shape, Color? color = null)
    {
        double glow = color != null ? Math.Max(Glow, Shadows.GlowMinWithCover) : Glow;
        double soft = Design.OpalShade.IsLight ? Opal.LightShadow : 1; // valeurs visées : ces effets ne sont refaits qu'au changement de réglage
        effect.Color = Lerp(Swatch.Black, color ?? AccentMid, Math.Min(1, glow * Shadows.GlowColorBoost));
        effect.Opacity = (shape.Opacity + Shadows.GlowOpacityRange * glow) * soft;
        effect.BlurRadius = shape.Blur + Shadows.GlowBlurExtra * glow;
        effect.ShadowDepth = shape.Depth * (1 - glow);
        effect.Direction = shape.Direction;
    }

    /// <summary>Liseré autour de l'île et du dock : blanc (encre en clair) discret, qui prend la couleur d'accent avec le halo.</summary>
    public static Color RimColor(Color? color = null)
    {
        var c = color ?? AccentMid;
        double glow = color != null ? Math.Max(Glow, Shadows.GlowMinWithCover) : Glow;
        var rest = Design.OpalColor.Mix(Swatch.WhiteA(Shadows.RimAlphaRest), Swatch.WithAlpha(InkPalette.LightInk, Shadows.RimAlphaRest), Design.OpalShade.IsLight ? 1 : 0);
        return Lerp(rest, Swatch.WithAlpha(c, Shadows.RimAlphaGlow), glow);
    }

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
