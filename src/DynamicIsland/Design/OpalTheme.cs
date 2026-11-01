using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Design;

/// <summary>
/// Verre opalin appliqué à toute l'app (refonte O1) : intensité choisie (<c>Appearance.OpalStyle</c>), pinceaux partagés des
/// tuiles et des menus rangés dans les ressources de l'app (utilisés en <c>DynamicResource</c>, donc changés en direct), et
/// prévient les surfaces (<see cref="OpalSkin"/>) quand le style ou la couleur d'accent changent. Rien ne tourne en continu.
/// </summary>
public static class OpalTheme
{
    public const string TileKey = "OpalTileBrush";
    public const string TileHoverKey = "OpalTileHoverBrush";
    public const string TileRimKey = "OpalTileRimBrush";
    public const string PopupKey = "OpalPopupBrush";
    public const string PopupRimKey = "OpalPopupRimBrush";

    private static ImageSource? _grain;
    private static string? _signature;

    public static OpalLook Look { get; private set; } = Opal.Balanced;

    /// <summary>Texture de grain (même image partout, calculée une fois).</summary>
    public static ImageSource Grain => _grain ??= OpalPalette.NoiseImage();

    /// <summary>Style ou accent changés : chaque surface refait ses pinceaux.</summary>
    public static event Action? Changed;

    /// <summary>Reflets animés (bordure qui suit la souris, balayage, lueur des tuiles) : seulement avec le verre, jamais en jeu.</summary>
    public static bool EffectsOn => SettingsStore.Current.Appearance.LiquidGlass && !GamingMode.IsActive;

    /// <summary>Appelé à chaque changement de réglage (AppearanceService.Apply) : ne refait rien si rien n'a bougé.</summary>
    public static void Apply()
    {
        var style = SettingsStore.Current.Appearance.OpalStyle;
        string signature = $"{style}|{AppearanceService.AccentStart}|{AppearanceService.AccentEnd}|{AppearanceService.SurfaceBase}|{SettingsStore.Current.Appearance.Opacity:0.00}";
        if (signature == _signature) return;
        _signature = signature;
        Look = Opal.For(style);
        BuildResources();
        Changed?.Invoke();
    }

    /// <summary>Intensité en sombre, en opale blanche (<see cref="Opal.Light"/>) ou entre les deux pendant la transition.</summary>
    public static OpalLook LookFor(OpalStyle style, double light)
    {
        var dark = Opal.For(style);
        return light <= 0 ? dark : Opal.Blend(dark, Opal.Light(dark), light);
    }

    private static void BuildResources()
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;
        var look = Look;
        Color start = OpalShade.Shown.AccentStart, end = OpalShade.Shown.AccentEnd, white = Swatch.White;
        double tint = look.Tint * Opal.TileTint;

        resources[TileKey] = Gradient(
            Over(OpalPalette.WithOpacity(white, look.MilkTop + Opal.TileLift), OpalPalette.WithOpacity(start, tint)),
            Over(OpalPalette.WithOpacity(white, look.MilkBottom + Opal.TileLift), OpalPalette.WithOpacity(end, tint * 0.7)));
        resources[TileHoverKey] = Gradient(
            Over(OpalPalette.WithOpacity(white, look.MilkTop + Opal.TileLift + Opal.TileHoverLift), OpalPalette.WithOpacity(start, tint * 1.3)),
            Over(OpalPalette.WithOpacity(white, look.MilkBottom + Opal.TileLift + Opal.TileHoverLift), OpalPalette.WithOpacity(end, tint)));

        // Les ressources de l'app sont figées par WPF : la bordure des tuiles garde l'orientation de la lumière au repos.
        resources[TileRimKey] = Iridescent(look.IridAlpha * Opal.TileRim);

        // Menus clic droit : fond presque opaque (lisible sur n'importe quoi) teinté par l'accent, bordure irisée.
        var popup = ((SolidColorBrush)Paint.PopupBackground).Color;
        var surface = Swatch.WithAlpha(AppearanceService.Lerp(popup, AppearanceService.SurfaceBase, 0.5), popup.A);
        resources[PopupKey] = Gradient(
            Over(OpalPalette.WithOpacity(start, look.Tint * Opal.PopupTint), surface),
            Over(OpalPalette.WithOpacity(end, look.Tint * Opal.PopupTint * 0.7), surface));
        var popupRim = Iridescent(look.IridAlpha * Opal.PopupRim);
        popupRim.Freeze();
        resources[PopupRimKey] = popupRim;
    }

    /// <summary>Bordure en dégradé de trois teintes voisines de l'accent (orientée vers la lumière au repos).</summary>
    public static LinearGradientBrush Iridescent(double alpha, Color? accent = null)
    {
        var irid = OpalPalette.Iridescent(accent ?? AppearanceService.ShownAccentMid, Look.IridShift, Look.IridPale, OpalShade.Light);
        var (from, to) = OpalPalette.Direction(Opal.IridBaseAngle);
        return new LinearGradientBrush
        {
            StartPoint = from, EndPoint = to,
            GradientStops =
            {
                new GradientStop(OpalPalette.WithOpacity(irid[0], alpha), 0),
                new GradientStop(OpalPalette.WithOpacity(irid[1], alpha * 0.55), 0.5),
                new GradientStop(OpalPalette.WithOpacity(irid[2], alpha), 1),
            },
        };
    }

    private static Brush Gradient(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    /// <summary>Couleur <paramref name="top"/> posée sur <paramref name="bottom"/> (composition alpha classique).</summary>
    public static Color Over(Color top, Color bottom)
    {
        double ta = top.A / 255.0, ba = bottom.A / 255.0;
        double a = ta + ba * (1 - ta);
        if (a <= 0) return Colors.Transparent;
        byte Channel(byte t, byte b) => (byte)Math.Round((t * ta + b * ba * (1 - ta)) / a);
        return Color.FromArgb((byte)Math.Round(a * 255), Channel(top.R, bottom.R), Channel(top.G, bottom.G), Channel(top.B, bottom.B));
    }
}
