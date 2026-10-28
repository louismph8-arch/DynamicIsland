using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Page « Fond d'écran » : galerie du pack, fond du profil, fond dynamique et fond animé.</summary>
public partial class SettingsWindow
{
    /// <summary>Vignettes du pack (dessinées une fois par session, hors du thread de l'interface).</summary>
    private static readonly Dictionary<WallpaperDesign, BitmapSource> Thumbnails = [];
    private static Task? _thumbnailsTask;

    private async Task LoadWallpaperPageAsync()
    {
        WallpaperEngineNotice.Visibility = AnimatedWallpaper.IsWallpaperEngineRunning ? Visibility.Visible : Visibility.Collapsed;
        ShowWallpaperChoice();
        if (Thumbnails.Count < WallpaperArt.Designs.Length)
        {
            _thumbnailsTask ??= Task.Run(() =>
            {
                var images = WallpaperArt.Designs.ToDictionary(d => d.Design,
                    d => WallpaperArt.Render(d.Design, Wallpapers.Night, Wallpapers.ThumbnailWidth, Wallpapers.ThumbnailHeight));
                lock (Thumbnails)
                    foreach (var (design, image) in images) Thumbnails[design] = image;
            });
            try
            {
                await _thumbnailsTask;
            }
            catch (Exception ex)
            {
                Log.Error("Fond d'écran : vignettes", ex);
            }
        }
        BuildGallery();
    }

    private void BuildGallery()
    {
        WallpaperGallery.Children.Clear();
        var chosen = SettingsStore.Current.Wallpaper.Design;
        foreach (var (design, name) in WallpaperArt.Designs)
        {
            var content = new StackPanel();
            content.Children.Add(new SquircleBorder
            {
                Width = Wallpapers.ThumbnailWidth / 2.0,
                Height = Wallpapers.ThumbnailHeight / 2.0,
                CornerRadius = Corners.M,
                Background = Thumbnails.TryGetValue(design, out var image) ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : Paint.Card,
            });
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = TypeRamp.Footnote,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, Space.S3, 0, 0),
            });
            var tile = new SquircleBorder
            {
                Padding = new Thickness(Space.S2),
                Margin = new Thickness(0, 0, Space.S4, Space.S4),
                CornerRadius = Corners.L,
                BorderThickness = Borders.Focus,
                BorderBrush = Brushes.Transparent,
                Background = Paint.HitTarget,
                Cursor = Cursors.Hand,
                ToolTip = Loc.F("wallpaper.choose", name),
                Child = content,
            };
            if (design == chosen) tile.SetResourceReference(Border.BorderBrushProperty, AppearanceService.AccentBrushKey);
            tile.MouseLeftButtonUp += (_, _) => SettingsStore.Update(s => s.Wallpaper.Design = design);
            WallpaperGallery.Children.Add(tile);
        }
    }

    /// <summary>Texte sous la galerie, descriptions et cadre du fond choisi (après chaque changement de réglage).</summary>
    private void ShowWallpaperChoice()
    {
        var settings = SettingsStore.Current.Wallpaper;
        WallpaperChoiceTitle.Text = WallpaperArt.NameOf(settings.Design);
        WallpaperChoiceText.Text = settings.Dynamic
            ? Loc.T("wallpaper.choice.dynamic")
            : Loc.F("wallpaper.choice.static", ProfileService.Active?.Name);
        UseWallpaperButton.IsEnabled = !settings.Dynamic;
        DynamicWallpaperDescription.Text = Loc.T("wallpaper.dynamic.description") + " " + DynamicWallpaperService.Describe() + " " + Loc.T("wallpaper.dynamic.restore");
        AnimatedAllScreensToggle.IsEnabled = settings.Animated;
        if (!AnimatedAuroraEffect.IsAvailable)
        {
            AnimatedWallpaperToggle.IsEnabled = false;
            AnimatedWallpaperDescription.Text = Loc.T("wallpaper.animated.unavailable");
        }
        if (WallpaperPage.Visibility == Visibility.Visible && WallpaperGallery.Children.Count > 0) BuildGallery();
    }

    /// <summary>Dessine le fond choisi à la taille de l'écran principal et le donne au profil actif.</summary>
    private async void UseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileService.Active is not { } profile) return;
        var design = SettingsStore.Current.Wallpaper.Design;
        UseWallpaperButton.IsEnabled = false;
        WallpaperChoiceText.Text = Loc.T("welcome.drawing");
        try
        {
            var path = await Task.Run(() => WallpaperArt.RenderForScreen(design));
            ProfileService.SetWallpaper(profile, path);
        }
        catch (Exception ex)
        {
            Log.Error("Fond d'écran : pack", ex);
        }
        finally
        {
            ShowWallpaperChoice();
        }
    }
}
