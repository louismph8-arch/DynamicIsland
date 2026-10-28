using System.IO;
using System.Text.Json.Serialization;

namespace DynamicIsland.Dock;

public enum DockMode
{
    /// <summary>Toujours visible ; les fenêtres maximisées s'arrêtent au-dessus.</summary>
    Fixed,
    /// <summary>Se cache quand une fenêtre le touche, revient quand la souris va en bas.</summary>
    Smart,
    /// <summary>Toujours caché, apparaît quand la souris touche le bas de l'écran.</summary>
    Peek,
}

/// <summary>Bord de l'écran où se trouve le dock (5I).</summary>
public enum DockEdge
{
    Bottom,
    Top,
}

public enum DockLayout
{
    /// <summary>Barre fine sur toute la largeur, collée en bas (recherche à gauche, icônes d'arrière-plan à droite).</summary>
    Bar,
    /// <summary>Dock flottant centré, façon macOS.</summary>
    Floating,
}

/// <summary>Appli épinglée, ou dossier d'applis (<see cref="Items"/> non nul).</summary>
public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>Exe, raccourci .lnk ou raccourci Internet .url à lancer.</summary>
    public string? Path { get; set; }
    /// <summary>AppUserModelID pour une appli du Store.</summary>
    public string? AppId { get; set; }
    /// <summary>Contenu d'un dossier du dock (un seul niveau : pas de dossier dans un dossier).</summary>
    public List<PinnedApp>? Items { get; set; }
    /// <summary>Image perso qui remplace l'icône de l'appli (PNG dans %APPDATA%\DynamicIsland\Icônes), null = icône d'origine.</summary>
    public string? Icon { get; set; }

    [JsonIgnore]
    public bool IsFolder => Items != null;

    public PinnedApp Clone() => new() { Name = Name, Path = Path, AppId = AppId, Icon = Icon, Items = Items?.Select(i => i.Clone()).ToList() };

    public static List<PinnedApp> CloneAll(IEnumerable<PinnedApp> pins) => pins.Select(p => p.Clone()).ToList();

    public static PinnedApp Folder(string name, IEnumerable<PinnedApp> items) => new() { Name = name, Items = items.ToList() };

    /// <summary>
    /// Retire les entrées vides et les dossiers imbriqués, défait les dossiers d'une seule appli. Modifie la
    /// liste en place (même instance) : le dock ne recharge ses icônes que si la liste a été remplacée.
    /// </summary>
    public static List<PinnedApp> NormalizeList(List<PinnedApp>? pins)
    {
        pins ??= [];
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            var pin = pins[i];
            if (pin is { IsFolder: true })
            {
                pin.Items!.RemoveAll(item => item is not { IsFolder: false } || (item.Path == null && item.AppId == null));
                if (string.IsNullOrWhiteSpace(pin.Name)) pin.Name = Loc.T("dock.folder.default");
                if (pin.Items.Count == 1) pins[i] = pin.Items[0];
                else if (pin.Items.Count == 0) pins.RemoveAt(i);
            }
            else if (pin == null || (pin.Path == null && pin.AppId == null))
            {
                pins.RemoveAt(i);
            }
        }
        return pins;
    }
}

/// <summary>Élément de la zone d'état du dock (6B) : langue du clavier, Windows Update, corbeille, Téléchargements.</summary>
public sealed class StatusItem
{
    public const string Keyboard = "keyboard";
    public const string Updates = "updates";
    public const string Trash = "trash";
    public const string Downloads = "downloads";
    public static readonly string[] All = [Keyboard, Updates, Trash, Downloads];

    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;

    public static List<StatusItem> Defaults() => All.Select(id => new StatusItem { Id = id }).ToList();

    /// <summary>Chaque élément connu une seule fois, dans l'ordre choisi ; les nouveaux s'ajoutent à la fin.</summary>
    public static List<StatusItem> NormalizeList(List<StatusItem>? items)
    {
        var result = (items ?? []).Where(i => i != null && All.Contains(i.Id)).DistinctBy(i => i.Id).ToList();
        foreach (var id in All.Where(id => result.All(i => i.Id != id))) result.Add(new StatusItem { Id = id });
        return result;
    }
}

/// <summary>Section « Dock » des réglages (voir <see cref="Settings.SettingsStore"/>).</summary>
public sealed class DockSettings
{
    public bool Enabled { get; set; } = true;
    public DockMode Mode { get; set; } = DockMode.Fixed;
    public DockLayout Layout { get; set; } = DockLayout.Bar;
    public bool HideTaskbar { get; set; }
    /// <summary>Icônes d'arrière-plan (zone de notification) à droite du dock.</summary>
    public bool ShowTrayIcons { get; set; } = true;
    /// <summary>Grossissement maximal au survol (1 = aucun).</summary>
    public double Magnification { get; set; } = 1.4;
    /// <summary>Fenêtres réduites aspirées dans leur icône (effet Génie de macOS).</summary>
    public bool MinimizeEffect { get; set; } = true;
    /// <summary>Applis épinglées, dans l'ordre d'affichage.</summary>
    public List<PinnedApp> Pinned { get; set; } = [];
    /// <summary>Raccourci global qui fait apparaître le dock, même en jeu ou en plein écran.</summary>
    public string RevealHotkey { get; set; } = Services.Hotkeys.DefaultReveal;
    /// <summary>Toutes les icônes dans un même cadre squircle sombre (5G).</summary>
    public bool HarmonizedIcons { get; set; }
    /// <summary>Icônes redessinées d'une seule couleur : le dégradé d'accent (5G).</summary>
    public bool TintIcons { get; set; }
    /// <summary>Écran du dock (nom pour Windows, « \\.\DISPLAY3 ») ; null = l'écran principal (5I).</summary>
    public string? Screen { get; set; }
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    /// <summary>Dock flottant : décalage depuis le centre du bord, en unités WPF (0 = centré).</summary>
    public double Offset { get; set; }
    /// <summary>Position verrouillée : glisser le fond du dock ne le déplace plus.</summary>
    public bool LockPosition { get; set; }
    /// <summary>Zone d'état à droite du dock (6B) : éléments dans l'ordre d'affichage, chacun masquable.</summary>
    public List<StatusItem> Status { get; set; } = StatusItem.Defaults();
    /// <summary>Changement de langue du clavier aussi annoncé brièvement dans l'île.</summary>
    public bool KeyboardInIsland { get; set; }

    public bool ShowsStatus(string id) => Enabled && Status.Any(i => i.Id == id && i.Visible);

    public void Normalize()
    {
        Status = StatusItem.NormalizeList(Status);
        Magnification = Math.Clamp(Magnification, 1, 1.8);
        Offset = double.IsFinite(Offset) ? Math.Clamp(Offset, -8000, 8000) : 0;
        if (!Services.Hotkeys.TryParse(RevealHotkey, out _, out _)) RevealHotkey = Services.Hotkeys.DefaultReveal;
        Pinned = PinnedApp.NormalizeList(Pinned);
    }

    /// <summary>Premier lancement : l'Explorateur + les applis courantes trouvées sur le PC.</summary>
    public static List<PinnedApp> CreateDefaultPins() => [Explorer(), .. Find(KnownApps.Chrome, KnownApps.Spotify)];

    public static PinnedApp Explorer() =>
        new() { Name = Loc.T("search.kind.explorer"), Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") };

    /// <summary>Les applis connues installées sur ce PC (premier emplacement trouvé pour chacune).</summary>
    public static List<PinnedApp> Find(params (string Name, string[] Candidates)[] apps)
    {
        var pins = new List<PinnedApp>();
        foreach (var (name, candidates) in apps)
        {
            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null) pins.Add(new PinnedApp { Name = name, Path = found });
        }
        return pins;
    }
}

/// <summary>Emplacements habituels d'applis courantes (apps par défaut du dock et des profils).</summary>
public static class KnownApps
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly (string, string[]) Chrome = ("Google Chrome",
    [
        Path.Combine(ProgramFiles, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(ProgramFilesX86, @"Google\Chrome\Application\chrome.exe"),
        Path.Combine(Local, @"Google\Chrome\Application\chrome.exe"),
    ]);
    public static readonly (string, string[]) Edge = ("Microsoft Edge", [Path.Combine(ProgramFilesX86, @"Microsoft\Edge\Application\msedge.exe")]);
    public static readonly (string, string[]) Spotify = ("Spotify", [Path.Combine(Roaming, @"Spotify\Spotify.exe")]);
    public static readonly (string, string[]) Steam = ("Steam",
        [Path.Combine(ProgramFilesX86, @"Steam\steam.exe"), Path.Combine(ProgramFiles, @"Steam\steam.exe")]);
    public static readonly (string, string[]) Discord = ("Discord",
        [Path.Combine(Roaming, @"Microsoft\Windows\Start Menu\Programs\Discord Inc\Discord.lnk")]);
    public static readonly (string, string[]) Epic = ("Epic Games Launcher",
    [
        Path.Combine(ProgramFilesX86, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
        Path.Combine(ProgramFiles, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
    ]);
    public static readonly (string, string[]) BattleNet = ("Battle.net", [Path.Combine(ProgramFilesX86, @"Battle.net\Battle.net Launcher.exe")]);
    public static readonly (string, string[]) Ankama = ("Ankama Launcher", [Path.Combine(Local, @"Programs\Ankama Launcher\Ankama Launcher.exe")]);
    public static readonly (string, string[]) Ubisoft = ("Ubisoft Connect",
        [Path.Combine(ProgramFilesX86, @"Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe")]);
    public static readonly (string, string[]) Riot = ("Riot Client", [@"C:\Riot Games\Riot Client\RiotClientServices.exe"]);
    public static readonly (string, string[]) Word = Office("Word", "WINWORD.EXE");
    public static readonly (string, string[]) Excel = Office("Excel", "EXCEL.EXE");
    public static readonly (string, string[]) Outlook = Office("Outlook", "OUTLOOK.EXE");
    public static readonly (string, string[]) OneNote = Office("OneNote", "ONENOTE.EXE");
    public static readonly (string, string[]) VsCode = ("Visual Studio Code", [Path.Combine(Local, @"Programs\Microsoft VS Code\Code.exe")]);

    private static (string, string[]) Office(string name, string exe) =>
    (name,
    [
        Path.Combine(ProgramFiles, @"Microsoft Office\root\Office16", exe),
        Path.Combine(ProgramFilesX86, @"Microsoft Office\root\Office16", exe),
    ]);
}
