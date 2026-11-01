using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland.Settings;

/// <summary>
/// Page Apparence : intensité du « verre opalin » (refonte O1, appliquée en direct), ouverture de la démo, et thème d'Opaline
/// (refonte O2 : sombre, opale blanche, selon le soleil, comme Windows — bascule en douceur, Windows n'est jamais touché).
/// </summary>
public partial class SettingsWindow
{
    private void OpalDemo_Click(object sender, RoutedEventArgs e) => Design.OpalDemoWindow.Open();

    /// <summary>Coche l'intensité actuelle (choisie aussi dans l'assistant ou la démo).</summary>
    private void ShowOpalStyle()
    {
        var style = SettingsStore.Current.Appearance.OpalStyle;
        bool loading = _loading;
        _loading = true;
        OpalDiscreet.IsChecked = style == OpalStyle.Discreet;
        OpalBalanced.IsChecked = style == OpalStyle.Balanced;
        OpalIntense.IsChecked = style == OpalStyle.Intense;
        _loading = loading;
    }

    private void OpalStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var style = sender == OpalDiscreet ? OpalStyle.Discreet : sender == OpalIntense ? OpalStyle.Intense : OpalStyle.Balanced;
        if (SettingsStore.Current.Appearance.OpalStyle == style) return;
        SettingsStore.Update(s => s.Appearance.OpalStyle = style);
    }

    /// <summary>Coche le thème actuel ; la carte « Auto » donne les heures de bascule du jour (ville de la météo).</summary>
    private void ShowOpalTheme()
    {
        var mode = SettingsStore.Current.Appearance.OpalTheme;
        bool loading = _loading;
        _loading = true;
        OpalThemeDark.IsChecked = mode == OpalThemeMode.Dark;
        OpalThemeLight.IsChecked = mode == OpalThemeMode.Light;
        OpalThemeAuto.IsChecked = mode == OpalThemeMode.Auto;
        OpalThemeWindows.IsChecked = mode == OpalThemeMode.Windows;
        _loading = loading;
        string auto = Loc.T("opal.theme.auto.description");
        try { auto += " " + AutoThemeService.Describe(); } catch (Exception) { }
        OpalThemeAuto.Tag = auto;
    }

    private void OpalTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var mode = sender == OpalThemeLight ? OpalThemeMode.Light
            : sender == OpalThemeAuto ? OpalThemeMode.Auto
            : sender == OpalThemeWindows ? OpalThemeMode.Windows
            : OpalThemeMode.Dark;
        if (SettingsStore.Current.Appearance.OpalTheme == mode) return;
        SettingsStore.Update(s => s.Appearance.OpalTheme = mode);
    }
}
