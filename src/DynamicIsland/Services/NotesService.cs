using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>Une note (titre facultatif + texte en Markdown léger).</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Pinned { get; set; }
    /// <summary>Identifiant d'une couleur de <c>Swatch.NoteColors</c> (« » = sans couleur).</summary>
    public string Color { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;

    /// <summary>Titre affiché : le titre, sinon la première ligne non vide du texte (sans « # » ni case à cocher).</summary>
    public string DisplayTitle
    {
        get
        {
            if (Title.Trim().Length > 0) return Title.Trim();
            var first = NoteText.Lines(Text).FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            return NoteText.Plain(first);
        }
    }

    /// <summary>Lignes d'aperçu sous le titre (index dans le texte, pour cocher une case depuis l'île).</summary>
    public IEnumerable<(int Index, string Line)> PreviewLines()
    {
        var lines = NoteText.Lines(Text);
        bool skipFirst = Title.Trim().Length == 0; // la 1re ligne sert déjà de titre
        bool skipped = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            if (skipFirst && !skipped)
            {
                skipped = true;
                continue;
            }
            yield return (i, lines[i]);
        }
    }

    public Note Clone() => (Note)MemberwiseClone();
}

/// <summary>Petits calculs sur le texte d'une note (purs : vérifiés par --self-test).</summary>
public static partial class NoteText
{
    [GeneratedRegex(@"^(?<indent>\s*)[-*+] \[(?<mark>[ xX])\](?<rest>.*)$")]
    private static partial Regex TaskLine();

    public static string[] Lines(string text) => (text ?? "").Replace("\r", "").Split('\n');

    /// <summary>« - [ ] lait » → (vrai, faux, « lait ») ; une ligne normale → (faux, faux, ligne).</summary>
    public static (bool IsTask, bool Done, string Text) Task(string line)
    {
        var match = TaskLine().Match(line);
        return match.Success ? (true, match.Groups["mark"].Value != " ", match.Groups["rest"].Value.Trim()) : (false, false, line);
    }

    /// <summary>Coche / décoche la case de la ligne <paramref name="index"/> (rien si ce n'est pas une case).</summary>
    public static string ToggleTask(string text, int index)
    {
        var lines = Lines(text);
        if (index < 0 || index >= lines.Length) return text;
        var match = TaskLine().Match(lines[index]);
        if (!match.Success) return text;
        var mark = match.Groups["mark"];
        lines[index] = lines[index][..mark.Index] + (mark.Value == " " ? "x" : " ") + lines[index][(mark.Index + 1)..];
        return string.Join("\n", lines);
    }

    /// <summary>Cases cochées / nombre de cases.</summary>
    public static (int Done, int Total) Progress(string text)
    {
        int done = 0, total = 0;
        foreach (var line in Lines(text))
        {
            var task = Task(line);
            if (!task.IsTask) continue;
            total++;
            if (task.Done) done++;
        }
        return (done, total);
    }

    /// <summary>Ligne sans marques Markdown (titre d'une note sans titre).</summary>
    public static string Plain(string line)
    {
        var task = Task(line);
        var text = (task.IsTask ? task.Text : line).Trim().TrimStart('#').Trim();
        if (text.StartsWith("- ") || text.StartsWith("* ")) text = text[2..];
        text = text.Replace("**", "").Replace("`", "");
        return text.Length > 120 ? text[..120] + "…" : text;
    }
}

/// <summary>
/// Contenu du bloc-notes (sans Windows : testable) : 500 notes au plus (jamais une note effacée toute seule : on refuse
/// d'en créer une de plus), épinglées d'abord puis de la plus récemment modifiée à la plus ancienne.
/// </summary>
public sealed class NotesStore
{
    public const int MaxNotes = 500;
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 100_000;

    private readonly object _lock = new();
    private readonly List<Note> _notes = [];

    /// <summary>Quelque chose a changé (n'importe quel thread).</summary>
    public event Action? Changed;

    /// <summary>Copies des notes, dans l'ordre d'affichage.</summary>
    public List<Note> Items
    {
        get
        {
            lock (_lock) return _notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.Modified).Select(n => n.Clone()).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _notes.Count;
        }
    }

    public bool IsFull => Count >= MaxNotes;

    public Note? Get(string id)
    {
        lock (_lock) return _notes.FirstOrDefault(n => n.Id == id)?.Clone();
    }

    /// <summary>Nouvelle note ; null si la limite est atteinte.</summary>
    public Note? Add(string title, string text, bool pinned = false, string color = "")
    {
        Note note;
        lock (_lock)
        {
            if (_notes.Count >= MaxNotes) return null;
            note = new Note { Title = Clip(title, MaxTitleLength), Text = Clip(text, MaxTextLength), Pinned = pinned, Color = color ?? "" };
            _notes.Add(note);
        }
        Changed?.Invoke();
        return note.Clone();
    }

    /// <summary>Titre et texte modifiés (rien si identiques : la date ne bouge pas).</summary>
    public bool Update(string id, string title, string text)
    {
        lock (_lock)
        {
            if (_notes.FirstOrDefault(n => n.Id == id) is not { } note) return false;
            title = Clip(title, MaxTitleLength);
            text = Clip(text, MaxTextLength);
            if (note.Title == title && note.Text == text) return true;
            note.Title = title;
            note.Text = text;
            note.Modified = DateTime.Now;
        }
        Changed?.Invoke();
        return true;
    }

    public void Pin(string id, bool pinned) => Change(id, n => n.Pinned = pinned);

    public void SetColor(string id, string color) => Change(id, n => n.Color = color ?? "");

    /// <summary>Case cochée depuis l'île (la date de modification bouge : la note remonte).</summary>
    public void ToggleTask(string id, int lineIndex) => Change(id, n =>
    {
        var text = NoteText.ToggleTask(n.Text, lineIndex);
        if (text == n.Text) return;
        n.Text = text;
        n.Modified = DateTime.Now;
    });

    public bool Delete(string id)
    {
        int removed;
        lock (_lock) removed = _notes.RemoveAll(n => n.Id == id);
        if (removed > 0) Changed?.Invoke();
        return removed > 0;
    }

    private void Change(string id, Action<Note> change)
    {
        lock (_lock)
        {
            if (_notes.FirstOrDefault(n => n.Id == id) is not { } note) return;
            change(note);
        }
        Changed?.Invoke();
    }

    private static string Clip(string? text, int max)
    {
        text ??= "";
        return text.Length > max ? text[..max] : text;
    }

    // ------------------------------------------------------------------ fichier

    public byte[] Serialize()
    {
        lock (_lock) return JsonSerializer.SerializeToUtf8Bytes(_notes, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Load(byte[] json)
    {
        var notes = JsonSerializer.Deserialize<List<Note>>(json) ?? [];
        lock (_lock)
        {
            _notes.Clear();
            foreach (var note in notes.Where(n => n != null && !string.IsNullOrEmpty(n.Id)).DistinctBy(n => n.Id).Take(MaxNotes))
            {
                note.Title = Clip(note.Title, MaxTitleLength);
                note.Text = Clip(note.Text, MaxTextLength);
                note.Color ??= "";
                _notes.Add(note);
            }
        }
        Changed?.Invoke();
    }
}

/// <summary>
/// Bloc-notes d'Opaline : %APPDATA%\DynamicIsland\notes.json (ou notes.bin chiffré par Windows, DPAPI, si l'option est
/// cochée), écriture différée et atomique. Jamais dans le rapport de bug ni les thèmes exportés. Rien ne sort du PC.
/// </summary>
public static class NotesService
{
    private static readonly byte[] Entropy = "Opaline.Notes.v1"u8.ToArray();

    public static NotesStore Store { get; } = new();

    private static DispatcherTimer? _save;
    private static bool _started, _loading;
    private static bool? _encrypted;

    public static string PlainPath => Path.Combine(AppPaths.DataFolder, "notes.json");
    public static string EncryptedPath => Path.Combine(AppPaths.DataFolder, "notes.bin");

    /// <summary>Le fichier n'a pas pu être lu : on n'écrit rien par-dessus (les notes ne sont jamais perdues).</summary>
    public static bool IsBroken { get; private set; }

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Load();
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Save();
        };
        Store.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_loading || _save == null) return;
            _save.Stop();
            _save.Start();
        });
        SettingsStore.Changed += ApplyEncryption;
        _encrypted = SettingsStore.Current.Notes.Encrypt;
        Log.Info($"Notes : {Store.Count} note(s){(SettingsStore.Current.Notes.Encrypt ? ", chiffrées" : "")}");
    }

    public static void Stop()
    {
        if (!_started) return;
        Flush();
        _started = false;
        SettingsStore.Changed -= ApplyEncryption;
    }

    /// <summary>Fermeture de l'app ou d'un éditeur : écriture en attente faite tout de suite.</summary>
    public static void Flush()
    {
        if (_save?.IsEnabled != true) return;
        _save.Stop();
        Save();
    }

    /// <summary>Option « Chiffrer mes notes » changée : le fichier est réécrit dans l'autre format, l'ancien effacé.</summary>
    private static void ApplyEncryption()
    {
        bool encrypt = SettingsStore.Current.Notes.Encrypt;
        if (_encrypted == encrypt) return;
        _encrypted = encrypt;
        _save?.Stop();
        Save();
    }

    private static void Load()
    {
        _loading = true;
        try
        {
            if (File.Exists(EncryptedPath)) Store.Load(Unprotect(File.ReadAllBytes(EncryptedPath)));
            else if (File.Exists(PlainPath)) Store.Load(File.ReadAllBytes(PlainPath));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            IsBroken = true;
            Log.Error("Notes : lecture du fichier (rien ne sera écrit par-dessus)", ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Save()
    {
        if (IsBroken || SettingsStore.IsReadOnly) return;
        try
        {
            AppPaths.EnsureDataFolder();
            bool encrypt = SettingsStore.Current.Notes.Encrypt;
            var json = Store.Serialize();
            ClipboardHistory.WriteAtomic(encrypt ? EncryptedPath : PlainPath, encrypt ? Protect(json) : json);
            var other = encrypt ? PlainPath : EncryptedPath;
            if (File.Exists(other)) File.Delete(other);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Notes : enregistrement", ex);
        }
    }

    public static byte[] Protect(byte[] json) => ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
    public static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
