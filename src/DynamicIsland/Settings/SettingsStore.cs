using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using DynamicIsland.Dock;

namespace DynamicIsland.Settings;

/// <summary>
/// Charge et sauvegarde les réglages. Un changement prévient tout de suite les modules
/// (<see cref="Changed"/>) puis est écrit sur disque 400 ms plus tard (un curseur qu'on glisse ne
/// provoque donc pas des dizaines d'écritures). Écriture atomique : un plantage ne corrompt pas le fichier.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static DispatcherTimer? _saveTimer;
    /// <summary>Mode --snapshot : on ne réécrit jamais le fichier (l'app normale tourne peut-être à côté).</summary>
    private static bool _readOnly;

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Mode --snapshot : rien n'est écrit, et aucun état partagé (fond d'écran…) n'est modifié.</summary>
    public static bool IsReadOnly => _readOnly;

    /// <summary>Levé (sur le thread UI) après chaque changement de réglage.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Dernière erreur d'enregistrement (null = tout va bien) : affichée en haut des paramètres au lieu de passer inaperçue
    /// (correctif urgent 2 : un interrupteur qui « ne fait rien » chez un ami = fichier impossible à écrire).
    /// </summary>
    public static string? SaveError { get; private set; }

    /// <summary>Enregistrement réussi ou raté (sur le thread de l'interface).</summary>
    public static event Action? SaveStateChanged;

    /// <summary>Réglages remis d'une sauvegarde : plus rien n'est écrit jusqu'au redémarrage (bandeau des paramètres).</summary>
    public static bool SavingSuppressed { get; private set; }

    /// <summary>Erreur levée par un module en appliquant le DERNIER changement (null = appliqué partout).</summary>
    public static string? ApplyError { get; private set; }

    /// <summary>Réglages illisibles au lancement : repartis des valeurs par défaut (copie gardée en « .illisible »).</summary>
    public static bool RecoveredFromBrokenFile { get; private set; }

    /// <param name="testFile">Mode --snapshot uniquement : réglages de test à lire à la place (jamais écrits).</param>
    /// <param name="readOnly">Ne jamais écrire sur le disque (mode --snapshot).</param>
    public static void Load(string? testFile = null, bool readOnly = false)
    {
        _readOnly = readOnly || testFile != null;
        if (testFile != null)
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(testFile), JsonOptions) ?? new AppSettings();
            Current.Normalize();
            return;
        }
        try
        {
            if (File.Exists(AppPaths.Settings))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), JsonOptions) ?? new AppSettings();
            }
            else if (File.Exists(AppPaths.LegacyDockSettings))
            {
                // Migration depuis l'étape 2 : dock.json devient la section « Dock ».
                var dock = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(AppPaths.LegacyDockSettings), JsonOptions);
                Current = new AppSettings { Dock = dock ?? new DockSettings() };
                SaveNow();
                File.Move(AppPaths.LegacyDockSettings, AppPaths.LegacyDockSettings + ".bak", overwrite: true);
            }
            else
            {
                // Tout premier lancement : l'assistant de bienvenue s'ouvrira.
                Current = new AppSettings { Dock = { Pinned = DockSettings.CreateDefaultPins() }, WelcomeDone = false };
            }
        }
        catch (Exception ex)
        {
            // Fichier illisible : on le met de côté (sans l'écraser) et on repart des valeurs par défaut.
            Log.Error("Réglages : lecture", ex);
            TryKeepBrokenFile();
            RecoveredFromBrokenFile = true;
            Current = new AppSettings { Dock = { Pinned = DockSettings.CreateDefaultPins() } };
        }
        Current.Normalize();
    }

    /// <summary>Modifie les réglages, prévient les modules et programme la sauvegarde.</summary>
    public static void Update(Action<AppSettings> change)
    {
        change(Current);
        Current.Normalize();
        RaiseChanged();
        ScheduleSave();
    }

    /// <summary>Programme une sauvegarde (après un changement fait directement sur <see cref="Current"/>).</summary>
    public static void ScheduleSave()
    {
        if (_readOnly) return;
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveNow();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Écrit tout de suite si une sauvegarde est en attente (à la fermeture de l'app).</summary>
    public static void Flush()
    {
        if (_saveTimer is not { IsEnabled: true }) return;
        _saveTimer.Stop();
        SaveNow();
    }

    /// <summary>Réessaie tout de suite (bouton « Réessayer » du bandeau d'erreur).</summary>
    public static bool SaveAgain()
    {
        if (_readOnly) return false;
        _saveTimer?.Stop();
        SaveNow();
        return SaveError == null;
    }

    private static void SaveNow()
    {
        string? error = null;
        try
        {
            AppPaths.EnsureDataFolder();
            var temp = AppPaths.Settings + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            // Fichier marqué « lecture seule » (copié depuis une clé, synchronisé…) : on enlève juste cet attribut.
            if (File.Exists(AppPaths.Settings) && File.GetAttributes(AppPaths.Settings).HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(AppPaths.Settings, File.GetAttributes(AppPaths.Settings) & ~FileAttributes.ReadOnly);
            File.Move(temp, AppPaths.Settings, overwrite: true);
            SettingsBackup.AfterSave(); // copie automatique (au plus une par heure)
        }
        catch (Exception ex)
        {
            Log.Error("Réglages : sauvegarde", ex);
            error = Describe(ex);
        }
        if (error == SaveError) return;
        SaveError = error;
        SaveStateChanged?.Invoke();
    }

    /// <summary>Cause lisible d'un échec d'écriture.</summary>
    internal static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Loc.F("settings.save.denied", AppPaths.DataFolder),
        IOException io when (io.HResult & 0xFFFF) is 0x70 or 0x27 => Loc.T("settings.save.full"),             // disque plein
        IOException io when (io.HResult & 0xFFFF) is 0x20 or 0x21 => Loc.T("settings.save.locked"),           // fichier pris par une autre appli
        _ => Loc.F("settings.save.other", ex.Message),
    };

    /// <summary>Chaque module est prévenu séparément : si l'un plante, les autres sont quand même mis à jour.</summary>
    private static void RaiseChanged()
    {
        ApplyError = null;
        if (Changed == null) return;
        foreach (var handler in Changed.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                ApplyError = ex.Message;
                // Un module qui plante en appliquant un réglage est arrêté pour la session (les autres continuent).
                if (Services.ModuleGuard.OwnerOf(handler.Method.DeclaringType) is { } module) Services.ModuleGuard.Fail(module, ex);
                else if (!Services.ModuleGuard.TryFail(ex)) Log.Error("Réglages : application d'un changement", ex);
            }
        }
    }

    /// <summary>
    /// Mode sans échec (Maj au lancement, 6A) : réglages par défaut en mémoire, sans modules optionnels, JAMAIS écrits
    /// (le vrai settings.json revient au lancement suivant) ; rien n'est changé dans Windows (comme --snapshot).
    /// </summary>
    public static void LoadSafeDefaults()
    {
        _readOnly = true;
        Current = new AppSettings { WelcomeDone = true };
        Current.Dock.Enabled = false;            // la barre des tâches de Windows reste là
        Current.Palette.Enabled = false;
        Current.Claude.Enabled = false;
        Current.Updates.CheckAutomatically = false;
        Current.Gaming.Auto = false;
        Current.Remote.Enabled = false;          // rien n'écoute sur le réseau
        Current.Rgb.Enabled = false;             // G HUB garde l'éclairage
        Current.Friends.Enabled = false;         // rien n'est partagé
        Current.Normalize();
    }

    /// <summary>Retour à des réglages précédents : plus aucune écriture jusqu'au redémarrage (le fichier remis reste tel quel).</summary>
    public static void SuppressSaving()
    {
        _saveTimer?.Stop();
        _readOnly = true;
        SavingSuppressed = true;
    }

    /// <summary>Les réglages actuels en JSON (rapport de bug, avant nettoyage des données personnelles).</summary>
    internal static string ToJson() => JsonSerializer.Serialize(Current, JsonOptions);

    private static void TryKeepBrokenFile()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                File.Move(AppPaths.Settings, AppPaths.Settings + ".illisible", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tant pis : le fichier sera simplement réécrit.
        }
    }
}
