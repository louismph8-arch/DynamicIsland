using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DynamicIsland.Services;

/// <summary>
/// Prévient quand une clé du registre (ou l'une de ses sous-clés) change : RegNotifyChangeKeyValue sur un fil à part qui dort
/// jusqu'au changement (aucun sondage, rien au repos). Lecture seule. Le rappel arrive sur ce fil : le ramener sur le thread de
/// l'interface (<see cref="Guarded.OnUi"/>).
/// </summary>
public sealed class RegistryWatcher : IDisposable
{
    private const int REG_NOTIFY_CHANGE_NAME = 0x1;
    private const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    private readonly RegistryKey _key;
    private readonly Action _changed;
    private readonly ManualResetEvent _stop = new(false);

    private RegistryWatcher(RegistryKey key, Action changed)
    {
        _key = key;
        _changed = changed;
        new Thread(Run) { IsBackground = true, Name = "Surveillance du registre" }.Start();
    }

    /// <summary>Surveille la clé si elle existe et peut être lue (null sinon, sans erreur).</summary>
    public static RegistryWatcher? TryStart(RegistryKey root, string path, Action changed)
    {
        try
        {
            return root.OpenSubKey(path, RegistryRights.ReadKey) is { } key ? new RegistryWatcher(key, changed) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Error($"Registre : surveillance de {path}", ex);
            return null;
        }
    }

    private void Run()
    {
        using var changedEvent = new AutoResetEvent(false);
        try
        {
            while (true)
            {
                int result = RegNotifyChangeKeyValue(_key.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
                    changedEvent.SafeWaitHandle, true);
                if (result != 0)
                {
                    Log.Info($"Registre : surveillance impossible (code {result})");
                    return;
                }
                if (WaitHandle.WaitAny([changedEvent, _stop]) == 1) return;
                _changed();
            }
        }
        catch (Exception ex)
        {
            Guarded.Report(ex, "Registre : surveillance");
        }
        finally
        {
            _key.Dispose();
            _stop.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            _stop.Set();
        }
        catch (ObjectDisposedException)
        {
            // Déjà arrêtée.
        }
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter, SafeWaitHandle evt, bool asynchronous);
}
