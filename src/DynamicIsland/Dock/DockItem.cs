using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Interop;
using DynamicIsland.Services;

namespace DynamicIsland.Dock;

/// <summary>Une icône du dock : appli épinglée et/ou ouverte (avec ses fenêtres), ou dossier d'applis épinglées.</summary>
public sealed class DockItem
{
    private static int _folderCount;

    private DockItem(string name, string? launchPath, string? exePath, string? appId)
    {
        Name = name;
        LaunchPath = launchPath;
        ExePath = exePath;
        AppId = appId;
        Key = appId != null ? "app:" + appId.ToLowerInvariant() : "exe:" + (exePath ?? launchPath ?? name).ToLowerInvariant();
        _systemIcon = IconService.Get(appId != null ? @"shell:AppsFolder\" + appId : launchPath ?? exePath ?? "");
        BaseIcon = _systemIcon;
    }

    /// <summary>Dossier : ses applis gardent leur propre DockItem (icône, lancement).</summary>
    private DockItem(PinnedApp folder, List<DockItem> children)
    {
        Name = folder.Name;
        Pin = folder;
        Children = children;
        Key = "folder:" + Interlocked.Increment(ref _folderCount);
        RefreshFolderIcon();
    }

    /// <summary>Identifiant de regroupement (exe ou AppUserModelID).</summary>
    public string Key { get; }
    public string Name { get; private set; }
    /// <summary>Ce qu'on lance au clic (exe ou raccourci).</summary>
    public string? LaunchPath { get; }
    /// <summary>Exe réel, pour reconnaître les fenêtres de l'appli.</summary>
    public string? ExePath { get; }
    public string? AppId { get; }

    private readonly ImageSource? _systemIcon;
    private ImageSource? _folderIcon;

    /// <summary>Icône sans style : image perso de l'appli épinglée, sinon celle de Windows.</summary>
    public ImageSource? BaseIcon { get; private set; }

    /// <summary>Icône affichée : celle d'origine, ou harmonisée / teintée selon les réglages (<see cref="IconStyler"/>).</summary>
    public ImageSource? Icon => Children != null ? _folderIcon : IconStyler.Style(BaseIcon);

    /// <summary>Applis d'un dossier (null pour une appli).</summary>
    public List<DockItem>? Children { get; }
    public bool IsFolder => Children != null;

    /// <summary>Entrée sauvegardée si l'appli est épinglée, sinon null.</summary>
    public PinnedApp? Pin { get; set; }
    public bool IsPinned => Pin != null;

    /// <summary>Fenêtres ouvertes, la plus au premier plan d'abord.</summary>
    public List<AppWindow> Windows { get; } = [];
    public bool IsRunning => Windows.Count > 0;

    public static DockItem FromPin(PinnedApp pin)
    {
        if (pin.IsFolder) return new DockItem(pin, pin.Items!.Where(i => !i.IsFolder).Select(FromPin).ToList());
        var exe = pin.Path != null && pin.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ShellNative.ResolveShortcut(pin.Path)
            : pin.Path;
        var item = new DockItem(pin.Name, pin.Path, exe, pin.AppId) { Pin = pin };
        if (pin.Icon != null) item.BaseIcon = LoadCustomIcon(pin.Icon) ?? item._systemIcon;
        return item;
    }

    /// <summary>Remplace l'icône par une image perso (null = icône d'origine). L'appli doit être épinglée.</summary>
    public void SetCustomIcon(string? path)
    {
        if (Pin == null) return;
        Pin.Icon = path;
        BaseIcon = path != null ? LoadCustomIcon(path) ?? _systemIcon : _systemIcon;
    }

    private static ImageSource? LoadCustomIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad; // fichier relâché tout de suite
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or UnauthorizedAccessException)
        {
            Log.Error($"Dock : icône perso {path}", ex);
            return null;
        }
    }

    /// <summary>Nouveau dossier qui regroupe des applis déjà dans le dock.</summary>
    public static DockItem CreateFolder(string name, IEnumerable<DockItem> items)
    {
        var children = items.ToList();
        foreach (var child in children) child.Windows.Clear(); // les fenêtres restent dans le dock, à part
        return new DockItem(PinnedApp.Folder(name, children.Select(c => c.Pin!)), children);
    }

    /// <summary>« Ajouter une app… » : un exe, un raccourci ou un raccourci Internet choisi par l'utilisateur.</summary>
    public static DockItem FromFile(string path)
    {
        bool isShortcut = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
        var name = isShortcut ? Path.GetFileNameWithoutExtension(path) : Describe(path);
        return FromPin(new PinnedApp { Name = name, Path = path });
    }

    /// <summary>Appli ouverte mais pas épinglée.</summary>
    public static DockItem FromWindow(AppWindow window) =>
        window.AppId != null
            ? new DockItem(window.Title, null, null, window.AppId)
            : new DockItem(window.ExePath != null ? Describe(window.ExePath) : window.Title, window.ExePath, window.ExePath, null);

    public bool Matches(AppWindow window) =>
        AppId != null
            ? string.Equals(AppId, window.AppId, StringComparison.OrdinalIgnoreCase)
            : window.AppId == null && ExePath != null && string.Equals(ExePath, window.ExePath, StringComparison.OrdinalIgnoreCase);

    public PinnedApp CreatePin() => new() { Name = Name, Path = AppId == null ? LaunchPath ?? ExePath : null, AppId = AppId };

    /// <summary>Dossier : recopie son contenu dans l'entrée sauvegardée.</summary>
    public void SyncFolder()
    {
        if (Children != null && Pin != null) Pin.Items = Children.Select(c => c.Pin!).ToList();
    }

    public void Rename(string name)
    {
        Name = name;
        if (Pin != null) Pin.Name = name;
    }

    /// <summary>Dossier : refait la mosaïque après un ajout ou un retrait.</summary>
    public void RefreshFolderIcon()
    {
        if (Children != null) _folderIcon = FolderIcon.Render(Children.Select(c => c.Icon));
    }

    public void Launch()
    {
        if (IsFolder) return; // un dossier s'ouvre, il ne se lance pas
        try
        {
            if (AppId != null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + AppId) { UseShellExecute = true });
            }
            else if ((LaunchPath ?? ExePath) is { } path)
            {
                var start = new ProcessStartInfo(path) { UseShellExecute = true };
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    start.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
                Process.Start(start);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Dock : lancement de « {Name} »", ex);
        }
    }

    /// <summary>Nom lisible d'un exe (description du fichier, sinon son nom).</summary>
    private static string Describe(string exePath)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch
        {
            // Fichier inaccessible : on garde le nom du fichier.
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }
}
