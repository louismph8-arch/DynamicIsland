using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Tuiles-outils de la grille de l'île (O6, lot B) : chacune rend un vrai service en un clic (les vues détaillées riches
/// viendront en O8). Rien ne tourne hors de la page visible : chaque tuile ne relit que de la mémoire, au plus une fois par
/// seconde (l'état de Windows, quand il en faut, est relu en arrière-plan et rarement). Ce qui touche à Windows (fond d'écran,
/// micros) ne se fait qu'au clic, par les mêmes chemins réversibles que le reste de l'app.
/// </summary>
public static class ToolTiles
{
    public const string Wallpaper = "wallpaper";
    public const string Microphone = "microphone";
    public const string Capture = "capture";
    public const string Color = "color";
    public const string Calculator = "calculator";
    public const string Qr = "qrcode";
    public const string Memo = "memo";
    public const string Phone = "phone";
    public const string Settings = "settings";

    private static readonly TileSize[] All3 = [TileSize.S, TileSize.W, TileSize.L];

    /// <summary>Dans l'ordre de la page « outils » de la disposition par défaut (Fond d'écran W, Micro, Capture, Couleur W…).</summary>
    public static readonly IReadOnlyList<TileDefinition> All =
    [
        new()
        {
            Id = Wallpaper, NameKey = "tiles.wallpaper", Glyph = Icons.Wallpaper, Sizes = All3, DefaultSize = TileSize.W, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new WallpaperFace(Find(Wallpaper), size, context),
            Click = (_, face) => ((WallpaperFace)face).Next(),
            RightClick = (_, _) => SettingsWindow.Open(SettingsPage.Wallpaper),
        },
        new()
        {
            Id = Microphone, NameKey = "tiles.mic", Glyph = Icons.Voice, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new MicFace(Find(Microphone), size, context),
            Click = (_, _) => MicSwitch.Toggle(),
        },
        new()
        {
            Id = Capture, NameKey = "tiles.capture", Glyph = Icons.Capture, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new CaptureFace(Find(Capture), size, context),
            Click = (context, _) => StartCapture(context),
        },
        new()
        {
            Id = Color, NameKey = "tiles.color", Glyph = Icons.Pipette, Sizes = All3, DefaultSize = TileSize.W, Module = ModuleGuard.ColorTile,
            CreateFace = (context, size) => new ColorFace(Find(Color), size, context),
            Click = (context, _) => ColorPicker.Start(context),
        },
        new()
        {
            Id = Calculator, NameKey = "tiles.calc", Glyph = Icons.Calculator, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new SimpleToolFace(Find(Calculator), size, context, ReadCalculator),
            Click = (context, _) => context.OpenPalette("= "), // la calculatrice de la palette (l'île ne prend jamais le clavier)
        },
        new()
        {
            Id = Settings, NameKey = "tiles.settings", Glyph = Icons.Settings, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new SimpleToolFace(Find(Settings), size, context,
                () => new(Icons.Settings, ToolTone.Idle, null, Loc.F("tiles.settings.line", AppInfo.Name))),
            Click = (_, _) => SettingsWindow.Open(),
        },
        new()
        {
            Id = Qr, NameKey = "tiles.qr", Glyph = Icons.QrCode, Sizes = [TileSize.W, TileSize.L], DefaultSize = TileSize.W, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new QrFace(Find(Qr), size, context),
            Click = (_, _) => QrClip.FromClipboard(),
            RightClick = (_, _) => QrClip.CopyImage(),
        },
        new()
        {
            Id = Memo, NameKey = "tiles.memo", Glyph = Icons.Record, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.MemoTile,
            CreateFace = (context, size) => new VoiceMemoFace(Find(Memo), size, context),
            IsAvailable = _ => VoiceMemo.Supported,
            Click = (context, _) => VoiceMemo.Toggle(context),
            RightClick = (_, _) => VoiceMemo.OpenFolder(),
        },
        new()
        {
            Id = Phone, NameKey = "tiles.phone", Glyph = Icons.Smartphone, Sizes = All3, DefaultSize = TileSize.S, Module = ModuleGuard.ToolTiles,
            CreateFace = (context, size) => new PhoneFace(Find(Phone), size, context),
            IsAvailable = _ => SettingsStore.Current.Remote.Enabled, // télécommande coupée : la tuile ne prend pas de place
            Click = (_, _) => SettingsWindow.Open(SettingsPage.Connected),
        },
    ];

    private static TileDefinition Find(string id) => All.First(d => d.Id == id);

    // ------------------------------------------------------------------ calculatrice

    private static ToolState ReadCalculator()
    {
        if (DynamicIsland.Services.Calculator.Last is not { } last) return new(Icons.Calculator, ToolTone.Idle, null, Loc.T("tiles.calc.hint"));
        return new(Icons.Calculator, ToolTone.Idle, DynamicIsland.Services.Calculator.Format(last.Result), last.Expression);
    }

    // ------------------------------------------------------------------ capture

    /// <summary>
    /// Outil Capture d'écran de Windows (lien officiel « ms-screenclip: », comme Win+Maj+S) : rien n'est injecté. L'île se
    /// referme d'abord pour ne pas être sur la capture.
    /// </summary>
    private static void StartCapture(TileContext context)
    {
        context.CollapseIsland();
        var timer = new DispatcherTimer { Interval = CaptureDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Tuile Capture : ouverture de l'outil Capture d'écran", ex);
                context.Notice(Icons.Alert, Loc.T("tiles.capture.error"), null);
            }
        };
        timer.Start();
    }

    /// <summary>Le temps que l'île se referme (ressort de fermeture) avant que Windows fige l'écran.</summary>
    internal static readonly TimeSpan CaptureDelay = TimeSpan.FromMilliseconds(380);

    /// <summary>Lien d'exemple des rendus (page publique d'Opaline).</summary>
    internal const string DemoLink = "https://louismph8-arch.github.io/opaline-app/";

    /// <summary>
    /// Rendus --snapshot et auto-test : états d'exemple en mémoire (micro coupé, fond du pack, couleurs, QR code du site, mémo en
    /// cours, dernier calcul). Rien n'est lu ni changé dans Windows, rien n'est enregistré.
    /// </summary>
    internal static void UseDemo(bool recording = true)
    {
        MicSwitch.UseDemo(true);
        WallpaperThumbnail.UseDemo(WallpaperArt.Render(WallpaperDesign.Prisme, Wallpapers.Night, ToolTileShape.ThumbWidth, ToolTileShape.ThumbWidth * 9 / 16));
        ColorHistory.UseDemo(Swatch.Teal, Swatch.Green, Swatch.Orange, Swatch.Pink, Swatch.Violet, Swatch.Blue);
        QrClip.Encode(DemoLink);
        VoiceMemo.UseDemo(recording, TimeSpan.FromSeconds(42), recording ? null : TimeSpan.FromSeconds(83));
        DynamicIsland.Services.Calculator.Last = (Loc.T("tiles.calc.sample"), 12);
    }

    internal static void ClearDemo()
    {
        MicSwitch.UseDemo(null);
        WallpaperThumbnail.UseDemo(null);
        ColorHistory.UseDemo();
        QrClip.Reset();
        VoiceMemo.ClearDemo();
        DynamicIsland.Services.Calculator.Last = null;
    }
}

/// <summary>Ce que montre une tuile-outil simple : icône, couleur, chiffre, ligne.</summary>
public readonly record struct ToolState(string Glyph, ToolTone Tone, string? Value, string? Line);

/// <summary>Tuile-outil sans accessoire (Calculatrice, Paramètres) : son état est lu par une fonction.</summary>
internal sealed class SimpleToolFace(TileDefinition definition, TileSize size, TileContext context, Func<ToolState> read)
    : ToolFace(definition, size, context)
{
    protected override void Update()
    {
        var state = read();
        SetTone(state.Glyph, state.Tone);
        SetTexts(state.Value, state.Line);
    }
}

/// <summary>Capture : la ligne d'info, et en W / L les touches du raccourci de Windows dessinées en relief.</summary>
internal sealed class CaptureFace : ToolFace
{
    public CaptureFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        if (size == TileSize.S) return;
        var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in new[] { Loc.T("tiles.capture.key.win"), Loc.T("tiles.capture.key.shift"), "S" })
        {
            if (keys.Children.Count > 0 && size == TileSize.W)
            {
                keys.Children.Add(new Border { Width = Space.S1 }); // W : touches serrées, le nom reste lisible à gauche
            }
            else if (keys.Children.Count > 0)
            {
                var plus = new TextBlock { Text = "+", FontSize = TypeRamp.Caption2, Margin = new Thickness(Space.S1, 0, Space.S1, 0), VerticalAlignment = VerticalAlignment.Center };
                plus.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextTertiary));
                keys.Children.Add(plus);
            }
            keys.Children.Add(KeyCap(key));
        }
        SetAccessory(keys);
    }

    /// <summary>Une touche : voile de contrôle, liseré fin, ombre d'un pixel en bas (relief).</summary>
    private static FrameworkElement KeyCap(string text)
    {
        var label = new TextBlock { Text = text, FontSize = TypeRamp.Caption2, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
        var cap = new SquircleBorder { CornerRadius = Corners.XS, Padding = new Thickness(Space.S2, Space.S1, Space.S2, Space.S1), MinWidth = Sizes.TinyButton, BorderThickness = new Thickness(Stroke.Hairline, Stroke.Hairline, Stroke.Hairline, Stroke.Focus), Child = label };
        cap.SetResourceReference(Border.BackgroundProperty, Paint.Key(InkRole.Control));
        cap.SetResourceReference(Border.BorderBrushProperty, Paint.Key(InkRole.Divider));
        return cap;
    }

    protected override void Update()
    {
        SetTone(Icons.Capture, ToolTone.Idle);
        SetTexts(null, Loc.T(TileSize == TileSize.L ? "tiles.capture.line.long" : "tiles.capture.line"));
    }
}
