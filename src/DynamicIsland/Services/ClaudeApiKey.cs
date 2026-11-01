using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>
/// Clé API d'un fournisseur du mini assistant (6D-3 : Anthropic ; priorité 3 n° 27 : OpenAI) : chiffrée par Windows (DPAPI)
/// pour ce compte Windows seulement, dans %APPDATA%\DynamicIsland\&lt;fichier&gt;. Jamais dans settings.json, ni dans le
/// journal, ni dans le rapport de bug, ni sur GitHub ; un autre compte Windows (ou un autre PC) ne peut pas la lire.
/// Chaque fournisseur a son fichier et son entropie : une clé ne peut pas être lue à la place de l'autre.
/// </summary>
public sealed class ApiKeyStore(string fileName, string entropy, string logName, int visiblePrefix, Func<string, bool> looksValid)
{
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes(entropy);

    private string FilePath => Path.Combine(AppPaths.DataFolder, fileName);

    public bool Exists => File.Exists(FilePath);

    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), _entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Mini assistant : lecture de la clé API {logName}", ex);
            return null;
        }
    }

    public void Save(string key)
    {
        AppPaths.EnsureDataFolder();
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), _entropy, DataProtectionScope.CurrentUser));
        Log.Info($"Mini assistant : clé API {logName} enregistrée (chiffrée)");
    }

    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
            Log.Info($"Mini assistant : clé API {logName} supprimée");
        }
        catch (IOException ex)
        {
            Log.Error($"Mini assistant : suppression de la clé API {logName}", ex);
        }
    }

    /// <summary>« sk-ant-api03-…AbCd » : assez pour la reconnaître, pas pour s'en servir.</summary>
    public string? Masked() => Load() is { } key && key.Length > visiblePrefix + 8 ? key[..visiblePrefix] + "…" + key[^4..] : null;

    /// <summary>Forme de la clé (le vrai contrôle est fait par l'API avec « Vérifier »).</summary>
    public bool LooksValid(string key) => looksValid(key.Trim());
}

/// <summary>Les deux clés API du mini assistant.</summary>
public static class ApiKeys
{
    /// <summary>Clé Anthropic (« sk-ant-… »), fichier et entropie d'origine (6D-3) : les clés déjà enregistrées restent lisibles.</summary>
    public static readonly ApiKeyStore Anthropic = new("claude-cle.bin", "Opaline.ClaudeApiKey", "Anthropic", 13,
        key => key.StartsWith("sk-ant-", StringComparison.Ordinal) && key.Length is > 30 and < 300 && !key.Any(char.IsWhiteSpace));

    /// <summary>Clé OpenAI (« sk-proj-… », « sk-svcacct-… », ou plus ancienne « sk-… »), mais jamais une clé Anthropic.</summary>
    public static readonly ApiKeyStore OpenAi = new("openai-cle.bin", "Opaline.OpenAiApiKey", "OpenAI", 8,
        key => key.StartsWith("sk-", StringComparison.Ordinal) && !key.StartsWith("sk-ant-", StringComparison.Ordinal)
               && key.Length is > 30 and < 400 && !key.Any(char.IsWhiteSpace));

    /// <summary>Clé du moteur (null pour Claude Code, qui n'en a pas).</summary>
    public static ApiKeyStore? For(Settings.ClaudeEngine engine) => engine switch
    {
        Settings.ClaudeEngine.ApiKey => Anthropic,
        Settings.ClaudeEngine.OpenAi => OpenAi,
        _ => null,
    };
}
