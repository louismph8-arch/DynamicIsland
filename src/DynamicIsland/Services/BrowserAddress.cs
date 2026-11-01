using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DynamicIsland.Services;

/// <summary>Navigateur trouvé : son nom, et l'adresse de son onglet actif (null si elle n'a pas pu être lue).</summary>
public sealed record BrowserPage(string Browser, string? Address);

/// <summary>
/// « Traduire la page ouverte » : adresse de l'onglet actif du navigateur au premier plan, sinon du dernier navigateur utilisé
/// (fenêtre de navigateur la plus haute dans l'ordre des fenêtres). Lecture SEULE de la barre d'adresse par UI Automation (ce que
/// lisent les lecteurs d'écran) : pas d'injection, pas d'extension, pas de touche simulée, rien n'est écrit dans le navigateur.
/// Chrome, Edge, Brave, Opera, Vivaldi (Chromium) et Firefox.
/// </summary>
public static class BrowserAddress
{
    /// <summary>Processus de navigateurs reconnus → nom affiché.</summary>
    private static readonly Dictionary<string, string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["brave"] = "Brave", ["opera"] = "Opera", ["opera_gx"] = "Opera GX",
        ["vivaldi"] = "Vivaldi", ["firefox"] = "Firefox", ["chromium"] = "Chromium",
    };

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(4);
    private const int MaxNodes = 600;   // la barre d'adresse est en haut de l'arbre : inutile de parcourir toute la page
    private const int MaxDepth = 14;

    /// <summary>Navigateur trouvé et son adresse (null : aucun navigateur ouvert).</summary>
    public static async Task<BrowserPage?> ReadAsync()
    {
        var found = FindWindow();
        if (found == null) return null;
        var (hwnd, name) = found.Value;
        var reading = Task.Run(() => ReadAddress(hwnd)); // UI Automation hors du thread de l'interface (fil MTA)
        var finished = await Task.WhenAny(reading, Task.Delay(ReadTimeout));
        string? address = finished == reading ? await reading : null;
        if (finished != reading) Log.Info($"Traduire : barre d'adresse de {name} trop lente à lire");
        return new BrowserPage(name, address);
    }

    /// <summary>Fenêtre du navigateur au premier plan, sinon la plus haute fenêtre de navigateur visible (ordre des fenêtres).</summary>
    internal static (IntPtr Hwnd, string Name)? FindWindow()
    {
        var foreground = GetForegroundWindow();
        if (BrowserName(foreground) is { } active) return (foreground, active);
        (IntPtr, string)? shown = null, minimized = null;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || BrowserName(hwnd) is not { } name) return true;
            if (IsIconic(hwnd))
            {
                minimized ??= (hwnd, name); // réduit : seulement s'il n'y en a pas d'affiché
                return true;
            }
            shown = (hwnd, name);
            return false; // EnumWindows va du haut vers le bas : la première = la dernière utilisée
        }, IntPtr.Zero);
        return shown ?? minimized;
    }

    /// <summary>Nom du navigateur si cette fenêtre principale en est un (classe de fenêtre + nom du processus).</summary>
    private static string? BrowserName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        var buffer = new StringBuilder(64);
        GetClassName(hwnd, buffer, buffer.Capacity);
        var cls = buffer.ToString();
        if (cls is not ("Chrome_WidgetWin_1" or "MozillaWindowClass")) return null;
        if (GetWindowTextLength(hwnd) == 0) return null; // fenêtres d'outils / bulles sans titre
        GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Browsers.TryGetValue(process.ProcessName, out var name) ? name : null;
        }
        catch (ArgumentException)
        {
            return null; // processus fermé entre-temps
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Barre d'adresse = premier champ de saisie de la fenêtre, parcours en largeur SANS entrer dans les pages web (Document) :
    /// un champ d'une page n'est jamais pris pour l'adresse. Valeur lue par ValuePattern (lecture seule).
    /// </summary>
    private static string? ReadAddress(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            int seen = 0;
            while (queue.Count > 0 && seen++ < MaxNodes)
            {
                var (element, depth) = queue.Dequeue();
                var type = element.Current.ControlType;
                if (type == ControlType.Document) continue;
                if (type == ControlType.Edit && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                {
                    var value = ((ValuePattern)pattern).Current.Value;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
                if (depth >= MaxDepth) continue;
                for (var child = walker.GetFirstChild(element); child != null; child = walker.GetNextSibling(child))
                    queue.Enqueue((child, depth + 1));
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            Log.Info($"Traduire : barre d'adresse illisible ({ex.GetType().Name})");
        }
        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
