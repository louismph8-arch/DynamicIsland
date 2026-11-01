using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Micro de la tuile (O6) : celui de Discord quand Discord est relié (comme la télécommande et les automatisations), sinon TOUS
/// les micros de Windows (une appli peut en utiliser un autre que celui « par défaut »). L'état de Windows est relu en
/// arrière-plan, au plus toutes les <see cref="Stale"/> et seulement quand la tuile est affichée ; couper / rétablir se fait au
/// clic seulement, et se défait d'un autre clic.
/// </summary>
internal static class MicSwitch
{
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(3);
    private static bool? _windowsMuted;
    private static DateTime _readAt = DateTime.MinValue;
    private static bool _reading;
    private static bool _writing; // coupure / rétablissement en cours : aucune lecture ne doit écraser l'état voulu
    private static int _generation; // change à chaque clic : une lecture partie avant est ignorée
    private static bool? _demo;

    /// <summary>Coupé ? (null = aucun micro, ou pas encore lu), et d'où vient l'état.</summary>
    public static (bool? Muted, bool Discord) Read()
    {
        if (_demo is { } demo) return (demo, false);
        if (DiscordVoice.IsConnected && DiscordVoice.SelfMuted is { } discord) return (discord, true);
        if (!_reading && !_writing && DateTime.UtcNow - _readAt > Stale) ReadWindowsLater();
        return (_windowsMuted, false);
    }

    private static void ReadWindowsLater()
    {
        _reading = true;
        int generation = _generation;
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _ = Task.Run(VolumeService.AllMicrophonesMuted).ContinueWith(task => ui.BeginInvoke(() =>
        {
            _reading = false;
            if (generation != _generation) return; // un clic est passé entre-temps : cette lecture est périmée
            _readAt = DateTime.UtcNow;
            if (task.IsCompletedSuccessfully) _windowsMuted = task.Result;
        }), TaskScheduler.Default);
    }

    public static void Toggle()
    {
        if (_demo != null) return;
        if (DiscordVoice.IsConnected && DiscordVoice.SelfMuted is { } discord)
        {
            _ = ToggleDiscordAsync(discord);
            return;
        }
        ToggleWindows();
    }

    private static async Task ToggleDiscordAsync(bool muted)
    {
        if (!await DiscordVoice.SetVoiceAsync(mute: !muted)) ToggleWindows(); // Discord refuse : les micros de Windows, comme la télécommande
    }

    private static void ToggleWindows()
    {
        bool mute = _windowsMuted != true;
        _windowsMuted = mute; // tout de suite dans la tuile ; relu APRÈS la coupure pour confirmer
        _generation++;
        _writing = true;
        int generation = _generation;
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _ = Task.Run(() => VolumeService.MuteAllMicrophones(mute)).ContinueWith(_ => ui.BeginInvoke(() =>
        {
            if (generation != _generation) return; // un autre clic a pris la suite
            _writing = false;
            _readAt = DateTime.MinValue; // prochaine mise à jour de la tuile : vraie lecture, la coupure est appliquée
        }), TaskScheduler.Default);
    }

    /// <summary>Rendus --snapshot : état d'exemple, rien n'est lu ni changé.</summary>
    internal static void UseDemo(bool? muted) => _demo = muted;
}

/// <summary>Tuile Micro : icône rouge quand il est coupé ; en W / L, un gros bouton rond.</summary>
internal sealed class MicFace : ToolFace
{
    private readonly SquircleBorder? _button;
    private readonly TextBlock? _buttonGlyph;

    public MicFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        if (size == TileSize.S) return;
        (_button, _buttonGlyph) = RoundButton(size == TileSize.L ? ToolTileShape.ButtonLarge : ToolTileShape.Button);
        SetAccessory(_button);
    }

    protected override void Update()
    {
        var (muted, discord) = MicSwitch.Read();
        string glyph = muted == true ? Icons.MicOff : Icons.Voice;
        var tone = muted == true ? ToolTone.Alert : ToolTone.Idle;
        SetTone(glyph, tone);
        string line = muted switch
        {
            null => Loc.T("tiles.mic.none"),
            true => Loc.F("tiles.mic.state", Loc.T("tiles.mic.off"), Loc.T(discord ? "tiles.mic.discord" : "tiles.mic.windows")),
            false => Loc.F("tiles.mic.state", Loc.T("tiles.mic.on"), Loc.T(discord ? "tiles.mic.discord" : "tiles.mic.windows")),
        };
        SetTexts(null, line);
        if (_button != null && _buttonGlyph != null) PaintButton(_button, _buttonGlyph, glyph, tone);
    }
}
