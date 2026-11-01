using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DynamicIsland.Settings;
using Microsoft.Win32;

namespace DynamicIsland.Plugins;

/// <summary>Widget installé : paquet relu (et revérifié) au lancement, dossier où il est rangé.</summary>
public sealed record InstalledWidget(WidgetPackage Package, string Folder)
{
    public WidgetDefinition Definition => Package.Definition;
    public string Id => Package.Definition.Id;
}

/// <summary>
/// Widgets des potes installés : un dossier par widget dans %APPDATA%\DynamicIsland\Widgets\&lt;id&gt;\ qui garde le
/// fichier reçu tel quel (« widget.opalinewidget ») ; il est revérifié à chaque lancement (un fichier abîmé ou modifié à
/// la main qui ne passe plus les vérifications n'est pas affiché). Choix de l'utilisateur dans <see cref="PluginSettings"/>.
/// </summary>
public static class PluginStore
{
    private const string PackageFile = "widget.opalinewidget";
    private const string ProgId = "DynamicIsland.Widget";
    private static List<InstalledWidget>? _installed;
    private static List<InstalledWidget>? _sample;
    private static readonly List<string> BrokenList = [];

    public static string Root => Path.Combine(AppPaths.DataFolder, "Widgets");

    /// <summary>La liste des widgets installés a changé (installation, mise à jour, désinstallation).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<InstalledWidget> Installed
    {
        get
        {
            if (_sample != null) return _sample;
            if (_installed == null) Load();
            return _installed!;
        }
    }

    /// <summary>Dossiers de widgets qui ne se relisent plus (affichés « indisponibles » dans les paramètres).</summary>
    public static IReadOnlyList<string> Broken => BrokenList;

    public static InstalledWidget? Find(string id) => Installed.FirstOrDefault(w => w.Id == id);

    /// <summary>Rendus --snapshot : widgets d'exemple en mémoire (le dossier Widgets n'est jamais lu ni écrit).</summary>
    internal static void UseSample(IEnumerable<WidgetPackage> packages)
    {
        _sample = packages.Select(p => new InstalledWidget(p, "")).ToList();
        Changed?.Invoke();
    }

    private static void Load()
    {
        _installed = [];
        BrokenList.Clear();
        if (!Directory.Exists(Root)) return;
        foreach (var folder in Directory.GetDirectories(Root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(folder);
            if (!WidgetPackageReader.IsValidId(id)) continue;
            try
            {
                var package = WidgetPackageReader.Read(Path.Combine(folder, PackageFile), out _);
                if (package.Definition.Id != id) throw new WidgetPackageException("plugins.error.id", package.Definition.Id);
                _installed.Add(new InstalledWidget(package, folder));
            }
            catch (Exception ex)
            {
                BrokenList.Add(id);
                Log.Error($"Widget des potes « {id} » : illisible, pas affiché", ex);
            }
        }
    }

    /// <summary>
    /// Installe (ou met à jour) un widget déjà vérifié. <paramref name="allowWeb"/> = l'utilisateur a coché l'accord pour le
    /// site de sa source web ; une mise à jour qui change de site perd l'accord précédent.
    /// </summary>
    public static void Install(WidgetPackage package, byte[] bytes, bool allowWeb)
    {
        if (SettingsStore.IsReadOnly) throw new WidgetPackageException("plugins.error.read_only");
        var definition = package.Definition;
        var folder = Path.Combine(Root, definition.Id);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, PackageFile);
        var temporary = target + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, target, overwrite: true);

        var host = definition.Source?.Host;
        SettingsStore.Update(s =>
        {
            bool known = s.Plugins.Items.Any(i => i.Id == definition.Id);
            var state = s.Plugins.For(definition.Id);
            if (!known) state.InIsland = true;
            state.AllowedHost = host == null ? null : allowWeb ? host : state.AllowedHost == host ? host : null;
        });
        Log.Info($"Widget des potes installé : {definition.Id} {definition.Version}{(host != null ? $", source {host} {(allowWeb ? "autorisée" : "non autorisée")}" : "")}");
        Reload();
    }

    /// <summary>Désinstalle : dossier du widget effacé (après la confirmation de l'utilisateur), choix oubliés.</summary>
    public static void Uninstall(string id)
    {
        if (!WidgetPackageReader.IsValidId(id)) return;
        var folder = Path.Combine(Root, id);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        SettingsStore.Update(s => s.Plugins.Items.RemoveAll(i => i.Id == id));
        Log.Info($"Widget des potes désinstallé : {id}");
        Reload();
    }

    public static void Reload()
    {
        _installed = null;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ double-clic, modèle

    /// <summary>Associe .opalinewidget à l'app (registre de l'utilisateur seulement), comme les thèmes.</summary>
    public static void RegisterFileType()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var command = $"\"{exe}\" --import \"%1\"";
        var typeName = Loc.F("plugins.file_type", AppInfo.Name);
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        using (var current = classes.OpenSubKey($@"{ProgId}\shell\open\command"))
        using (var type = classes.OpenSubKey(ProgId))
        using (var extension = classes.OpenSubKey(WidgetPackageReader.Extension))
        {
            if (current?.GetValue(null) as string == command && type?.GetValue(null) as string == typeName
                && extension?.GetValue(null) as string == ProgId) return;
        }
        using (var extension = classes.CreateSubKey(WidgetPackageReader.Extension)) extension.SetValue(null, ProgId);
        using (var type = classes.CreateSubKey(ProgId))
        {
            type.SetValue(null, typeName);
            using (var icon = type.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using (var open = type.CreateSubKey(@"shell\open\command")) open.SetValue(null, command);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
    }

    public static bool IsWidgetFile(string? path) => path != null && path.EndsWith(WidgetPackageReader.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fichiers du modèle embarqués dans l'app (« Créer un widget »).</summary>
    internal static readonly (string Resource, string File)[] TemplateFiles =
    [
        ("DynamicIsland.Widgets.modele.opalinewidget", "mon-widget.opalinewidget"),
        ("DynamicIsland.Widgets.horloge-meteo.opalinewidget", "exemple-horloge-meteo.opalinewidget"),
        ("DynamicIsland.Widgets.compteur-bitcoin.opalinewidget", "exemple-compteur-bitcoin.opalinewidget"),
        ("DynamicIsland.Widgets.doc.md", "widgets.md"),
    ];

    /// <summary>Contenu d'un fichier du modèle (ressource de l'exe).</summary>
    internal static byte[]? TemplateBytes(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Exemples embarqués lus comme des widgets reçus (rendus --snapshot, auto-test).</summary>
    internal static IEnumerable<WidgetPackage> Examples() =>
        TemplateFiles.Where(t => t.Resource.EndsWith(WidgetPackageReader.Extension, StringComparison.Ordinal) && !t.Resource.Contains(".modele."))
            .Select(t => TemplateBytes(t.Resource)).OfType<byte[]>().Select(WidgetPackageReader.Parse);

    /// <summary>
    /// « Créer un widget » : Documents\Opaline - créer un widget\ avec le modèle commenté, deux exemples et la doc (un fichier
    /// déjà là n'est jamais écrasé : c'est peut-être le travail de l'utilisateur), puis ouverture du dossier.
    /// </summary>
    public static string CreateTemplateFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Loc.F("plugins.template.folder", AppInfo.Name));
        Directory.CreateDirectory(folder);
        foreach (var (resource, file) in TemplateFiles)
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path) && TemplateBytes(resource) is { } bytes) File.WriteAllBytes(path, bytes);
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
        return folder;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
