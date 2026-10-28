using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services.Remote;

/// <summary>Ce que la télécommande lit et fait sur le PC (appelé sur le thread de l'interface, sauf <see cref="Wake"/>).</summary>
public interface IRemoteBackend
{
    /// <summary>État affiché sur le téléphone : musique, volume, micro, casque, profils, réglages du PC.</summary>
    JsonObject State(RemoteDevice device);

    /// <summary>Pochette de la musique en cours (PNG), null s'il n'y en a pas.</summary>
    byte[]? Cover();

    /// <summary>Action demandée par le téléphone ; faux si elle est inconnue ou impossible. Les droits sont déjà vérifiés.</summary>
    Task<bool> DoAsync(string action, string? value, RemoteDevice device);

    /// <summary>Applis du dock, et celles du menu Démarrer qui correspondent à la recherche.</summary>
    JsonObject Apps(string? query);

    /// <summary>Icône d'une appli de <see cref="Apps"/> (PNG).</summary>
    byte[]? AppIcon(string id);

    /// <summary>Texte du presse-papiers du PC (contrôle complet).</summary>
    string? Clipboard();

    /// <summary>Carte réseau pour allumer le PC à distance (hors du thread de l'interface).</summary>
    JsonObject? Wake();

    /// <summary>
    /// Pavé tactile, clavier, raccourcis, toucher sur l'aperçu (contrôle complet, déjà vérifié). Renvoie null si c'est fait,
    /// sinon la raison : « gaming » (pas pendant une partie) ou « bad ».
    /// </summary>
    string? Input(JsonObject input);

    /// <summary>Fenêtres ouvertes (la plus au premier plan d'abord).</summary>
    JsonArray Windows();

    /// <summary>Capture de l'écran <paramref name="index"/> en JPEG (hors du thread de l'interface).</summary>
    byte[]? Screen(int index);
}

/// <summary>
/// La vraie télécommande : musique de l'île, volume, micro et casque (ceux de Discord quand il est connecté, sinon ceux
/// de Windows), profils, applis, liens, presse-papiers, réglages de l'écran, minuteur, messages, alimentation.
/// </summary>
public sealed class AppRemoteBackend : IRemoteBackend
{
    private const int CoverSize = 300;
    private const int IconPixels = 96;
    private const int MaxText = 100_000;

    private readonly Func<IslandWindow?> _island;
    private readonly Action<string, string, string?> _notify;
    private readonly VolumeService _speakers = new();
    private readonly Dictionary<string, byte[]> _icons = [];
    private BitmapSource? _coverSource;
    private byte[]? _coverPng;
    private int _coverVersion;

    /// <param name="notify">Annonce dans l'île : icône, texte, deuxième ligne.</param>
    public AppRemoteBackend(Func<IslandWindow?> island, Action<string, string, string?> notify)
    {
        _island = island;
        _notify = notify;
    }

    private MediaService? Media => _island()?.Media;

    public JsonObject State(RemoteDevice device)
    {
        var island = _island();
        var state = new JsonObject
        {
            ["pc"] = Environment.MachineName,
            ["accent"] = new JsonArray(Hex(AppearanceService.AccentStart), Hex(AppearanceService.AccentEnd)),
            ["full"] = device.FullControl,
        };
        if (Media is { HasSession: true } media)
        {
            if (!ReferenceEquals(media.Cover, _coverSource))
            {
                _coverSource = media.Cover;
                _coverPng = null;
                _coverVersion++;
            }
            state["media"] = new JsonObject
            {
                ["title"] = media.Title,
                ["artist"] = media.Artist,
                ["app"] = media.AppName,
                ["playing"] = media.IsPlaying,
                ["canPrev"] = media.CanGoPrevious,
                ["canNext"] = media.CanGoNext,
                ["cover"] = media.Cover != null ? _coverVersion : null,
            };
        }
        bool speakersMuted = false;
        if (_speakers.TryRead(out double level, out speakersMuted))
            state["volume"] = new JsonObject { ["level"] = Math.Round(level, 3), ["muted"] = speakersMuted };

        bool discord = DiscordVoice.IsConnected && DiscordVoice.SelfMuted != null;
        var micMuted = discord ? DiscordVoice.SelfMuted : VolumeService.AllMicrophonesMuted();
        if (micMuted != null) state["mic"] = new JsonObject { ["muted"] = micMuted, ["discord"] = discord };
        state["deaf"] = new JsonObject { ["on"] = discord ? DiscordVoice.SelfDeafened == true : speakersMuted, ["discord"] = discord };

        state["profiles"] = Profiles();
        state["profile"] = ProfileService.Active?.Id;
        if (island?.Brightness is { IsSupported: true } brightness) state["brightness"] = brightness.Current;
        state["dark"] = ThemeService.IsDarkMode();
        state["dock"] = SettingsStore.Current.Dock.Enabled;
        state["screens"] = RemoteScreen.Screens.Count;
        state["primary"] = RemoteScreen.Primary;
        state["playing"] = GamingMode.IsActive;
        state["gaming"] = new JsonObject { ["auto"] = SettingsStore.Current.Gaming.Auto, ["active"] = GamingMode.IsActive, ["game"] = GamingMode.Game };
        if (island?.Timer is { State: not TimerState.Idle } timer)
            state["timer"] = new JsonObject { ["state"] = timer.State.ToString().ToLowerInvariant(), ["remaining"] = (int)timer.Remaining.TotalSeconds };
        return state;
    }

    public byte[]? Cover()
    {
        if (_coverSource == null) return null;
        if (_coverPng != null) return _coverPng;
        BitmapSource image = _coverSource;
        double scale = (double)CoverSize / Math.Max(image.PixelWidth, image.PixelHeight);
        if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        return _coverPng = OpalineLogo.Png(image);
    }

    public async Task<bool> DoAsync(string action, string? value, RemoteDevice device)
    {
        var media = Media;
        var island = _island();
        switch (action)
        {
            case "play" when media?.HasSession == true:
                media.TogglePlayPause();
                return true;
            case "next" when media?.CanGoNext == true:
                media.Next();
                return true;
            case "prev" when media?.CanGoPrevious == true:
                media.Previous();
                return true;
            case "volume" when Number(value) is { } level:
                _speakers.SetLevel(Math.Clamp(level, 0, 1));
                return true;
            case "mute":
                _speakers.ToggleMute();
                return true;
            case "mic":
                if (DiscordVoice.IsConnected && DiscordVoice.SelfMuted is { } discordMuted && await DiscordVoice.SetVoiceAsync(mute: !discordMuted)) return true;
                VolumeService.MuteAllMicrophones(VolumeService.AllMicrophonesMuted() != true);
                return true;
            case "deaf":
                if (DiscordVoice.IsConnected && DiscordVoice.SelfDeafened is { } deafened && await DiscordVoice.SetVoiceAsync(deaf: !deafened)) return true;
                _speakers.ToggleMute();
                return true;
            case "profile" when value != null && ProfileService.Find(value) != null:
                ProfileService.Switch(value);
                return true;
            case "lock":
                Log.Info("Télécommande : PC verrouillé depuis le téléphone");
                return LockWorkStation();
            case "launch" when value != null:
                return Launch(value);
            case "url" when value != null && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && value.Length <= 2048:
                Start(uri.AbsoluteUri, "");
                return true;
            case "clipboard" when value != null && value.Length <= MaxText:
                if (!SetClipboard(value)) return false;
                _notify(Icons.Copy, Loc.T("remote.clipboard.received"), Loc.F("remote.from", device.Name));
                return true;
            case "message" when !string.IsNullOrWhiteSpace(value) && value.Length <= 120:
                _notify(Icons.Smartphone, value.Trim(), Loc.F("remote.from", device.Name));
                return true;
            case "brightness" when island?.Brightness is { IsSupported: true } brightness && Number(value) is { } percent:
                brightness.Set((int)Math.Clamp(Math.Round(percent), 0, 100));
                return true;
            case "dark" when value is "true" or "false":
                ThemeService.SetDarkMode(value == "true");
                return true;
            case "wallpaper":
                NextWallpaper();
                return true;
            case "gaming":
                SettingsStore.Update(s => s.Gaming.Auto = !s.Gaming.Auto);
                return true;
            case "dock" when !App.SafeMode:
                TaskbarSwitch.Toggle();
                return true;
            case "timer" when island != null && Number(value) is { } minutes && minutes is >= 1 and <= 99:
                island.StartTimer(TimeSpan.FromMinutes(minutes));
                return true;
            case "timer-stop" when island != null:
                island.Timer.Reset();
                return true;
            case "window-focus" when FindWindow(value) is { } window:
                WindowActions.Activate(window.Handle);
                return true;
            case "window-min" when FindWindow(value) is { } window:
                WindowActions.Minimize(window.Handle);
                return true;
            case "window-close" when FindWindow(value) is { } window:
                Log.Info("Télécommande : fenêtre fermée depuis le téléphone");
                WindowActions.Close(window.Handle); // l'appli peut encore demander d'enregistrer
                return true;
            case "desktop":
                ToggleDesktop();
                return true;
            case "screenoff":
                Later(() => PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2))); // WM_SYSCOMMAND, SC_MONITORPOWER, éteint
                return true;
            case "sleep" or "restart" or "shutdown":
                // Demandé sans confirmation par l'utilisateur ; Windows laisse quand même les applis protéger un travail non enregistré.
                Log.Info($"Télécommande : {action} demandé par un téléphone");
                Later(() =>
                {
                    if (action == "sleep") SetSuspendState(false, false, false);
                    else Start("shutdown.exe", action == "restart" ? "/r /t 0" : "/s /t 0", hidden: true);
                });
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ applis

    public JsonObject Apps(string? query)
    {
        var result = new JsonObject
        {
            ["pinned"] = new JsonArray(Pinned().Select(p => (JsonNode)new JsonObject { ["id"] = PinId(p), ["name"] = p.Name }).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(query))
        {
            _ = AppCatalog.RefreshAsync();
            result["results"] = new JsonArray(AppCatalog.Search(query.Trim(), 12)
                .Select(a => (JsonNode)new JsonObject { ["id"] = "app:" + a.Id, ["name"] = a.Name }).ToArray());
            result["loading"] = AppCatalog.Apps.Count == 0;
        }
        return result;
    }

    public byte[]? AppIcon(string id)
    {
        if (_icons.TryGetValue(id, out var cached)) return cached;
        ImageSource? icon = null;
        if (Pinned().FirstOrDefault(p => PinId(p) == id) is { } pin) icon = DockItem.FromPin(pin).Icon;
        else if (id.StartsWith("win:", StringComparison.Ordinal) && FindWindow(id[4..]) is { } window)
            icon = window.AppId != null ? IconService.Get(@"shell:AppsFolder\" + window.AppId) : window.ExePath != null ? IconService.Get(window.ExePath) : null;
        else if (id.StartsWith("app:", StringComparison.Ordinal) && AppCatalog.Apps.FirstOrDefault(a => a.Id == id[4..]) is { } app) icon = IconService.Get(app.ParsingName);
        if (icon == null) return null;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawImage(icon, new Rect(0, 0, IconPixels, IconPixels));
        var bitmap = new RenderTargetBitmap(IconPixels, IconPixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        if (_icons.Count > 300) _icons.Clear();
        return _icons[id] = OpalineLogo.Png(bitmap);
    }

    /// <summary>Seulement une appli du dock ou du menu Démarrer : jamais un programme choisi par le téléphone.</summary>
    private static bool Launch(string id)
    {
        if (Pinned().FirstOrDefault(p => PinId(p) == id) is { } pin)
        {
            DockItem.FromPin(pin).Launch();
            return true;
        }
        if (id.StartsWith("app:", StringComparison.Ordinal) && AppCatalog.Apps.FirstOrDefault(a => a.Id == id[4..]) is { } app)
        {
            app.Launch();
            return true;
        }
        return false;
    }

    private static IEnumerable<PinnedApp> Pinned() =>
        SettingsStore.Current.Dock.Pinned.SelectMany(p => p.IsFolder ? p.Items! : [p]).Where(p => !p.IsFolder);

    private static string PinId(PinnedApp pin) =>
        "pin:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin.Path ?? pin.AppId ?? pin.Name)))[..16].ToLowerInvariant();

    // ------------------------------------------------------------------ écran (6D-1c)

    public string? Input(JsonObject input)
    {
        if (GamingMode.IsActive) return "gaming"; // pas de souris ni de touches envoyées au milieu d'une partie
        string Text(string name) => input[name]?.ToString() ?? "";
        double Number(string name) => input[name] is JsonValue v && v.TryGetValue(out double d) ? d : 0;
        switch (Text("t"))
        {
            case "move":
                RemoteInput.Move((int)Math.Round(Number("x")), (int)Math.Round(Number("y")));
                return null;
            case "button":
                return RemoteInput.Button(Text("b"), Text("h")) ? null : "bad";
            case "scroll":
                RemoteInput.Scroll((int)Math.Round(Number("y")), (int)Math.Round(Number("x")));
                return null;
            case "text":
                RemoteInput.Text(Text("v"));
                return null;
            case "key":
                return RemoteInput.PressKey(Text("v")) ? null : "bad";
            case "shortcut":
                return RemoteInput.Shortcut(Text("v")) ? null : "bad";
            case "tap":
                var screens = RemoteScreen.Screens;
                int index = (int)Number("s");
                if (index < 0 || index >= screens.Count) return "bad";
                RemoteInput.ClickAt(screens[index], Number("x"), Number("y"));
                return null;
            default:
                return "bad";
        }
    }

    public JsonArray Windows()
    {
        var foreground = GetForegroundWindow();
        return new JsonArray(WindowTracker.Snapshot().Take(30).Select(w => (JsonNode)new JsonObject
        {
            ["id"] = w.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["title"] = w.Title,
            ["app"] = w.ExePath != null ? System.IO.Path.GetFileNameWithoutExtension(w.ExePath) : "",
            ["active"] = w.Handle == foreground,
            ["min"] = w.IsMinimized,
        }).ToArray());
    }

    public byte[]? Screen(int index) => RemoteScreen.Capture(index);

    /// <summary>Seulement une fenêtre de la liste actuelle (pas n'importe quel identifiant venu du téléphone).</summary>
    private static AppWindow? FindWindow(string? id) =>
        long.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long handle)
            ? WindowTracker.Snapshot().FirstOrDefault(w => w.Handle.ToInt64() == handle)
            : null;

    /// <summary>« Afficher le bureau » (et le remettre), comme le coin de la barre des tâches.</summary>
    private static void ToggleDesktop()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            shell.ToggleDesktop();
            Marshal.FinalReleaseComObject(shell);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Error("Télécommande : afficher le bureau", ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // ------------------------------------------------------------------ PC

    public string? Clipboard()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText()) return "";
            var text = System.Windows.Clipboard.GetText();
            return text.Length <= MaxText ? text : text[..MaxText];
        }
        catch (COMException)
        {
            return null; // presse-papiers occupé par une autre appli
        }
    }

    private static bool SetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    public JsonObject? Wake() => WakeOnLan.Read() is { } wake
        ? new JsonObject
        {
            ["mac"] = wake.Mac,
            ["broadcast"] = wake.Broadcast,
            ["ready"] = wake.Ready,
            ["last"] = WakeOnLan.Describe(WakeOnLan.LastWake(wake.Description)),
        }
        : null;

    /// <summary>Fond d'écran Opaline suivant (dessiné à la taille de l'écran, donné au profil actif).</summary>
    private static void NextWallpaper()
    {
        var designs = Enum.GetValues<WallpaperDesign>();
        var next = designs[(Array.IndexOf(designs, SettingsStore.Current.Wallpaper.Design) + 1) % designs.Length];
        SettingsStore.Update(s => s.Wallpaper.Design = next);
        var ui = Dispatcher.CurrentDispatcher;
        _ = Task.Run(() => WallpaperArt.RenderForScreen(next)).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && ProfileService.Active is { } profile) ui.BeginInvoke(() => ProfileService.SetWallpaper(profile, task.Result));
            else if (task.Exception != null) Log.Error("Télécommande : fond d'écran", task.Exception);
        }, TaskScheduler.Default);
    }

    /// <summary>Un peu plus tard : le téléphone reçoit d'abord sa réponse.</summary>
    private static void Later(Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static void Start(string file, string arguments, bool hidden = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = !hidden, CreateNoWindow = hidden });
        }
        catch (Exception ex)
        {
            Log.Error("Télécommande : lancement", ex);
        }
    }

    private static double? Number(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) ? number : null;

    internal static JsonArray Profiles() =>
        new(ProfileService.All.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["icon"] = Icons.Glyph(p.Icon) }).ToArray());

    internal static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}

/// <summary>
/// Télécommande de démonstration (`--remote-demo`, auto-test) : un faux état en mémoire, aucune action réelle sur le PC
/// (rien n'est verrouillé ni éteint, le volume ne bouge pas) ; les actions reçues sont seulement notées.
/// </summary>
public sealed class DemoRemoteBackend : IRemoteBackend
{
    private bool _playing = true, _muted, _micMuted, _deaf, _dark = true, _gaming = true, _dock = true;
    private double _level = 0.42;
    private int _brightness = 70, _timer;
    private string _profile = "perso";

    public List<string> Actions { get; } = [];

    public JsonObject State(RemoteDevice device)
    {
        var state = new JsonObject
        {
            ["pc"] = "OPALINE-DEMO",
            ["accent"] = new JsonArray(AppRemoteBackend.Hex(Swatch.AccentStart), AppRemoteBackend.Hex(Swatch.AccentEnd)),
            ["full"] = device.FullControl,
            ["media"] = new JsonObject
            {
                ["title"] = "Nuit d'opale",
                ["artist"] = "Les Reflets",
                ["app"] = "Spotify",
                ["playing"] = _playing,
                ["canPrev"] = true,
                ["canNext"] = true,
                ["cover"] = 1,
            },
            ["volume"] = new JsonObject { ["level"] = _level, ["muted"] = _muted },
            ["mic"] = new JsonObject { ["muted"] = _micMuted, ["discord"] = true },
            ["deaf"] = new JsonObject { ["on"] = _deaf, ["discord"] = true },
            ["profiles"] = new JsonArray(
                Profile("perso", Loc.T("remote.demo.perso"), "user"),
                Profile("gaming", "Gaming", "gamepad-2"),
                Profile("etudes", Loc.T("remote.demo.studies"), "graduation-cap")),
            ["profile"] = _profile,
            ["brightness"] = _brightness,
            ["dark"] = _dark,
            ["dock"] = _dock,
            ["screens"] = 2,
            ["primary"] = 0,
            ["playing"] = false,
            ["gaming"] = new JsonObject { ["auto"] = _gaming, ["active"] = false, ["game"] = null },
        };
        if (_timer > 0) state["timer"] = new JsonObject { ["state"] = "running", ["remaining"] = _timer * 60 };
        return state;
    }

    public byte[]? Cover() => OpalineLogo.Png(OpalineLogo.Render(256));

    public Task<bool> DoAsync(string action, string? value, RemoteDevice device)
    {
        Actions.Add(value == null ? action : $"{action}:{value}");
        bool done = true;
        switch (action)
        {
            case "play":
                _playing = !_playing;
                break;
            case "volume" when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double level):
                _level = Math.Clamp(level, 0, 1);
                _muted = false;
                break;
            case "mute":
                _muted = !_muted;
                break;
            case "mic":
                _micMuted = !_micMuted;
                break;
            case "deaf":
                _deaf = !_deaf;
                break;
            case "profile" when value is "perso" or "gaming" or "etudes":
                _profile = value;
                break;
            case "brightness" when int.TryParse(value, out int percent):
                _brightness = Math.Clamp(percent, 0, 100);
                break;
            case "dark":
                _dark = value == "true";
                break;
            case "gaming":
                _gaming = !_gaming;
                break;
            case "dock":
                _dock = !_dock;
                break;
            case "timer" when int.TryParse(value, out int minutes):
                _timer = minutes;
                break;
            case "timer-stop":
                _timer = 0;
                break;
            case "next" or "prev" or "lock" or "launch" or "url" or "clipboard" or "message" or "wallpaper" or "screenoff" or "sleep" or "restart" or "shutdown"
                or "window-focus" or "window-min" or "window-close" or "desktop":
                break;
            default:
                done = false;
                break;
        }
        return Task.FromResult(done);
    }

    public JsonObject Apps(string? query)
    {
        var result = new JsonObject
        {
            ["pinned"] = new JsonArray(new[] { "Spotify", "Discord", "Steam", "Chrome", "Explorateur", "Notes" }
                .Select((name, i) => (JsonNode)new JsonObject { ["id"] = $"pin:demo{i}", ["name"] = name }).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(query))
            result["results"] = new JsonArray(new JsonObject { ["id"] = "app:demo", ["name"] = $"{query.Trim()} (démo)" });
        return result;
    }

    public byte[]? AppIcon(string id) => OpalineLogo.Png(OpalineLogo.Render(96));

    public string? Clipboard() => "Texte d'exemple du presse-papiers";

    public string? Input(JsonObject input)
    {
        Actions.Add("input:" + input["t"]);
        return input["t"]?.ToString() is "move" or "button" or "scroll" or "text" or "key" or "shortcut" or "tap" ? null : "bad";
    }

    public JsonArray Windows() => new(
        new JsonObject { ["id"] = "101", ["title"] = "Opaline — Notes", ["app"] = "notepad", ["active"] = true, ["min"] = false },
        new JsonObject { ["id"] = "102", ["title"] = "YouTube - Google Chrome", ["app"] = "chrome", ["active"] = false, ["min"] = false },
        new JsonObject { ["id"] = "103", ["title"] = "Discord", ["app"] = "Discord", ["active"] = false, ["min"] = true });

    public byte[]? Screen(int index)
    {
        using var bitmap = new System.Drawing.Bitmap(640, 360);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, 640, 360),
                   System.Drawing.Color.FromArgb(124, 92, 255), System.Drawing.Color.FromArgb(76, 201, 240), 30f))
            graphics.FillRectangle(brush, 0, 0, 640, 360);
        return RemoteScreen.Jpeg(bitmap);
    }

    public JsonObject? Wake() => new()
    {
        ["mac"] = "D8:5E:D3:00:00:00",
        ["broadcast"] = "192.168.1.255",
        ["ready"] = false,
        ["last"] = WakeOnLan.Describe(new WakeOnLan.LastWakeInfo(DateTime.Today.AddHours(19.9), true, true)),
    };

    private static JsonObject Profile(string id, string name, string icon) => new() { ["id"] = id, ["name"] = name, ["icon"] = Icons.Glyph(icon) };
}
