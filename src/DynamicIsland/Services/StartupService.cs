using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Lancement au démarrage de Windows : valeur « DynamicIsland » de HKCU\...\Run (la source de vérité). Windows peut aussi l'avoir
/// coupé à part (Gestionnaire des tâches → Applications de démarrage, ou Paramètres → Applications → Démarrage) dans
/// StartupApproved\Run : la valeur Run existe toujours mais n'est pas lancée — l'interrupteur affichait « activé » à tort.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
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
                    && command.Contains(exe, StringComparison.OrdinalIgnoreCase) && !DisabledByWindows();
            }
            catch (Exception ex)
            {
                Log.Error("Démarrage : lecture", ex);
                return false;
            }
        }
    }

    /// <summary>Premier octet impair (03, 07…) = coupé dans le Gestionnaire des tâches ; pair (02, 06) ou absent = autorisé.</summary>
    private static bool DisabledByWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    /// <summary>Écrit le choix ; renvoie la cause lisible d'un échec (null = fait).</summary>
    public static string? SetEnabled(bool enabled)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, $"\"{exe}\"");
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            // Réactiver ici = ce que fait le Gestionnaire des tâches (« Activer ») ; désactiver = plus rien de nous à cet endroit.
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            if (approved?.GetValue(ValueName) != null)
            {
                if (enabled) approved.SetValue(ValueName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                else approved.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Démarrage : écriture", ex);
            return ex is UnauthorizedAccessException or System.Security.SecurityException ? Loc.T("settings.alert.denied") : ex.Message;
        }
    }
}
