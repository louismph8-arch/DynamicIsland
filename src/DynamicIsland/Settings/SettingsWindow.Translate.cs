using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Page Île → groupe « Traduire » : langue cible (menu), raccourci « Traduire ce que j'ai copié », d'où viennent les traductions.</summary>
public partial class SettingsWindow
{
    private void ShowTranslate()
    {
        var settings = SettingsStore.Current.Translate;
        var target = TranslateService.TargetCode;
        TranslateTargetButton.Content = settings.Target.Length == 0
            ? Loc.F("settings.translate.target.auto", TranslateService.NameOf(target))
            : TranslateService.NameOf(target);
        var hotkey = settings.Hotkey;
        if (!TranslateHotkeyBox.IsKeyboardFocused) TranslateHotkeyBox.Text = hotkey.Length > 0 ? Hotkeys.Display(hotkey) : Loc.T("settings.island.hotkey.none");
        bool error = hotkey.Length > 0 && GlobalHotkey.TranslateError != null;
        TranslateHotkeyStatus.Text = error ? GlobalHotkey.TranslateError : Loc.T("settings.translate.hotkey.help");
        TranslateHotkeyStatus.Foreground = error ? Paint.Timer : Paint.TextSecondary;
    }

    private void TranslateTarget_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TranslateTargetButton, Placement = PlacementMode.Bottom };
        var current = SettingsStore.Current.Translate.Target;
        var auto = new MenuItem
        {
            Header = Loc.F("settings.translate.target.auto", TranslateService.NameOf(Loc.Language)),
            IsCheckable = true, IsChecked = current.Length == 0,
        };
        auto.Click += (_, _) => SettingsStore.Update(s => s.Translate.Target = "");
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        foreach (var language in TranslateService.Languages)
        {
            var item = new MenuItem { Header = language.Name, IsCheckable = true, IsChecked = current.Length > 0 && language.Code == current };
            var code = language.Code;
            item.Click += (_, _) => SettingsStore.Update(s => s.Translate.Target = code);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void DefaultTranslateHotkey_Click(object sender, RoutedEventArgs e) =>
        SettingsStore.Update(s => s.Translate.Hotkey = Hotkeys.DefaultTranslate);
}
