using System.Windows;
using System.Windows.Controls.Primitives;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>
/// Correctif urgent 2 : un interrupteur ne doit jamais « ne rien faire » en silence. Bandeau en haut des paramètres (mode sans
/// échec, enregistrement impossible, module arrêté, réglage refusé, fichier abîmé), interrupteurs resynchronisés quand un réglage
/// change ailleurs (dock ↔ barre des tâches depuis le dock, téléphone, import d'un thème…) : sinon le clic suivant sur n'importe quel
/// interrupteur recopiait l'ancienne valeur et annulait ce changement.
/// </summary>
public partial class SettingsWindow
{
    private enum AlertKind { None, Safe, ReadOnly, Save, Apply, Module, Broken }

    private AlertKind _alert;
    private string? _alertDismissed;
    private string? _applyProblem;

    /// <summary>Interrupteurs de <see cref="Setting_Changed"/> : valeur réelle et module qui l'applique (null = l'île elle-même).</summary>
    private (ToggleButton Toggle, Func<AppSettings, bool> Read, string? Module)[] ToggleChecks => _toggleChecks ??=
    [
        (IslandEnabledToggle, s => s.Island.Enabled, null),
        (IslandVerticalTabsToggle, s => s.Island.VerticalTabs, null),
        (IslandFullscreenToggle, s => s.Island.HideInFullscreen, null),
        (IslandGameLineToggle, s => s.Island.GameLine, null),
        (VisualizerToggle, s => s.Island.Visualizer, null),
        (AlbumGlowToggle, s => s.Island.AlbumGlow, null),
        (PrivacyToggle, s => s.Island.PrivacyDots, null),
        (VolumeHudToggle, s => s.Island.VolumeHud, null),
        (FollowMouseToggle, s => s.Island.FollowMouse, null),
        (DiscordToggle, s => s.Discord.Enabled, ModuleGuard.Discord),
        (ClaudeToggle, s => s.Claude.Enabled, ModuleGuard.Claude),
        (WeatherToggle, s => s.Widgets.Weather, null),
        (CpuToggle, s => s.Widgets.Cpu, null),
        (RamToggle, s => s.Widgets.Ram, null),
        (GpuToggle, s => s.Widgets.Gpu, null),
        (NetworkToggle, s => s.Widgets.Network, null),
        (DockEnabledToggle, s => s.Dock.Enabled, ModuleGuard.Dock),
        (PaletteToggle, s => s.Palette.Enabled, ModuleGuard.Palette),
        (HideTaskbarToggle, s => s.Dock.HideTaskbar, ModuleGuard.Dock),
        (TrayIconsToggle, s => s.Dock.ShowTrayIcons, ModuleGuard.Dock),
        // Correctifs 15–16 : la flèche du dock change l'option des icônes du bureau, un glisser du dock flottant son alignement.
        (HideDesktopIconsToggle, s => s.Dock.HideDesktopIcons, ModuleGuard.Status),
        (DockAlignLeft, s => s.Dock.Alignment == Dock.DockAlignment.Left, ModuleGuard.Dock),
        (DockAlignCenter, s => s.Dock.Alignment == Dock.DockAlignment.Center, ModuleGuard.Dock),
        (DockAlignRight, s => s.Dock.Alignment == Dock.DockAlignment.Right, ModuleGuard.Dock),
        (CloseSmallOption, s => s.Dock.CloseSize == Dock.PreviewCloseSize.Small, ModuleGuard.Dock),
        (CloseMediumOption, s => s.Dock.CloseSize == Dock.PreviewCloseSize.Medium, ModuleGuard.Dock),
        (CloseLargeOption, s => s.Dock.CloseSize == Dock.PreviewCloseSize.Large, ModuleGuard.Dock),
        (CloseGrayOption, s => s.Dock.CloseColor == Dock.PreviewCloseColor.Gray, ModuleGuard.Dock),
        (CloseRedOption, s => s.Dock.CloseColor == Dock.PreviewCloseColor.Red, ModuleGuard.Dock),
        (CloseAccentOption, s => s.Dock.CloseColor == Dock.PreviewCloseColor.Accent, ModuleGuard.Dock),
        (MinimizeEffectToggle, s => s.Dock.MinimizeEffect, ModuleGuard.Dock),
        (HarmonizedIconsToggle, s => s.Dock.HarmonizedIcons, ModuleGuard.Dock),
        (TintIconsToggle, s => s.Dock.TintIcons, ModuleGuard.Dock),
        (BarBlurToggle, s => s.Appearance.DockBlur, ModuleGuard.Dock),
        (GamingToggle, s => s.Gaming.Auto, ModuleGuard.Gaming),
        (AutoUpdateToggle, s => s.Updates.CheckAutomatically, ModuleGuard.Updates),
        (BackdropToggle, s => s.Appearance.SystemBackdrop, null),
        (GlassToggle, s => s.Appearance.LiquidGlass && Controls.LiquidGlassEffect.IsAvailable, null),
        (GlassFollowToggle, s => s.Appearance.GlassFollowMouse, null),
        (WallpaperAccentToggle, s => s.Appearance.AccentFromWallpaper, ModuleGuard.Colors),
        (WallpaperTintToggle, s => s.Appearance.TintFromWallpaper, ModuleGuard.Colors),
        (WindowsAccentToggle, s => s.Appearance.SyncWindowsAccent, ModuleGuard.Colors),
        (AutoThemeToggle, s => s.Appearance.AutoTheme, ModuleGuard.Colors),
        (CursorToggle, s => s.Appearance.ThemedCursor, ModuleGuard.Colors),
        (MenuBarToggle, s => s.MenuBar.Enabled, ModuleGuard.MenuBar),
        (MenuBarReserveToggle, s => s.MenuBar.ReserveSpace, ModuleGuard.MenuBar),
        (MenuBarBlurToggle, s => s.MenuBar.Blur, ModuleGuard.MenuBar),
        (DesktopWidgetsToggle, s => s.DesktopWidgets.Enabled, ModuleGuard.Widgets),
        (LockWidgetsToggle, s => s.DesktopWidgets.Locked, ModuleGuard.Widgets),
        (ClockWidgetToggle, s => s.DesktopWidgets[DesktopWidgetKind.Clock].Visible, ModuleGuard.Widgets),
        (WeatherWidgetToggle, s => s.DesktopWidgets[DesktopWidgetKind.Weather].Visible, ModuleGuard.Widgets),
        (MusicWidgetToggle, s => s.DesktopWidgets[DesktopWidgetKind.Music].Visible, ModuleGuard.Widgets),
        (CalendarWidgetToggle, s => s.DesktopWidgets[DesktopWidgetKind.Calendar].Visible, ModuleGuard.Widgets),
        (StatsWidgetToggle, s => s.DesktopWidgets[DesktopWidgetKind.Stats].Visible, ModuleGuard.Widgets),
        (DynamicWallpaperToggle, s => s.Wallpaper.Dynamic, ModuleGuard.Wallpapers),
        (EnforceWallpaperToggle, s => s.Wallpaper.Enforce, ModuleGuard.Wallpapers),
        (AnimatedWallpaperToggle, s => s.Wallpaper.Animated, ModuleGuard.Wallpapers),
        (AnimatedAllScreensToggle, s => s.Wallpaper.AnimatedAllScreens, ModuleGuard.Wallpapers),
    ];
    private (ToggleButton Toggle, Func<AppSettings, bool> Read, string? Module)[]? _toggleChecks;

    private void WatchAlerts()
    {
        SettingsStore.SaveStateChanged += ShowAlert;
        ModuleGuard.ModuleDisabled += OnModuleDisabled;
        Closed += (_, _) =>
        {
            SettingsStore.SaveStateChanged -= ShowAlert;
            ModuleGuard.ModuleDisabled -= OnModuleDisabled;
        };
        ShowAlert();
    }

    private void OnModuleDisabled(string module)
    {
        ShowAlert();
        if (AboutPage.Visibility == Visibility.Visible) ShowSafety();
    }

    /// <summary>Remet chaque interrupteur sur la vraie valeur (changée ailleurs, ou refusée par l'app).</summary>
    private void SyncToggles()
    {
        var settings = SettingsStore.Current;
        bool wasLoading = _loading;
        _loading = true;
        foreach (var (toggle, read, _) in ToggleChecks)
        {
            bool actual = read(settings);
            if (toggle.IsChecked != actual) toggle.IsChecked = actual;
        }
        _loading = wasLoading;
        UpdateValueTexts();
    }

    /// <summary>
    /// Après un clic : le réglage a-t-il vraiment pris ? Sinon l'interrupteur revient à sa vraie place et le bandeau dit pourquoi
    /// (module arrêté après une erreur, erreur en appliquant, valeur refusée).
    /// </summary>
    private void CheckClickedToggle(object sender, bool? intended)
    {
        _applyProblem = null;
        var entry = ToggleChecks.FirstOrDefault(c => ReferenceEquals(c.Toggle, sender));
        if (entry.Toggle == null) return;
        bool actual = entry.Read(SettingsStore.Current);
        if (entry.Module is { } module && ModuleGuard.IsDisabled(module))
            _applyProblem = Loc.F("settings.alert.module.text", ModuleGuard.Name(module));
        else if (SettingsStore.ApplyError is { } error)
            _applyProblem = Loc.F("settings.alert.apply.text", error);
        else if (intended != null && actual != intended)
            _applyProblem = Loc.T("settings.alert.refused.text");
        SyncToggles();
        if (_applyProblem != null) Log.Info($"Paramètres : réglage « {entry.Toggle.Name} » non appliqué ({_applyProblem})");
        _alertDismissed = null;
        ShowAlert();
    }

    /// <summary>Le problème le plus important du moment (ou rien).</summary>
    private void ShowAlert()
    {
        string? module = ModuleGuard.Disabled.FirstOrDefault();
        var (kind, title, text, action) =
            App.SafeMode ? (AlertKind.Safe, Loc.T("settings.alert.safe.title"), Loc.T("settings.alert.safe.text"), Loc.T("safe.restart"))
            : SettingsStore.SavingSuppressed ? (AlertKind.ReadOnly, Loc.T("settings.alert.readonly.title"), Loc.T("settings.alert.readonly.text"), Loc.T("settings.alert.restart"))
            : SettingsStore.SaveError is { } save ? (AlertKind.Save, Loc.T("settings.alert.save.title"), save + " " + Loc.T("settings.alert.save.text"), Loc.T("settings.alert.retry"))
            : _applyProblem is { } apply ? (AlertKind.Apply, Loc.T("settings.alert.apply.title"), apply, module != null ? Loc.T("settings.alert.restart") : null)
            : module != null ? (AlertKind.Module, Loc.T("settings.alert.module.title"), Loc.F("settings.alert.module.text", string.Join(", ", ModuleGuard.Disabled.Select(ModuleGuard.Name))), Loc.T("settings.alert.restart"))
            : SettingsStore.RecoveredFromBrokenFile ? (AlertKind.Broken, Loc.T("settings.alert.broken.title"), Loc.T("settings.alert.broken.text"), Loc.T("settings.alert.backups"))
            : (AlertKind.None, "", "", (string?)null);
        _alert = kind;
        bool dismissed = kind is (AlertKind.Apply or AlertKind.Module or AlertKind.Broken) && _alertDismissed == kind + text;
        SettingsAlert.Visibility = kind == AlertKind.None || dismissed ? Visibility.Collapsed : Visibility.Visible;
        if (SettingsAlert.Visibility != Visibility.Visible) return;
        SettingsAlertTitle.Text = title;
        SettingsAlertText.Text = text;
        SettingsAlertAction.Content = action;
        SettingsAlertAction.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        SettingsAlertFolder.Visibility = kind == AlertKind.Save ? Visibility.Visible : Visibility.Collapsed;
        SettingsAlertClose.Visibility = kind is AlertKind.Apply or AlertKind.Module or AlertKind.Broken ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsAlertAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_alert)
        {
            case AlertKind.Save:
                SettingsStore.SaveAgain();
                ShowAlert();
                break;
            case AlertKind.Broken:
                ShowPage(SettingsPage.About);
                break;
            case AlertKind.Safe or AlertKind.ReadOnly or AlertKind.Apply or AlertKind.Module:
                App.Restart();
                break;
        }
    }

    private void SettingsAlertClose_Click(object sender, RoutedEventArgs e)
    {
        _alertDismissed = _alert + SettingsAlertText.Text;
        if (_alert == AlertKind.Apply) _applyProblem = null;
        ShowAlert();
    }
}
