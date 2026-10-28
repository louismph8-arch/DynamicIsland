using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>Lancement au démarrage de Windows : valeur « DynamicIsland » de HKCU\...\Run (la source de vérité).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    /// <summary>Vrai si Windows lancera CET exe à l'ouverture de session.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string command && Environment.ProcessPath is { } exe
                    && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error("Démarrage : lecture", ex);
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, $"\"{exe}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Démarrage : écriture", ex);
        }
    }
}
