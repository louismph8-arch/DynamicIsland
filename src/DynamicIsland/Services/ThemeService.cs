using Microsoft.Win32;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>Mode sombre de Windows (applications + barre des tâches / menu Démarrer).</summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    public static void SetDarkMode(bool dark)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
            {
                int light = dark ? 0 : 1;
                key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
            }

            // Prévient Explorer et les applis ; en arrière-plan car une fenêtre figée peut ralentir l'appel.
            Task.Run(() => SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
                "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out _));
        }
        catch (Exception ex)
        {
            Log.Error("Thème : mode sombre", ex);
        }
    }
}
