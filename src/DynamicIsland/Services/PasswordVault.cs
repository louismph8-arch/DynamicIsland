using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;
using Windows.Security.Credentials.UI;

namespace DynamicIsland.Services;

/// <summary>Une entrée du coffre, ajoutée par l'utilisateur lui-même.</summary>
public sealed class VaultEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;

    public VaultEntry Clone() => (VaultEntry)MemberwiseClone();
}

/// <summary>Fichier du coffre : JSON chiffré par Windows (DPAPI, ce compte seulement, entropie propre au coffre).</summary>
public static class VaultFile
{
    private static readonly byte[] Entropy = "Opaline.Coffre.v1"u8.ToArray();

    public static List<VaultEntry> Load(string path) =>
        !File.Exists(path) ? []
        : JsonSerializer.Deserialize<List<VaultEntry>>(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)) ?? [];

    public static void Save(string path, List<VaultEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ClipboardHistory.WriteAtomic(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(entries), Entropy, DataProtectionScope.CurrentUser));
    }
}

/// <summary>
/// Petit coffre de mots de passe de l'île : seulement ce que l'utilisateur ajoute lui-même (jamais lu dans un navigateur ni
/// dans le Gestionnaire d'identification de Windows). Fichier %APPDATA%\DynamicIsland\coffre.bin chiffré DPAPI ; jamais
/// dans settings.json, le journal, le rapport de bug ni les thèmes exportés. Déverrouillé par Windows Hello s'il est
/// disponible (sinon DPAPI seul : le compte Windows est le verrou), reverrouillé après 5 min sans s'en servir et au
/// verrouillage de la session ; verrouillé = plus rien en mémoire.
/// </summary>
public static class PasswordVault
{
    public static readonly TimeSpan RelockAfter = TimeSpan.FromMinutes(5);

    private static List<VaultEntry>? _entries;
    private static DispatcherTimer? _relock;
    private static bool _started, _unlocking, _broken;

    /// <summary>Fenêtre à qui rattacher la demande Windows Hello (l'île).</summary>
    public static Func<IntPtr>? Owner { get; set; }

    /// <summary>Windows Hello configuré sur ce PC (vérifié au démarrage) ; null = pas encore su.</summary>
    public static bool? HelloAvailable { get; private set; }

    public static event Action? Changed;

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "coffre.bin");

    public static bool IsLocked => _entries == null;

    /// <summary>Le fichier existe mais n'a pas pu être lu (autre compte, abîmé) : on n'écrit jamais par-dessus.</summary>
    public static bool IsBroken => _broken;

    public static IReadOnlyList<VaultEntry> Entries =>
        _entries?.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];

    public static void Start()
    {
        if (_started) return;
        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _ = CheckHelloAsync();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Lock();
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Application.Current?.Dispatcher.BeginInvoke(Lock);
    }

    private static async Task CheckHelloAsync()
    {
        try
        {
            HelloAvailable = await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception ex)
        {
            Log.Info($"Coffre : Windows Hello indisponible ({ex.GetType().Name})");
            HelloAvailable = false;
        }
        Changed?.Invoke();
    }

    /// <summary>Plus rien en mémoire (les mots de passe déchiffrés sont oubliés).</summary>
    public static void Lock()
    {
        _relock?.Stop();
        if (_entries == null) return;
        _entries = null;
        Changed?.Invoke();
    }

    /// <summary>Déverrouille : Windows Hello s'il est configuré, sinon tout de suite (DPAPI = le compte Windows).</summary>
    public static async Task<bool> UnlockAsync()
    {
        if (!IsLocked)
        {
            Touch();
            return true;
        }
        if (_unlocking) return false;
        _unlocking = true;
        try
        {
            HelloAvailable ??= await IsHelloAvailableAsync();
            if (HelloAvailable == true)
            {
                var owner = Owner?.Invoke() ?? IntPtr.Zero;
                var result = owner != IntPtr.Zero
                    ? await UserConsentVerifierInterop.RequestVerificationForWindowAsync(owner, Loc.T("vault.hello"))
                    : await UserConsentVerifier.RequestVerificationAsync(Loc.T("vault.hello"));
                if (result != UserConsentVerificationResult.Verified)
                {
                    Log.Info($"Coffre : Windows Hello non validé ({result})");
                    return false;
                }
            }
            try
            {
                _entries = VaultFile.Load(FilePath);
                _broken = false;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error("Coffre : lecture", ex);
                _broken = true;
                return false;
            }
            Touch();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Coffre : déverrouillage", ex);
            return false;
        }
        finally
        {
            _unlocking = false;
        }
    }

    private static async Task<bool> IsHelloAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>On vient de s'en servir : le compte à rebours de 5 min repart.</summary>
    public static void Touch()
    {
        if (Application.Current == null) return;
        _relock ??= CreateRelockTimer();
        _relock.Stop();
        _relock.Start();
    }

    private static DispatcherTimer CreateRelockTimer()
    {
        var timer = new DispatcherTimer { Interval = RelockAfter };
        timer.Tick += (_, _) => Lock();
        return timer;
    }

    /// <summary>Ajoute ou remplace une entrée (le coffre doit être déverrouillé).</summary>
    public static bool Save(VaultEntry entry)
    {
        if (_entries == null || _broken) return false;
        var copy = entry.Clone();
        copy.Updated = DateTime.Now;
        var list = _entries.Where(e => e.Id != copy.Id).Append(copy).ToList();
        VaultFile.Save(FilePath, list);
        _entries = list;
        Log.Info($"Coffre : enregistré ({list.Count} entrées)");
        Touch();
        Changed?.Invoke();
        return true;
    }

    public static bool Delete(string id)
    {
        if (_entries == null || _broken) return false;
        var list = _entries.Where(e => e.Id != id).ToList();
        VaultFile.Save(FilePath, list);
        _entries = list;
        Log.Info($"Coffre : entrée supprimée ({list.Count} entrées)");
        Touch();
        Changed?.Invoke();
        return true;
    }

    public static VaultEntry? Find(string id) => _entries?.FirstOrDefault(e => e.Id == id);

    /// <summary>Mot de passe dans le presse-papiers : exclu des historiques ; effacé après 30 s si l'option est active.</summary>
    public static bool CopyPassword(VaultEntry entry)
    {
        Touch();
        return SecureClipboard.SetSecret(entry.Password, SettingsStore.Current.Clipboard.AutoClear);
    }

    public static bool CopyLogin(VaultEntry entry)
    {
        Touch();
        return SecureClipboard.SetOwn(new DataObject(DataFormats.UnicodeText, entry.Login));
    }

    /// <summary>Palette « mot de passe &lt;nom&gt; » : déverrouille au besoin puis copie la meilleure correspondance.</summary>
    public static async Task<VaultEntry?> CopyPasswordByNameAsync(string name)
    {
        if (!await UnlockAsync()) return null;
        var entry = Match(name).FirstOrDefault();
        if (entry != null) CopyPassword(entry);
        return entry;
    }

    /// <summary>Entrées dont le nom ressemble à <paramref name="name"/> (coffre déverrouillé seulement).</summary>
    public static List<VaultEntry> Match(string name) =>
        Entries.Select(e => (Entry: e, Score: AppCatalog.MatchScore(e.Name, name)))
            .Where(m => m.Score > 0).OrderByDescending(m => m.Score).Select(m => m.Entry).ToList();
}
