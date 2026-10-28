using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Services.Remote;

/// <summary>
/// Règle du pare-feu Windows pour la télécommande (6D), ajoutée et retirée par netsh avec la fenêtre administrateur de
/// Windows, et seulement après la confirmation de l'utilisateur : entrées TCP vers Opaline, sur SON port, sur les réseaux
/// « Privé » seulement. Lire si elle existe ne demande pas de droits. Le désinstalleur la retire aussi.
/// </summary>
public static class FirewallRule
{
    public const string Name = "Opaline Remote";

    private static string Netsh => Path.Combine(Environment.SystemDirectory, "netsh.exe");

    /// <summary>La règle est-elle là ? (netsh répond 0 quand il la trouve, 1 sinon)</summary>
    public static bool Exists()
    {
        try
        {
            var start = new ProcessStartInfo(Netsh) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in new[] { "advfirewall", "firewall", "show", "rule", $"name={Name}" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Télécommande : lecture du pare-feu", ex);
            return false;
        }
    }

    /// <summary>Ajoute la règle : vrai = ajoutée, faux = échec, null = l'utilisateur a refusé la fenêtre administrateur.</summary>
    public static Task<bool?> AddAsync(int port)
    {
        var program = Environment.ProcessPath ?? "";
        return RunElevatedAsync(Netsh, $"advfirewall firewall add rule name=\"{Name}\" dir=in action=allow protocol=TCP localport={port} " +
                                $"profile=private program=\"{program}\" enable=yes");
    }

    /// <summary>Retire la règle : vrai = retirée, faux = échec, null = refusé.</summary>
    public static Task<bool?> RemoveAsync() => RunElevatedAsync(Netsh, $"advfirewall firewall delete rule name=\"{Name}\"");

    /// <summary>Commande avec la fenêtre administrateur de Windows : vrai = réussie, faux = échec, null = refusée.</summary>
    internal static async Task<bool?> RunElevatedAsync(string file, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var process = Process.Start(start);
            if (process == null) return false;
            await process.WaitForExitAsync();
            Log.Info($"Télécommande : commande administrateur {Path.GetFileName(file)} → code {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED : « Non » dans la fenêtre administrateur
        {
            Log.Info("Télécommande : fenêtre administrateur refusée");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Télécommande : commande administrateur {Path.GetFileName(file)}", ex);
            return false;
        }
    }
}
