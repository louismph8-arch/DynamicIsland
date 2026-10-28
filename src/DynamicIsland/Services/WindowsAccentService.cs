using System.Windows.Media;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Option « Couleur d'accent de Windows = celle de l'app ». Écrit ce que l'application Paramètres de Windows
/// écrit (palette de 8 nuances, couleurs du menu Démarrer et de DWM, choix automatique coupé) puis prévient
/// Windows. Réversible : l'état d'origine est sauvé avant le premier changement (<see cref="WindowsBackup"/>,
/// section « Accent ») et remis dès que l'option est décochée ou que l'app est désinstallée.
/// </summary>
public static class WindowsAccentService
{
    private const string Section = "Accent";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string DesktopKey = @"Control Panel\Desktop";

    internal static readonly (string Key, string Name)[] Values =
    [
        (AccentKey, "AccentPalette"), (AccentKey, "AccentColorMenu"), (AccentKey, "StartColorMenu"),
        (DwmKey, "AccentColor"), (DwmKey, "ColorizationColor"), (DwmKey, "ColorizationAfterglow"),
        (DesktopKey, "AutoColorization"),
    ];

    private static Color? _applied;

    /// <summary>Réglages changés : synchronise la couleur, ou remet celle d'origine si l'option est décochée.</summary>
    public static void Apply()
    {
        if (SettingsStore.IsReadOnly || ModuleGuard.IsDisabled(ModuleGuard.Colors)) return; // --snapshot / module arrêté
        try
        {
            if (SettingsStore.Current.Appearance.SyncWindowsAccent)
            {
                Sync(AppearanceService.AccentMid, broadcast: true);
            }
            else if (WindowsBackup.Restore(Section))
            {
                _applied = null;
                WindowsBackup.BroadcastColors();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couleur d'accent de Windows", ex);
        }
    }

    internal static void Sync(Color accent, bool broadcast)
    {
        if (_applied == accent) return;
        WindowsBackup.SaveIfMissing(Section, Values);
        var palette = Palette(accent, CurrentExtra());

        using (var key = WindowsBackup.Root.CreateSubKey(AccentKey))
        {
            key.SetValue("AccentPalette", PaletteBytes(palette), RegistryValueKind.Binary);
            key.SetValue("AccentColorMenu", Abgr(palette[3]), RegistryValueKind.DWord);
            key.SetValue("StartColorMenu", Abgr(palette[4]), RegistryValueKind.DWord);
        }
        using (var key = WindowsBackup.Root.CreateSubKey(DwmKey))
        {
            key.SetValue("AccentColor", Abgr(palette[3]), RegistryValueKind.DWord);
            int colorization = (AutoColor.WindowsColorizationAlpha << 24) | (palette[3].R << 16) | (palette[3].G << 8) | palette[3].B;
            key.SetValue("ColorizationColor", colorization, RegistryValueKind.DWord);
            key.SetValue("ColorizationAfterglow", colorization, RegistryValueKind.DWord);
        }
        using (var key = WindowsBackup.Root.CreateSubKey(DesktopKey))
            key.SetValue("AutoColorization", 0, RegistryValueKind.DWord); // sinon Windows reprend la couleur du fond

        _applied = accent;
        if (broadcast) WindowsBackup.BroadcastColors();
    }

    /// <summary>
    /// Les 8 nuances de Windows : 3 plus claires, l'accent, 3 plus sombres, puis une couleur annexe (gardée).
    /// Écarts de luminosité relevés sur une palette générée par Windows.
    /// </summary>
    internal static Color[] Palette(Color accent, Color extra)
    {
        var (h, s, l) = ColorTools.ToHsl(accent);
        return
        [
            ColorTools.FromHsl(h, s, l + (1 - l) * 0.70),
            ColorTools.FromHsl(h, s, l + (1 - l) * 0.43),
            ColorTools.FromHsl(h, s, l + (1 - l) * 0.13),
            accent,
            ColorTools.FromHsl(h, s, l * 0.91),
            ColorTools.FromHsl(h, s, l * 0.70),
            ColorTools.FromHsl(h, s, l * 0.51),
            extra,
        ];
    }

    /// <summary>8e couleur de la palette actuelle (Windows y met une couleur annexe) ; vert de Windows sinon.</summary>
    private static Color CurrentExtra()
    {
        using var key = WindowsBackup.Root.OpenSubKey(AccentKey);
        return key?.GetValue("AccentPalette") is byte[] { Length: 32 } bytes
            ? Color.FromRgb(bytes[28], bytes[29], bytes[30])
            : Color.FromRgb(0x00, 0xCC, 0x6A);
    }

    internal static byte[] PaletteBytes(Color[] palette)
    {
        var bytes = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            bytes[i * 4] = palette[i].R;
            bytes[i * 4 + 1] = palette[i].G;
            bytes[i * 4 + 2] = palette[i].B;
        }
        return bytes;
    }

    /// <summary>DWORD 0xAABBGGRR, comme Windows le range.</summary>
    private static int Abgr(Color color) => unchecked((int)(0xFF000000u | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R));
}
