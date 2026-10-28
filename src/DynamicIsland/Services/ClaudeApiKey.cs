using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>
/// Clé API Anthropic du mini Claude (6D-3) : chiffrée par Windows (DPAPI) pour ce compte Windows seulement, dans
/// %APPDATA%\DynamicIsland\claude-cle.bin. Jamais dans settings.json, ni dans le journal, ni dans le rapport de bug, ni sur
/// GitHub ; un autre compte Windows (ou un autre PC) ne peut pas la lire.
/// </summary>
public static class ClaudeApiKey
{
    private static readonly byte[] Entropy = "Opaline.ClaudeApiKey"u8.ToArray();

    private static string FilePath => Path.Combine(AppPaths.DataFolder, "claude-cle.bin");

    public static bool Exists => File.Exists(FilePath);

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Mini Claude : lecture de la clé API", ex);
            return null;
        }
    }

    public static void Save(string key)
    {
        AppPaths.EnsureDataFolder();
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser));
        Log.Info("Mini Claude : clé API enregistrée (chiffrée)");
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
            Log.Info("Mini Claude : clé API supprimée");
        }
        catch (IOException ex)
        {
            Log.Error("Mini Claude : suppression de la clé API", ex);
        }
    }

    /// <summary>« sk-ant-api03-…AbCd » : assez pour la reconnaître, pas pour s'en servir.</summary>
    public static string? Masked() => Load() is { Length: > 16 } key ? key[..13] + "…" + key[^4..] : null;

    /// <summary>Forme d'une clé Anthropic (le vrai contrôle est fait par l'API avec « Vérifier »).</summary>
    public static bool LooksValid(string key)
    {
        key = key.Trim();
        return key.StartsWith("sk-ant-", StringComparison.Ordinal) && key.Length is > 30 and < 300 && !key.Any(char.IsWhiteSpace);
    }
}
