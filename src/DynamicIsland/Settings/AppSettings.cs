using DynamicIsland.Dock;
using DynamicIsland.Themes;

namespace DynamicIsland.Settings;

/// <summary>Tous les réglages de l'app (fichier %APPDATA%\DynamicIsland\settings.json).</summary>
public sealed class AppSettings
{
    public AppearanceSettings Appearance { get; set; } = new();
    public IslandSettings Island { get; set; } = new();
    public WidgetSettings Widgets { get; set; } = new();
    public DockSettings Dock { get; set; } = new();
    public ProfileSettings Profiles { get; set; } = new();
    public GamingSettings Gaming { get; set; } = new();
    public PaletteSettings Palette { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public WallpaperSettings Wallpaper { get; set; } = new();
    public DesktopWidgetSettings DesktopWidgets { get; set; } = new();
    /// <summary>Widgets des potes (7H) : où chacun s'affiche et le site web autorisé (les fichiers sont dans Widgets\).</summary>
    public PluginSettings Plugins { get; set; } = new();
    public MenuBarSettings MenuBar { get; set; } = new();
    public DiscordSettings Discord { get; set; } = new();
    public ClaudeSettings Claude { get; set; } = new();
    public RemoteSettings Remote { get; set; } = new();
    public RgbSettings Rgb { get; set; } = new();
    public FriendsSettings Friends { get; set; } = new();
    public HealthSettings Health { get; set; } = new();
    /// <summary>Presse-papiers de l'île : historique + coffre de mots de passe (le coffre lui-même n'est JAMAIS ici).</summary>
    public ClipboardSettings Clipboard { get; set; } = new();
    /// <summary>Notes de l'île : seulement des choix (les notes elles-mêmes sont dans notes.json, jamais ici).</summary>
    public NotesSettings Notes { get; set; } = new();
    public AutomationSettings Automations { get; set; } = new();
    public FocusSettings Focus { get; set; } = new();
    /// <summary>« Ma semaine » (7F) : suivi de l'activité, désactivé par défaut.</summary>
    public StatsSettings Stats { get; set; } = new();
    /// <summary>Minuteur de l'île (correctif 19) : son de fin, volume, dernière durée « Perso ».</summary>
    public TimerSettings Timer { get; set; } = new();
    /// <summary>Stage Manager (7G) : désactivé par défaut.</summary>
    public StageSettings Stage { get; set; } = new();
    /// <summary>Règles par application pour l'île et le dock (priorité 2, point 13).</summary>
    public List<AppRule> AppRules { get; set; } = [];
    /// <summary>Langue de l'interface (6A) : « auto » (celle de Windows), « fr » ou « en ». Appliquée au démarrage.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>
    /// Assistant de bienvenue déjà vu (5H). Vrai par défaut : des réglages d'avant la 5H (fichier sans ce champ) ne le
    /// montrent pas ; seul un tout premier lancement (pas de fichier) le met à faux.
    /// </summary>
    public bool WelcomeDone { get; set; } = true;

    /// <summary>Remet dans des bornes raisonnables des valeurs modifiées à la main dans le JSON.</summary>
    public void Normalize()
    {
        Appearance ??= new AppearanceSettings();
        Island ??= new IslandSettings();
        Widgets ??= new WidgetSettings();
        Dock ??= new DockSettings();
        Profiles ??= new ProfileSettings();
        Gaming ??= new GamingSettings();
        Palette ??= new PaletteSettings();
        Updates ??= new UpdateSettings();
        Wallpaper ??= new WallpaperSettings();
        DesktopWidgets ??= new DesktopWidgetSettings();
        Plugins ??= new PluginSettings();
        Plugins.Normalize();
        MenuBar ??= new MenuBarSettings();
        Discord ??= new DiscordSettings();
        Claude ??= new ClaudeSettings();
        Remote ??= new RemoteSettings();
        Rgb ??= new RgbSettings();
        Friends ??= new FriendsSettings();
        Health ??= new HealthSettings();
        Automations ??= new AutomationSettings();
        Focus ??= new FocusSettings();
        Stats ??= new StatsSettings();
        Focus.Normalize();
        Timer ??= new TimerSettings();
        Stage ??= new StageSettings();
        Stage.Normalize();
        Notes ??= new NotesSettings();
        Notes.Normalize();
        Timer.Normalize();
        Automations.Normalize();
        Friends.Normalize();
        Claude.Normalize();
        Remote.Normalize();
        if (Language is not ("auto" or Loc.French or Loc.English)) Language = "auto";
        Appearance.Normalize();
        Island.Normalize();
        AppRules = AppRule.NormalizeList(AppRules);
        Widgets.Normalize();
        Dock.Normalize();
        Profiles.Normalize();
        Gaming.Normalize();
        Palette.Normalize();
        Updates.Normalize();
        Wallpaper.Normalize();
        DesktopWidgets.Normalize();
        Discord.ClientId = (Discord.ClientId ?? "").Trim();
    }
}

/// <summary>Intensité du « verre opalin » (refonte O1) : discret, équilibré, intense.</summary>
public enum OpalStyle { Discreet, Balanced, Intense }

/// <summary>Thème d'Opaline (refonte O2) : verre sombre, « opale blanche » claire, selon le soleil, ou comme les applis de Windows.</summary>
public enum OpalThemeMode { Dark, Light, Auto, Windows }

/// <summary>Option d'accessibilité (refonte O3) : comme le réglage de Windows (défaut), toujours, ou jamais.</summary>
public enum AccessibilityChoice { Windows, On, Off }

public sealed class AppearanceSettings
{
    /// <summary>Accent en dégradé (deux couleurs identiques = couleur unie). Par défaut : bleu → violet.</summary>
    public string AccentStart { get; set; } = Swatch.Hex(Swatch.AccentStart);
    public string AccentEnd { get; set; } = Swatch.Hex(Swatch.AccentEnd);
    /// <summary>Thèmes saisonniers sur l'île (6F) : Halloween, Noël, Nouvel An, Saint-Valentin.</summary>
    public bool Seasons { get; set; } = true;
    /// <summary>Intensité du « verre opalin » choisie dans la démo (refonte O1) ; pas encore utilisée par le reste de l'app.</summary>
    public OpalStyle OpalStyle { get; set; } = OpalStyle.Balanced;
    /// <summary>Thème d'Opaline elle-même (île, dock, palette, paramètres…) ; ne change jamais le thème de Windows (≠ AutoTheme).</summary>
    public OpalThemeMode OpalTheme { get; set; } = OpalThemeMode.Dark;
    /// <summary>Halo néon autour de l'île et du dock, de 0 à 1.</summary>
    public double GlowIntensity { get; set; } = 0.4;
    /// <summary>Opacité du fond de l'île (et du dock flottant), de 0,5 à 1.</summary>
    public double Opacity { get; set; } = 0.94;
    /// <summary>Opacité de la teinte de la barre du dock, de 0 à 1 (le flou acrylique est dessous).</summary>
    public double DockOpacity { get; set; } = 0.8;
    /// <summary>Vrai flou acrylique de Windows derrière la barre du dock.</summary>
    public bool DockBlur { get; set; } = true;
    /// <summary>Arrondi des coins, de 0 (presque carré) à 1 (pilule).</summary>
    public double Roundness { get; set; } = 1;
    /// <summary>Taille des icônes du dock, en pixels (DIP).</summary>
    public double DockIconSize { get; set; } = 40;
    /// <summary>Vitesse des animations (1 = normale).</summary>
    public double AnimationSpeed { get; set; } = 1;
    /// <summary>« Réduire les animations » (O3) : ressorts sans rebond, pas de cascade / inclinaison / glissement / balayage. Défaut :
    /// comme « Effets d'animation » de Windows.</summary>
    public AccessibilityChoice ReduceMotion { get; set; } = AccessibilityChoice.Windows;
    /// <summary>« Réduire la transparence » (O3) : verre opalin presque opaque, sans copie floutée ni grain. Défaut : comme
    /// « Effets de transparence » de Windows.</summary>
    public AccessibilityChoice ReduceTransparency { get; set; } = AccessibilityChoice.Windows;
    /// <summary>Effet Mica de Windows 11 sur la fenêtre de paramètres.</summary>
    public bool SystemBackdrop { get; set; } = true;
    /// <summary>Verre liquide sur l'île, la palette et le dock (coupé en mode Gaming).</summary>
    public bool LiquidGlass { get; set; } = true;
    /// <summary>Intensité du verre (réfraction, liseré, reflet), de 0,2 à 1.</summary>
    public double GlassIntensity { get; set; } = Glass.DefaultIntensity;
    /// <summary>La lumière du verre suit le curseur quand il approche (sinon elle vient toujours d'en haut).</summary>
    public bool GlassFollowMouse { get; set; } = true;
    /// <summary>Accent (et halo) tiré du fond d'écran plutôt que la couleur du profil.</summary>
    public bool AccentFromWallpaper { get; set; }
    /// <summary>Avec l'accent du fond d'écran : le fond de l'île et du dock prend aussi une teinte très sombre de l'image.</summary>
    public bool TintFromWallpaper { get; set; } = true;
    /// <summary>Dernières couleurs extraites du fond d'écran (« #RRGGBB »), gardées pour le redémarrage.</summary>
    public string? WallpaperAccentStart { get; set; }
    public string? WallpaperAccentEnd { get; set; }
    public string? WallpaperSurface { get; set; }
    /// <summary>La couleur d'accent de Windows suit celle de l'app (réversible).</summary>
    public bool SyncWindowsAccent { get; set; }
    /// <summary>Windows en clair au lever du soleil, en sombre au coucher (réversible).</summary>
    public bool AutoTheme { get; set; }
    /// <summary>Curseur de souris Dynamic Island aux couleurs d'accent (réversible).</summary>
    public bool ThemedCursor { get; set; }

    public void Normalize()
    {
        if (!AppearanceColors.TryParse(AccentStart, out _)) AccentStart = Swatch.Hex(Swatch.AccentStart);
        if (!AppearanceColors.TryParse(AccentEnd, out _)) AccentEnd = AccentStart;
        GlowIntensity = Math.Clamp(GlowIntensity, 0, 1);
        Opacity = Math.Clamp(Opacity, 0.5, 1);
        if (!Enum.IsDefined(ReduceMotion)) ReduceMotion = AccessibilityChoice.Windows;
        if (!Enum.IsDefined(ReduceTransparency)) ReduceTransparency = AccessibilityChoice.Windows;
        DockOpacity = Math.Clamp(DockOpacity, 0, 1);
        Roundness = Math.Clamp(Roundness, 0, 1);
        DockIconSize = double.IsFinite(DockIconSize) ? Math.Clamp(Math.Round(DockIconSize), DockShape.MinIcon, DockShape.MaxIcon) : 40;
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0.5, 2);
        GlassIntensity = Math.Clamp(GlassIntensity, 0.2, 1);
    }
}

/// <summary>Lecture des couleurs « #RRGGBB » des réglages.</summary>
public static class AppearanceColors
{
    public static bool TryParse(string? text, out System.Windows.Media.Color color)
    {
        color = default;
        text = text?.Trim().TrimStart('#');
        if (text is not { Length: 6 } || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            return false;
        color = System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string ToHex(System.Windows.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>Horloge du monde : ville affichée et fuseau horaire (IANA, « Europe/Paris »).</summary>
public sealed class WorldClock
{
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "";
}

public sealed class IslandSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Délai avant ouverture au survol.</summary>
    public int ExpandDelayMs { get; set; } = 120;
    /// <summary>Délai avant fermeture quand la souris s'en va.</summary>
    public int CollapseDelayMs { get; set; } = 350;
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Pendant une partie : garder un trait discret en haut (sinon rien du tout par-dessus le jeu, 7A).</summary>
    public bool GameLine { get; set; }

    /// <summary>Barres de l'île réduite pilotées par le vumètre de la sortie audio.</summary>
    public bool Visualizer { get; set; } = true;
    /// <summary>Pendant la musique, le halo prend la couleur dominante de la pochette.</summary>
    public bool AlbumGlow { get; set; } = true;
    /// <summary>Point vert (webcam) / orange (micro) quand une appli les utilise.</summary>
    public bool PrivacyDots { get; set; } = true;
    /// <summary>Le volume s'affiche dans l'île quand il change.</summary>
    public bool VolumeHud { get; set; } = true;
    /// <summary>L'île passe sur l'écran où se trouve la souris.</summary>
    public bool FollowMouse { get; set; }
    public int PomodoroFocusMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;
    /// <summary>Place de l'île (5I) : on la change en faisant glisser l'île.</summary>
    public IslandPlacement Placement { get; set; } = new();
    /// <summary>Position verrouillée : glisser l'île ne la déplace plus.</summary>
    public bool LockPosition { get; set; }
    /// <summary>Annonce « façon AirPods » quand un casque, une manette ou une clé USB se connecte (6C).</summary>
    public bool DeviceNotices { get; set; } = true;
    /// <summary>Annonce quand un téléchargement se termine, fichier glissable depuis l'île (6C).</summary>
    public bool DownloadNotices { get; set; } = true;
    /// <summary>Horloges du monde (6C), 4 au plus : onglet « Horloges » de l'île.</summary>
    public List<WorldClock> WorldClocks { get; set; } = [];
    /// <summary>Taille de l'île (priorité 2, point 12) : 0,8 à 1,25 (petite 0,85, moyenne 1, grande 1,15).</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Onglets de l'île : affichés ou non, dans cet ordre (point 12).</summary>
    public List<IslandTabItem> Tabs { get; set; } = IslandTabItem.Defaults();
    /// <summary>Onglets en colonne à gauche du contenu (sinon en rangée en haut).</summary>
    public bool VerticalTabs { get; set; }
    /// <summary>Fixe / Intelligent / Peek, comme le dock (point 13).</summary>
    public IslandMode Mode { get; set; }
    /// <summary>Raccourci « Cacher / afficher l'île » (point 14) ; vide = aucun.</summary>
    public string ToggleHotkey { get; set; } = Services.Hotkeys.DefaultIsland;

    public const double MinScale = 0.8, MaxScale = 1.25;

    public void Normalize()
    {
        Placement ??= new IslandPlacement();
        Placement.Normalize();
        WorldClocks = (WorldClocks ?? []).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.TimeZone))
            .Take(Services.WorldClockService.MaxClocks).ToList();
        ExpandDelayMs = Math.Clamp(ExpandDelayMs, 0, 1000);
        CollapseDelayMs = Math.Clamp(CollapseDelayMs, 100, 2000);
        PomodoroFocusMinutes = Math.Clamp(PomodoroFocusMinutes, 5, 90);
        PomodoroBreakMinutes = Math.Clamp(PomodoroBreakMinutes, 1, 30);
        Scale = double.IsFinite(Scale) ? Math.Clamp(Math.Round(Scale, 2), MinScale, MaxScale) : 1;
        Tabs = IslandTabItem.NormalizeList(Tabs);
        if (!Enum.IsDefined(Mode)) Mode = IslandMode.Fixed;
        ToggleHotkey ??= "";
        if (ToggleHotkey.Length > 0 && !Services.Hotkeys.TryParse(ToggleHotkey, out _, out _)) ToggleHotkey = Services.Hotkeys.DefaultIsland;
    }
}

/// <summary>
/// Moteur du mini assistant : Claude Code (abonnement, par défaut), la clé API Anthropic (6D-3, payée à l'usage) ou la clé API
/// OpenAI (ChatGPT, priorité 3 n° 27). « ApiKey » garde son nom d'origine : les réglages déjà enregistrés restent valables.
/// </summary>
public enum ClaudeEngine
{
    ClaudeCode,
    ApiKey,
    OpenAi,
    /// <summary>L'abonnement ChatGPT de l'utilisateur par l'outil officiel d'OpenAI « codex » (aucune clé).</summary>
    Codex,
}

/// <summary>Mini assistant dans l'île (5J), par Claude Code installé sur le PC ou par une clé API.</summary>
public sealed class ClaudeSettings
{
    /// <summary>Onglet « Claude » / « ChatGPT » de l'île et ligne « Demander à … » de la palette.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Modèle demandé à Claude Code (choix de l'utilisateur : Opus 5.5).</summary>
    public string Model { get; set; } = Services.ClaudeService.DefaultModel;
    public ClaudeEngine Engine { get; set; } = ClaudeEngine.ClaudeCode;
    /// <summary>Modèle avec la clé API (choix de l'utilisateur par défaut : Sonnet 5.5). La clé elle-même n'est jamais ici.</summary>
    public string ApiModel { get; set; } = Services.ClaudeService.ApiDefaultModel;
    /// <summary>Modèle avec la clé OpenAI. La clé elle-même n'est jamais ici.</summary>
    public string OpenAiModel { get; set; } = Services.ClaudeService.OpenAiDefaultModel;
    /// <summary>Modèle demandé à Codex ; vide = le modèle par défaut de la version de Codex installée.</summary>
    public string CodexModel { get; set; } = "";
    /// <summary>Claude Code choisi à la main (claude.exe, claude.cmd ou cli.js) quand la recherche automatique ne le trouve pas ; vide = automatique.</summary>
    public string CodePath { get; set; } = "";

    public void Normalize()
    {
        if (!Services.ClaudeService.OpenAiModels.Any(m => m.Id == OpenAiModel)) OpenAiModel = Services.ClaudeService.OpenAiDefaultModel;
        if (!Services.ClaudeService.Models.Any(m => m.Id == Model)) Model = Services.ClaudeService.DefaultModel;
        if (!Services.ClaudeService.Models.Any(m => m.Id == ApiModel)) ApiModel = Services.ClaudeService.ApiDefaultModel;
        if (!Enum.IsDefined(Engine)) Engine = ClaudeEngine.ClaudeCode;
        // Liste des modèles de Codex variable (selon le compte et la version) : on garde le nom tel quel, sans espaces.
        CodexModel = (CodexModel ?? "").Trim();
        CodePath = (CodePath ?? "").Trim();
        if (CodexModel.Length > 80 || CodexModel.Any(char.IsWhiteSpace)) CodexModel = "";
    }
}

public enum HorizontalAnchor
{
    Center,
    Left,
    Right,
}

public enum VerticalAnchor
{
    Top,
    Bottom,
}

/// <summary>
/// Place de l'île sur son écran (5I), en unités WPF : collée au bord du haut ou du bas (<see cref="Y"/> = écart entre ce
/// bord et la pilule), centrée (<see cref="X"/> = décalage depuis le centre) ou partant du bord gauche / droit
/// (<see cref="X"/> = écart entre ce bord et la pilule). L'île s'ouvre toujours vers l'intérieur de l'écran.
/// Par défaut : en haut au centre de l'écran principal, comme avant.
/// </summary>
public sealed class IslandPlacement
{
    /// <summary>Nom de l'écran pour Windows (« \\.\DISPLAY3 ») ; null = l'écran principal.</summary>
    public string? Screen { get; set; }
    public HorizontalAnchor Horizontal { get; set; } = HorizontalAnchor.Center;
    public double X { get; set; }
    public VerticalAnchor Vertical { get; set; } = VerticalAnchor.Top;
    public double Y { get; set; } = IslandShape.TopMargin;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => Screen == null && Horizontal == HorizontalAnchor.Center && X == 0
                             && Vertical == VerticalAnchor.Top && Y == IslandShape.TopMargin;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Signature => $"{Screen}|{Horizontal}|{X}|{Vertical}|{Y}";

    public void Normalize()
    {
        X = double.IsFinite(X) ? Math.Clamp(X, -8000, 8000) : 0;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, 8000) : IslandShape.TopMargin;
        if (Horizontal != HorizontalAnchor.Center) X = Math.Max(0, X);
    }
}

/// <summary>Palette de commandes (barre de recherche qui sort de l'île).</summary>
public sealed class PaletteSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Raccourci global, format « Ctrl+Space » (voir Services/Hotkeys).</summary>
    public string Hotkey { get; set; } = Services.Hotkeys.Default;

    public void Normalize()
    {
        if (!Services.Hotkeys.TryParse(Hotkey, out _, out _)) Hotkey = Services.Hotkeys.Default;
    }
}

/// <summary>Salon vocal Discord dans l'île (voir Services/DiscordVoice).</summary>
public sealed class DiscordSettings
{
    public bool Enabled { get; set; }
    /// <summary>
    /// « Application ID » de l'appli Discord (créée par Louis, « Public Client »). Par défaut la sienne : ses
    /// potes n'ont rien à saisir, il suffit de les ajouter comme testeurs de l'appli sur le portail.
    /// </summary>
    public string ClientId { get; set; } = DefaultClientId;
    /// <summary>Annonce dans l'île quand quelqu'un rejoint mon salon vocal (correctif 25).</summary>
    public bool JoinNotices { get; set; } = true;

    public const string DefaultClientId = "1563653856645349517";
}

/// <summary>Téléphone appairé à la télécommande : seule l'empreinte (SHA-256) de son jeton est gardée.</summary>
public sealed class RemoteDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime Added { get; set; }
    /// <summary>
    /// Contrôle complet (6D-1b) : veille, redémarrage, arrêt, presse-papiers du PC (et l'écran en 6D-1c). Coupé par défaut,
    /// à activer téléphone par téléphone dans les paramètres.
    /// </summary>
    public bool FullControl { get; set; }
}

/// <summary>
/// Réglages de la carte réseau changés par « Préparer le PC » (allumer à distance, 6D-1b) : leurs valeurs d'avant, pour
/// « Remettre comme avant ».
/// </summary>
public sealed class WakeBackup
{
    public string Adapter { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Valeur d'avant de « *WakeOnMagicPacket » (« 0 », « 1 », ou vide si la carte ne l'avait pas).</summary>
    public string MagicPacket { get; set; } = "";
    /// <summary>La carte pouvait déjà réveiller le PC avant (powercfg wake_armed).</summary>
    public bool WasArmed { get; set; }
}

/// <summary>Télécommande mobile (6D) : page web servie sur le réseau local, désactivée par défaut (voir Services/Remote).</summary>
public sealed class RemoteSettings
{
    public const int MaxDevices = 8;

    public bool Enabled { get; set; }
    public int Port { get; set; } = 48765;
    /// <summary>Transfert de fichiers téléphone ↔ PC (7E) : désactivé par défaut (envois du téléphone ET « Envoyer au téléphone »).</summary>
    public bool FileTransfer { get; set; }
    public List<RemoteDevice> Devices { get; set; } = [];
    /// <summary>La règle du pare-feu a été ajoutée par Opaline (à retirer quand on coupe la télécommande).</summary>
    public bool FirewallRule { get; set; }
    /// <summary>Carte réseau préparée pour le réveil à distance (null = rien n'a été changé).</summary>
    public WakeBackup? Wake { get; set; }

    public void Normalize()
    {
        if (Port is < 1024 or > 65535) Port = 48765;
        Devices = (Devices ?? []).Where(d => d != null && d.TokenHash?.Length == 64 && !string.IsNullOrWhiteSpace(d.Id))
            .Take(MaxDevices).ToList();
    }
}

/// <summary>Fonds d'écran générés (5D) : dessin du pack, fond dynamique selon l'heure, fond animé.</summary>
public sealed class WallpaperSettings
{
    public Services.WallpaperDesign Design { get; set; } = Services.WallpaperDesign.Aurore;
    /// <summary>Le fond de Windows suit l'heure (couleurs du matin, du jour, du coucher de soleil, de la nuit).</summary>
    public bool Dynamic { get; set; }
    /// <summary>Aurore animée derrière les icônes du bureau.</summary>
    public bool Animated { get; set; }
    /// <summary>Fond animé sur tous les écrans (sinon l'écran principal seulement).</summary>
    public bool AnimatedAllScreens { get; set; } = true;
    /// <summary>
    /// Le fond d'Opaline (profil ou fond dynamique) est remis aussitôt si Windows, un thème, Spotlight ou une autre appli le
    /// change (demandé par l'utilisateur, correctif urgent 6). Sans effet si le profil n'a pas de fond.
    /// </summary>
    public bool Enforce { get; set; } = true;

    public void Normalize()
    {
        if (!Enum.IsDefined(Design)) Design = Services.WallpaperDesign.Aurore;
    }
}

/// <summary>Barre de menus façon macOS (5F), en haut de l'écran principal.</summary>
public sealed class MenuBarSettings
{
    public bool Enabled { get; set; }
    /// <summary>Les fenêtres agrandies s'arrêtent sous la barre (espace réservé comme la barre des tâches).</summary>
    public bool ReserveSpace { get; set; } = true;
    /// <summary>Vrai flou acrylique de Windows derrière la barre.</summary>
    public bool Blur { get; set; } = true;
}

public enum DesktopWidgetKind { Clock, Weather, Music, Calendar, Stats }

/// <summary>Widgets du bureau (5E) : un emplacement par widget (position en DIP, null = place par défaut).</summary>
public sealed class DesktopWidgetSettings
{
    public bool Enabled { get; set; }
    /// <summary>Verrouillés : on ne peut plus les déplacer ni les redimensionner (les boutons marchent toujours).</summary>
    public bool Locked { get; set; }
    public List<DesktopWidgetPlacement> Items { get; set; } = [];

    public DesktopWidgetPlacement this[DesktopWidgetKind kind] => Items.First(i => i.Kind == kind);

    public void Normalize()
    {
        Items = (Items ?? []).Where(i => i != null && Enum.IsDefined(i.Kind)).GroupBy(i => i.Kind).Select(g => g.First()).ToList();
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>())
            if (Items.All(i => i.Kind != kind)) Items.Add(new DesktopWidgetPlacement { Kind = kind });
        Items = Items.OrderBy(i => i.Kind).ToList();
        foreach (var item in Items) item.Scale = Math.Clamp(item.Scale, Themes.Desk.MinScale, Themes.Desk.MaxScale);
    }
}

public sealed class DesktopWidgetPlacement
{
    public DesktopWidgetKind Kind { get; set; }
    public bool Visible { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1;
}

/// <summary>
/// Onglet « Presse-papiers » de l'île (voir Services/ClipboardHistory, Services/PasswordVault). Seulement des choix : ni
/// l'historique ni le coffre ne sont rangés dans settings.json (fichiers chiffrés à part).
/// </summary>
public sealed class ClipboardSettings
{
    /// <summary>Historique des éléments copiés (texte, liens, images), sans durée : on le vide soi-même.</summary>
    public bool History { get; set; } = true;
    /// <summary>Historique gardé après un redémarrage (fichier chiffré DPAPI) : activé par défaut (demande de l'utilisateur).</summary>
    public bool Persist { get; set; } = true;
    /// <summary>Un mot de passe copié depuis le coffre est effacé du presse-papiers après 30 s (s'il y est encore) : option.</summary>
    public bool AutoClear { get; set; }
    /// <summary>Mot de passe copié par un gestionnaire (formats « sensibles ») : l'île propose de le garder dans le coffre.</summary>
    public bool OfferSecrets { get; set; } = true;
}

/// <summary>Notes (voir Services/NotesService) : raccourci « Nouvelle note » et chiffrement du fichier.</summary>
public sealed class NotesSettings
{
    /// <summary>Raccourci global « Nouvelle note » ; vide = aucun (désactivé par défaut).</summary>
    public string Hotkey { get; set; } = "";
    /// <summary>Notes chiffrées par Windows (DPAPI, ce compte seulement) : notes.bin au lieu de notes.json.</summary>
    public bool Encrypt { get; set; }

    public void Normalize()
    {
        Hotkey ??= "";
        if (Hotkey.Length > 0 && !Services.Hotkeys.TryParse(Hotkey, out _, out _)) Hotkey = "";
    }
}

/// <summary>Santé du PC (6E, voir Services/Health).</summary>
public sealed class HealthSettings
{
    /// <summary>Onglet « Santé » dans l'île.</summary>
    public bool Tab { get; set; } = true;
    /// <summary>Annonce quand un disque interne passe sous 10 % d'espace libre.</summary>
    public bool DiskAlert { get; set; } = true;
}

/// <summary>Amis en ligne (6D-4, voir Services/Friends et server/amis) : désactivé par défaut.</summary>
public sealed class FriendsSettings
{
    public bool Enabled { get; set; }
    /// <summary>Adresse du serveur de l'utilisateur (son Worker Cloudflare, « https://….workers.dev »).</summary>
    public string Server { get; set; } = "";
    /// <summary>Pseudo vu par les amis (vide = le code d'ami).</summary>
    public string Name { get; set; } = "";
    /// <summary>Invisible : plus rien n'est partagé (hors ligne pour tout le monde).</summary>
    public bool Invisible { get; set; }
    /// <summary>Le nom du jeu en cours est partagé (sinon seulement « en jeu »).</summary>
    public bool ShareGame { get; set; } = true;
    /// <summary>Annonce dans l'île quand un ami se connecte ou lance un jeu.</summary>
    public bool Notify { get; set; } = true;
    /// <summary>Messages rapides des amis (« On joue ? »…) annoncés dans l'île (correctif 24).</summary>
    public bool Messages { get; set; } = true;
    /// <summary>Codes d'ami (8 caractères, sans tiret) dont on ne veut plus d'annonces ni de messages dans l'île.</summary>
    public List<string> Muted { get; set; } = [];
    /// <summary>
    /// Personnes bloquées (code + pseudo vu au moment du blocage). Le serveur (version 3) les bloque aussi de son côté ; avec un
    /// serveur plus ancien, elles sont seulement retirées et cachées ici.
    /// </summary>
    public List<BlockedFriend> Blocked { get; set; } = [];

    public void Normalize()
    {
        Server = (Server ?? "").Trim().TrimEnd('/');
        Name = (Name ?? "").Trim();
        if (Name.Length > 32) Name = Name[..32];
        Muted = (Muted ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant().Replace("-", "")).Distinct().Take(200).ToList();
        Blocked = (Blocked ?? []).Where(b => b != null && !string.IsNullOrWhiteSpace(b.Code))
            .Select(b => new BlockedFriend { Code = b.Code.Trim().ToUpperInvariant().Replace("-", ""), Name = (b.Name ?? "").Trim() })
            .DistinctBy(b => b.Code).Take(200).ToList();
    }
}

public sealed class BlockedFriend
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Éclairage RGB (6D-2, voir Services/Rgb) : désactivé par défaut.</summary>
public sealed class RgbSettings
{
    public bool Enabled { get; set; }
    /// <summary>Couleur de la pochette quand de la musique joue (sinon toujours l'accent du profil).</summary>
    public bool FollowMusic { get; set; }
    /// <summary>Rendre l'éclairage à G HUB / OpenRGB pendant une partie (leurs effets de jeu).</summary>
    public bool PauseInGames { get; set; } = true;
}

/// <summary>Mises à jour par les « Releases » GitHub (voir Services/UpdateService).</summary>
/// <summary>Canal des mises à jour (7C) : versions normales seulement, ou aussi les bêtas (pré-versions GitHub).</summary>
public enum UpdateChannel { Stable, Beta }

public sealed class UpdateSettings
{
    /// <summary>Vérifier 30 s après le démarrage puis chaque jour (rien n'est installé sans accord).</summary>
    public bool CheckAutomatically { get; set; } = true;
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    /// <summary>Dernière version dont l'écran « Nouveautés » a été montré (7C) ; null = pas encore (premier lancement).</summary>
    public string? LastSeenVersion { get; set; }
    /// <summary>Dépôt « propriétaire/nom » dont les releases contiennent l'installeur.</summary>
    public string Repository { get; set; } = "louismph8-arch/DynamicIsland";
    /// <summary>Version qu'on a choisi d'ignorer (« v0.2.0 ») : plus d'annonce pour elle.</summary>
    public string? SkippedVersion { get; set; }

    public void Normalize()
    {
        Repository = (Repository ?? "").Trim();
    }
}

/// <summary>Widgets de l'onglet « Widgets » de l'île.</summary>
public sealed class WidgetSettings
{
    public bool Weather { get; set; } = true;
    public string City { get; set; } = "Montréal";
    /// <summary>Coordonnées de <see cref="CityCoordinatesFor"/> (thème automatique : lever et coucher du soleil).</summary>
    public double? CityLatitude { get; set; }
    public double? CityLongitude { get; set; }
    public string? CityCoordinatesFor { get; set; }
    public bool Cpu { get; set; } = true;
    public bool Ram { get; set; } = true;
    public bool Gpu { get; set; } = true;
    public bool Network { get; set; } = true;
    /// <summary>Météo relue au plus toutes les … minutes (correctif 18), et à la sortie de veille.</summary>
    public int WeatherRefreshMinutes { get; set; } = 10;

    /// <summary>Choix proposés pour <see cref="WeatherRefreshMinutes"/>.</summary>
    public static readonly int[] WeatherRefreshChoices = [5, 10, 15, 30, 60];

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(City)) City = "Montréal";
        // Valeur modifiée à la main : le choix le plus proche.
        int minutes = WeatherRefreshMinutes;
        WeatherRefreshMinutes = WeatherRefreshChoices.OrderBy(c => Math.Abs(c - minutes)).ThenBy(c => c).First();
    }
}

/// <summary>Minuteur de l'île (correctif 19) : son de fin au choix, volume, dernière durée « Perso ».</summary>
public sealed class TimerSettings
{
    /// <summary>
    /// Son de fin : « opaline » (son d'Opaline, comme avant), « windows:Alarm01.wav » (alarme ou sonnerie de C:\Windows\Media)
    /// ou « file » (<see cref="CustomFile"/>).
    /// </summary>
    public string Sound { get; set; } = Services.TimerSound.OpalineId;
    /// <summary>Son perso (.wav / .mp3) choisi par une fenêtre Ouvrir de Windows.</summary>
    public string? CustomFile { get; set; }
    /// <summary>Volume du son de fin, de 0 à 1.</summary>
    public double Volume { get; set; } = 0.8;
    /// <summary>Dernière durée « Perso » réglée dans l'île, en secondes (rouverte telle quelle).</summary>
    public int CustomSeconds { get; set; } = 15 * 60;

    public void Normalize()
    {
        CustomFile = string.IsNullOrWhiteSpace(CustomFile) ? null : CustomFile.Trim();
        Sound = Services.TimerSound.NormalizeId(Sound, CustomFile);
        Volume = double.IsFinite(Volume) ? Math.Clamp(Math.Round(Volume, 2), 0, 1) : 0.8;
        CustomSeconds = Math.Clamp(CustomSeconds, 1, (int)Services.TimerService.MaxDuration.TotalSeconds);
    }
}
