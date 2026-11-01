using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Services;

public enum ClipKind { Text, Link, Image }

/// <summary>Un élément de l'historique du presse-papiers.</summary>
public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Image réduite (≤ 512 px), en PNG.</summary>
    public byte[]? Png { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    /// <summary>Empreinte de l'image (pour ne pas la garder deux fois).</summary>
    public string Hash { get; set; } = "";

    private ImageSource? _thumbnail;

    /// <summary>Miniature (décodée une seule fois, à la taille de la liste).</summary>
    [JsonIgnore]
    public ImageSource? Thumbnail => _thumbnail ??= Png == null ? null : ClipboardHistoryStore.Decode(Png, 64);
}

/// <summary>
/// Contenu de l'historique (sans Windows : testable) : sans limite de durée, avec un garde-fou mémoire / disque (2000 textes,
/// 300 images) — seuls les plus anciens NON épinglés partent au-delà. Plus la « proposition » d'un mot de passe copié par un
/// gestionnaire : jamais dans l'historique, gardé en mémoire 60 s le temps de proposer « Garder dans le coffre », puis oublié.
/// </summary>
public sealed class ClipboardHistoryStore
{
    public const int MaxTexts = 2000;
    public const int MaxImages = 300;
    public const int MaxTextLength = 100_000;
    public const int ImageSize = 512;
    public const int MaxSecretLength = 256;

    private readonly object _lock = new();
    private readonly List<ClipItem> _items = [];
    private string? _secret;
    private Timer? _secretTimer;

    /// <summary>Durée de la proposition « Garder dans le coffre » (courte dans l'auto-test).</summary>
    public TimeSpan OfferLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Quelque chose a changé (n'importe quel thread).</summary>
    public event Action? Changed;

    public List<ClipItem> Items
    {
        get
        {
            lock (_lock) return [.. _items];
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    /// <summary>« https://… » sur une seule ligne = lien.</summary>
    public static bool IsLink(string text) =>
        text.Length < 2048 && !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto" or "ftp";

    public bool AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];
        var kind = IsLink(text.Trim()) ? ClipKind.Link : ClipKind.Text;
        if (kind == ClipKind.Link) text = text.Trim();
        lock (_lock)
        {
            var same = _items.FirstOrDefault(i => i.Kind != ClipKind.Image && i.Text == text);
            if (same != null)
            {
                _items.Remove(same); // déjà là : remonte en haut
                same.Time = DateTime.Now;
                _items.Insert(0, same);
            }
            else
            {
                _items.Insert(0, new ClipItem { Kind = kind, Text = text });
                Trim();
            }
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Texte écrit dans Opaline (« + Nouveau », palette « copier … ») : en tête de l'historique, épinglé si demandé. Même texte
    /// déjà là = il remonte (et s'épingle si demandé). Rend une copie de l'élément (null si le texte est vide).
    /// </summary>
    public ClipItem? AddManual(string text, bool pinned)
    {
        if (!AddText(text)) return null;
        ClipItem top;
        lock (_lock)
        {
            top = _items[0];
            if (pinned) top.Pinned = true;
        }
        if (pinned) Changed?.Invoke();
        return top;
    }

    /// <summary>« Modifier » un texte de l'historique : il garde sa place ; un autre élément identique est fusionné.</summary>
    public ClipItem? Edit(string id, string text, bool pinned)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];
        var kind = IsLink(text.Trim()) ? ClipKind.Link : ClipKind.Text;
        if (kind == ClipKind.Link) text = text.Trim();
        ClipItem? item;
        lock (_lock)
        {
            item = _items.FirstOrDefault(i => i.Id == id && i.Kind != ClipKind.Image);
            if (item == null) return null;
            _items.RemoveAll(i => i != item && i.Kind != ClipKind.Image && i.Text == text);
            item.Text = text;
            item.Kind = kind;
            item.Pinned = pinned;
        }
        Changed?.Invoke();
        return item;
    }

    public bool AddImage(byte[] png, int width, int height)
    {
        var hash = Convert.ToHexString(SHA256.HashData(png));
        lock (_lock)
        {
            var same = _items.FirstOrDefault(i => i.Kind == ClipKind.Image && i.Hash == hash);
            if (same != null)
            {
                _items.Remove(same);
                same.Time = DateTime.Now;
                _items.Insert(0, same);
            }
            else
            {
                _items.Insert(0, new ClipItem { Kind = ClipKind.Image, Png = png, Width = width, Height = height, Hash = hash });
                Trim();
            }
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Garde-fou : au-delà des maximums, les plus anciens non épinglés partent (jamais un épinglé).</summary>
    private void Trim()
    {
        foreach (var (images, max) in new[] { (true, MaxImages), (false, MaxTexts) })
        {
            int count = _items.Count(i => (i.Kind == ClipKind.Image) == images);
            for (int index = _items.Count - 1; index >= 0 && count > max; index--)
            {
                var item = _items[index];
                if ((item.Kind == ClipKind.Image) != images || item.Pinned) continue;
                _items.RemoveAt(index);
                count--;
            }
        }
    }

    public void Pin(string id, bool pinned)
    {
        lock (_lock)
        {
            if (_items.FirstOrDefault(i => i.Id == id) is not { } item) return;
            item.Pinned = pinned;
        }
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_lock) _items.RemoveAll(i => i.Id == id);
        Changed?.Invoke();
    }

    /// <summary>« Tout effacer » : les épinglés restent (comme dans Windows), sauf <paramref name="pinnedToo"/>.</summary>
    public void Clear(bool pinnedToo = false)
    {
        lock (_lock) _items.RemoveAll(i => pinnedToo || !i.Pinned);
        Changed?.Invoke();
    }

    public void MoveToTop(string id)
    {
        lock (_lock)
        {
            if (_items.FirstOrDefault(i => i.Id == id) is not { } item) return;
            _items.Remove(item);
            item.Time = DateTime.Now;
            _items.Insert(0, item);
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ mot de passe copié (proposition, jamais gardé)

    /// <summary>Mot de passe proposé pour le coffre (null = rien, ou délai passé).</summary>
    public bool HasSecret
    {
        get
        {
            lock (_lock) return _secret != null;
        }
    }

    /// <summary>Copie sensible : gardée en mémoire le temps de la proposition. Rien ne va dans l'historique.</summary>
    public bool Offer(string secret)
    {
        secret = secret.Trim();
        if (secret.Length is 0 or > MaxSecretLength || secret.Contains('\n')) return false; // un mot de passe tient sur une ligne
        lock (_lock)
        {
            _secret = secret;
            _secretTimer?.Dispose();
            _secretTimer = new Timer(_ => Forget(), null, OfferLifetime, Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>« Garder dans le coffre » : le mot de passe est rendu une fois, puis oublié.</summary>
    public string? TakeSecret()
    {
        string? secret;
        lock (_lock)
        {
            secret = _secret;
            _secret = null;
            _secretTimer?.Dispose();
            _secretTimer = null;
        }
        if (secret != null) Changed?.Invoke();
        return secret;
    }

    /// <summary>Délai passé, session verrouillée, refus : le mot de passe est oublié.</summary>
    public void Forget()
    {
        bool had;
        lock (_lock)
        {
            had = _secret != null;
            _secret = null;
            _secretTimer?.Dispose();
            _secretTimer = null;
        }
        if (had) Changed?.Invoke();
    }

    // ------------------------------------------------------------------ fichier (chiffré par l'appelant)

    public byte[] Serialize()
    {
        lock (_lock) return JsonSerializer.SerializeToUtf8Bytes(_items);
    }

    public void Load(byte[] json)
    {
        var items = JsonSerializer.Deserialize<List<ClipItem>>(json) ?? [];
        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(items.Where(i => i != null && (i.Kind == ClipKind.Image ? i.Png is { Length: > 0 } : !string.IsNullOrEmpty(i.Text))));
            Trim();
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ images

    /// <summary>Image du presse-papiers réduite à 512 px au plus, en PNG.</summary>
    public static (byte[] Png, int Width, int Height) Reduce(BitmapSource image)
    {
        double scale = Math.Min(1, ImageSize / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource small = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(small));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return (stream.ToArray(), small.PixelWidth, small.PixelHeight);
    }

    public static BitmapImage? Decode(byte[] png, int decodeWidth = 0)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// Historique du presse-papiers de l'île : AddClipboardFormatListener sur une fenêtre message-only (aucun sondage), gardé
/// sans durée dans %APPDATA%\DynamicIsland\presse-papiers.bin chiffré par Windows (DPAPI, ce compte seulement) — écriture
/// différée et atomique ; « Garder après redémarrage » décoché = en mémoire seulement, fichier effacé. Jamais gardé : ce que
/// l'appli qui copie marque sensible (gestionnaires de mots de passe), ce que l'app copie elle-même, rien pendant que la
/// session est verrouillée. Rien ne sort du PC.
/// </summary>
public static class ClipboardHistory
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly byte[] Entropy = "Opaline.PressePapiers.v1"u8.ToArray();

    public static ClipboardHistoryStore Store { get; } = new();

    /// <summary>Une copie sensible vient d'arriver (thread de l'interface) : l'île peut proposer « Garder dans le coffre ».</summary>
    public static event Action? SecretOffered;

    private static HwndSource? _window;
    private static DispatcherTimer? _read;
    private static DispatcherTimer? _save;
    private static bool _locked, _started;
    private static int _retries;

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "presse-papiers.bin");

    public static void Start()
    {
        if (_started) return;
        _started = true;
        if (SettingsStore.Current.Clipboard.Persist) LoadFile();
        _read = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _read.Tick += (_, _) =>
        {
            _read.Stop();
            Guarded.Run(Read, "Presse-papiers : lecture");
        };
        _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            SaveFile();
        };
        Store.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (SettingsStore.Current.Clipboard.Persist && _save != null)
            {
                _save.Stop();
                _save.Start();
            }
        });
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SettingsStore.Changed += Apply;
        Apply();
        Log.Info($"Presse-papiers : historique démarré ({Store.Count} éléments)");
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SettingsStore.Changed -= Apply;
        Listen(false);
        _read?.Stop();
        if (_save?.IsEnabled == true)
        {
            _save.Stop();
            SaveFile();
        }
        Store.Forget();
    }

    /// <summary>Fermeture de l'app : écriture en attente faite tout de suite.</summary>
    public static void Flush()
    {
        if (_save?.IsEnabled != true) return;
        _save.Stop();
        SaveFile();
    }

    private static void Apply()
    {
        var settings = SettingsStore.Current.Clipboard;
        Listen(settings.History || settings.OfferSecrets);
        if (!settings.OfferSecrets) Store.Forget();
        if (!settings.Persist && File.Exists(FilePath))
        {
            try
            {
                File.Delete(FilePath);
                Log.Info("Presse-papiers : fichier de l'historique effacé (gardé en mémoire seulement)");
            }
            catch (IOException ex)
            {
                Log.Error("Presse-papiers : effacement du fichier", ex);
            }
        }
        else if (settings.Persist && !File.Exists(FilePath) && Store.Count > 0)
        {
            SaveFile();
        }
    }

    private static void Listen(bool on)
    {
        if (on && _window == null)
        {
            _window = new HwndSource(new HwndSourceParameters("DynamicIsland.Clipboard") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
            _window.AddHook(WndProc);
            if (!AddClipboardFormatListener(_window.Handle)) Log.Info("Presse-papiers : AddClipboardFormatListener refusé");
        }
        else if (!on && _window != null)
        {
            RemoveClipboardFormatListener(_window.Handle);
            _window.Dispose();
            _window = null;
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && _read != null)
        {
            // L'appli qui copie peut encore écrire ses formats : on lit un instant plus tard.
            _retries = 0;
            _read.Stop();
            _read.Start();
        }
        return IntPtr.Zero;
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _locked = true;
            Store.Forget(); // un mot de passe proposé ne survit pas au verrouillage
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _locked = false;
        }
    }

    private static void Read()
    {
        if (_locked || ModuleGuard.IsDisabled(ModuleGuard.ClipboardTab)) return;
        if (SecureClipboard.Sequence == SecureClipboard.OwnSequence) return; // c'est nous (coffre, recopie d'un élément)
        var settings = SettingsStore.Current.Clipboard;
        try
        {
            if (SecureClipboard.CurrentIsSensitive())
            {
                // Gestionnaire de mots de passe : JAMAIS dans l'historique ; au plus une proposition pour le coffre.
                if (settings.OfferSecrets && Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    && Store.Offer(Clipboard.GetText(TextDataFormat.UnicodeText)))
                    SecretOffered?.Invoke();
                return;
            }
            if (!settings.History) return;
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                Store.AddText(Clipboard.GetText(TextDataFormat.UnicodeText));
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var (png, width, height) = ClipboardHistoryStore.Reduce(image);
                Store.AddImage(png, width, height);
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException)
        {
            if (++_retries <= 3) _read?.Start(); // presse-papiers encore ouvert par l'autre appli
        }
    }

    /// <summary>Clic sur un élément : il revient dans le presse-papiers (et remonte en haut).</summary>
    public static bool Copy(ClipItem item)
    {
        object data;
        if (item.Kind == ClipKind.Image)
        {
            if (item.Png == null || ClipboardHistoryStore.Decode(item.Png) is not { } image) return false;
            var image32 = new DataObject();
            image32.SetImage(image);
            data = image32;
        }
        else
        {
            data = new DataObject(DataFormats.UnicodeText, item.Text);
        }
        if (!SecureClipboard.SetOwn(data)) return false;
        Store.MoveToTop(item.Id);
        return true;
    }

    // ------------------------------------------------------------------ fichier chiffré

    public static byte[] Protect(byte[] json) => ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
    public static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);

    private static void LoadFile()
    {
        try
        {
            if (File.Exists(FilePath)) Store.Load(Unprotect(File.ReadAllBytes(FilePath)));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Presse-papiers : lecture de l'historique", ex);
        }
    }

    private static void SaveFile()
    {
        if (!SettingsStore.Current.Clipboard.Persist) return;
        try
        {
            AppPaths.EnsureDataFolder();
            WriteAtomic(FilePath, Protect(Store.Serialize()));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Presse-papiers : enregistrement de l'historique", ex);
        }
    }

    /// <summary>Fichier temporaire puis remplacement : jamais de fichier à moitié écrit.</summary>
    internal static void WriteAtomic(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
