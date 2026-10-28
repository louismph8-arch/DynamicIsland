using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace DynamicIsland.Settings;

/// <summary>Page « Profils » : tuiles des profils, réglages du profil choisi, mode Gaming automatique.</summary>
public partial class SettingsWindow
{
    /// <summary>Profil affiché dans l'éditeur (par défaut le profil actif).</summary>
    private string? _editedProfileId;

    private Profile? EditedProfile => ProfileService.Find(_editedProfileId ?? "") ?? ProfileService.Active;

    private void LoadProfiles()
    {
        ProfileService.CaptureActive(); // le profil actif montre ses apps et sa couleur actuelles
        BuildProfileTiles();
        ShowProfileEditor();
        BuildExceptions();
    }

    // ------------------------------------------------------------------ tuiles

    private void BuildProfileTiles()
    {
        ProfileTiles.Children.Clear();
        var edited = EditedProfile;
        foreach (var profile in ProfileService.All)
        {
            bool active = profile == ProfileService.Active;
            var (start, end) = ProfileService.AccentOf(profile);
            var tile = CreateTile(Icons.Glyph(profile.Icon), profile.Name, active ? Loc.T("profiles.active_badge") : null, Paint.Gradient(start, end, 45), selected: profile == edited);
            var id = profile.Id;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                _editedProfileId = id;
                LoadProfiles();
            };
            tile.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) ProfileService.Switch(id); // double-clic = activer
            };
            ProfileTiles.Children.Add(tile);
        }

        var add = CreateTile(Icons.Plus, Loc.T("profiles.new"), null, Paint.SurfaceHover, selected: false);
        add.MouseLeftButtonUp += (_, _) =>
        {
            _editedProfileId = ProfileService.Create().Id;
            LoadProfiles();
            ProfileNameBox.Focus();
            ProfileNameBox.SelectAll();
        };
        ProfileTiles.Children.Add(add);
    }

    private SquircleBorder CreateTile(string glyph, string name, string? badge, Brush circle, bool selected)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new SquircleBorder
        {
            Width = Sizes.ProfileDot,
            Height = Sizes.ProfileDot,
            CornerRadius = Corners.Pill,
            Background = circle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = FontStack.Icons,
                FontSize = IconSize.L,
                Foreground = Paint.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        content.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = TypeRamp.Body,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, Space.S4, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = badge ?? " ",
            FontSize = TypeRamp.Caption,
            Foreground = Paint.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var tile = new SquircleBorder
        {
            Width = Sizes.ProfileTileWidth,
            Height = Sizes.ProfileTileHeight,
            Margin = new Thickness(0, 0, Space.S5, Space.S5),
            Padding = new Thickness(Space.S4),
            CornerRadius = Corners.XL,
            Background = selected ? Paint.SelectedStrong : Paint.Card,
            BorderThickness = Borders.Focus,
            Cursor = Cursors.Hand,
            Child = content,
        };
        if (selected) tile.SetResourceReference(Border.BorderBrushProperty, AppearanceService.AccentBrushKey);
        else tile.BorderBrush = Brushes.Transparent;
        return tile;
    }

    // ------------------------------------------------------------------ éditeur du profil

    private void ShowProfileEditor()
    {
        if (EditedProfile is not { } profile) return;
        bool active = profile == ProfileService.Active;

        ProfileNameBox.Text = profile.Name;
        ActivateProfileButton.Content = Loc.T(active ? "profiles.is_active" : "profiles.activate");
        ActivateProfileButton.IsEnabled = !active;
        DeleteProfileButton.IsEnabled = ProfileService.All.Count > 1;

        ProfileIcons.Children.Clear();
        foreach (var (iconName, label) in Icons.ProfileChoices)
        {
            var button = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Width = Sizes.Choice,
                Height = Sizes.Choice,
                Margin = new Thickness(0, 0, Space.S3, 0),
                ToolTip = Loc.T(label),
                Background = iconName == Icons.Normalize(profile.Icon) ? Paint.SurfaceHover : Brushes.Transparent,
                Content = new TextBlock { Text = Icons.Glyph(iconName), FontFamily = FontStack.Icons, FontSize = IconSize.L },
            };
            button.Click += (_, _) => ProfileService.SetIcon(profile, iconName);
            ProfileIcons.Children.Add(button);
        }

        ProfileSwatches.Children.Clear();
        foreach (var (name, start, end) in AccentPresets)
        {
            AppearanceColors.TryParse(start, out var a);
            AppearanceColors.TryParse(end, out var b);
            bool selected = start.Equals(profile.AccentStart, StringComparison.OrdinalIgnoreCase)
                            && end.Equals(profile.AccentEnd, StringComparison.OrdinalIgnoreCase);
            var swatch = new Button
            {
                Style = (Style)FindResource("RoundButton"),
                Width = Sizes.Choice,
                Height = Sizes.Choice,
                Margin = new Thickness(0, 0, Space.S3, 0),
                ToolTip = name,
                Background = selected ? Paint.SurfaceHover : Brushes.Transparent,
                Content = new Ellipse { Width = Sizes.ChoiceDot, Height = Sizes.ChoiceDot, Fill = Paint.Gradient(a, b, 45) },
            };
            swatch.Click += (_, _) => ProfileService.SetAccent(profile, start, end);
            ProfileSwatches.Children.Add(swatch);
        }

        ProfileWallpaperText.Text = profile.Wallpaper is { } wallpaper
            ? File.Exists(wallpaper) ? Path.GetFileName(wallpaper) : Loc.F("profiles.wallpaper.missing", Path.GetFileName(wallpaper))
            : Loc.T("profiles.wallpaper.original");
        ResetWallpaperButton.IsEnabled = profile.Wallpaper != null;

        var apps = profile.Pinned.Select(p => p.IsFolder ? Loc.F("profiles.apps.folder", p.Name, p.Items!.Count) : p.Name).ToList();
        ProfileAppsText.Text = (apps.Count == 0 ? Loc.T("profiles.apps.none") : string.Join(" · ", apps)) + "\n"
            + Loc.T(active ? "profiles.apps.edit" : "profiles.apps.activate_to_edit");
    }

    private void ProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RenameEditedProfile();
    }

    private void ProfileNameBox_LostFocus(object sender, RoutedEventArgs e) => RenameEditedProfile();

    private void RenameEditedProfile()
    {
        if (EditedProfile is { } profile) ProfileService.Rename(profile, ProfileNameBox.Text);
    }

    private void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (EditedProfile is { } profile) ProfileService.Switch(profile.Id);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (EditedProfile is not { } profile) return;
        var answer = MessageBox.Show(this, Loc.F("profiles.delete.confirm", profile.Name),
            Loc.T("profiles.delete.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _editedProfileId = null;
        ProfileService.Delete(profile.Id);
        LoadProfiles();
    }

    private void ChooseWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (EditedProfile is not { } profile) return;
        var dialog = new OpenFileDialog
        {
            Title = Loc.F("profiles.wallpaper.dialog", profile.Name),
            Filter = Loc.T("dialog.images") + "|" + string.Join(";", WallpaperService.ImageExtensions.Select(x => "*" + x)),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) == true) ProfileService.SetWallpaper(profile, dialog.FileName);
    }

    private void ResetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (EditedProfile is { } profile) ProfileService.SetWallpaper(profile, null);
    }

    // ------------------------------------------------------------------ mode Gaming : exceptions

    private void BuildExceptions()
    {
        var exceptions = SettingsStore.Current.Gaming.Exceptions;
        ExceptionChips.Children.Clear();
        foreach (var name in exceptions)
        {
            var remove = new Button
            {
                Style = (Style)FindResource("SmallIconButton"),
                Content = Icons.Close,
                Width = Sizes.TinyButton,
                Height = Sizes.TinyButton,
                FontSize = IconSize.XS,
                Margin = new Thickness(Space.S2, 0, 0, 0),
                ToolTip = Loc.T("gaming.exceptions.remove"),
            };
            remove.Click += (_, _) => SettingsStore.Update(s => s.Gaming.Exceptions.Remove(name));
            var chip = new SquircleBorder
            {
                CornerRadius = Corners.Pill,
                Background = Paint.Control,
                Padding = new Thickness(Space.S5, Space.S1, Space.S1, Space.S1),
                Margin = new Thickness(0, 0, Space.S3, Space.S3),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { new TextBlock { Text = name, FontSize = TypeRamp.Footnote, VerticalAlignment = VerticalAlignment.Center }, remove },
                },
            };
            ExceptionChips.Children.Add(chip);
        }

        var last = GamingMode.LastFullscreenProcess;
        LastFullscreenRow.Visibility = last != null ? Visibility.Visible : Visibility.Collapsed;
        LastFullscreenText.Text = last != null ? Loc.F("gaming.last_fullscreen", last) : "";
        AddLastFullscreenButton.Visibility = last != null && !exceptions.Contains(last) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExceptionBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddException(ExceptionBox.Text);
    }

    private void AddException_Click(object sender, RoutedEventArgs e) => AddException(ExceptionBox.Text);

    private void AddLastFullscreen_Click(object sender, RoutedEventArgs e) => AddException(GamingMode.LastFullscreenProcess);

    private void AddException(string? text)
    {
        var name = GamingSettings.Clean(text);
        ExceptionBox.Text = "";
        if (name.Length == 0 || SettingsStore.Current.Gaming.Exceptions.Contains(name)) return;
        SettingsStore.Update(s => s.Gaming.Exceptions.Add(name));
    }
}
