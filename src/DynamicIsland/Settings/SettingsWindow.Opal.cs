using System.Windows;

namespace DynamicIsland.Settings;

/// <summary>Page Apparence : intensité du « verre opalin » (refonte O1, appliquée en direct) et ouverture de la démo.</summary>
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
}
