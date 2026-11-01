using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using DynamicIsland.Services;
using static DynamicIsland.Interop.DockNative;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>
/// Une fenêtre touchée par Stage Manager : sa place d'AVANT (rcNormalPosition de GetWindowPlacement, agrandie ou non), réduite par
/// nous ou seulement déplacée au centre (et alors la place où on l'a mise).
/// </summary>
internal readonly record struct StageEntry(long Handle, int ProcessId, bool Minimized, bool WasMaximized, RECT Original, RECT? Placed);

/// <summary>Que faire d'une fenêtre notée quand Stage Manager s'arrête (ou après un plantage).</summary>
internal enum StageRestore
{
    /// <summary>Fenêtre fermée, autre processus (handle réutilisé), ou que l'utilisateur a déjà changée : on n'y touche pas.</summary>
    Nothing,
    /// <summary>Réduite par nous et toujours réduite : rendue à sa place d'avant, sans voler le focus.</summary>
    Show,
    /// <summary>Déplacée au centre par nous et toujours là : remise à sa place d'avant.</summary>
    PutBack,
}

/// <summary>
/// Fichier d'état de Stage Manager (%APPDATA%\DynamicIsland\stage-manager.txt) : une ligne par fenêtre réduite ou déplacée, écrite
/// AVANT de la toucher. Le mode coupé, la fermeture, le gardien (après un plantage) et le lancement suivant rendent les fenêtres
/// encore là (<see cref="RecoverIfNeeded"/>, appelé par <see cref="ShellGuard.RecoverAll"/>), puis effacent le fichier.
/// </summary>
internal static class StageState
{
    private const string Header = "# Opaline Stage Manager 1";

    /// <summary>Format : « handle pid min|moved max|normal L T R B [pL pT pR pB] » (invariant).</summary>
    public static string Format(IEnumerable<StageEntry> entries)
    {
        var lines = new List<string> { Header };
        foreach (var e in entries)
        {
            var parts = new List<string>
            {
                e.Handle.ToString(CultureInfo.InvariantCulture), e.ProcessId.ToString(CultureInfo.InvariantCulture),
                e.Minimized ? "min" : "moved", e.WasMaximized ? "max" : "normal",
                Rect(e.Original),
            };
            if (e.Placed is { } placed) parts.Add(Rect(placed));
            lines.Add(string.Join(' ', parts));
        }
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Lignes lisibles seulement (un fichier abîmé ne fait jamais planter : les lignes illisibles sont ignorées).</summary>
    public static List<StageEntry> Parse(string text)
    {
        var entries = new List<StageEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is not (8 or 12)) continue;
            if (!long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long handle)
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                || p[2] is not ("min" or "moved") || p[3] is not ("max" or "normal")
                || !TryRect(p, 4, out var original)) continue;
            RECT? placed = null;
            if (p.Length == 12)
            {
                if (!TryRect(p, 8, out var r)) continue;
                placed = r;
            }
            entries.Add(new StageEntry(handle, pid, p[2] == "min", p[3] == "max", original, placed));
        }
        return entries;
    }

    /// <summary>
    /// Décision pure (vérifiée par --self-test) : la fenêtre existe encore, dans le MÊME processus (un handle peut être réutilisé),
    /// et n'a pas été changée depuis : réduite par nous et toujours réduite → rendue ; déplacée par nous et toujours à la place où on
    /// l'a mise (à 4 px près) → remise à sa place d'avant. Sinon on ne touche à rien.
    /// </summary>
    public static StageRestore Plan(StageEntry entry, bool exists, int currentProcess, bool iconic, RECT? currentRect)
    {
        if (!exists || currentProcess != entry.ProcessId) return StageRestore.Nothing;
        if (entry.Minimized) return iconic ? StageRestore.Show : StageRestore.Nothing;
        if (iconic || entry.Placed is not { } placed || currentRect is not { } now) return StageRestore.Nothing;
        return Near(placed, now) ? StageRestore.PutBack : StageRestore.Nothing;
    }

    private static bool Near(RECT a, RECT b) =>
        Math.Abs(a.Left - b.Left) <= 4 && Math.Abs(a.Top - b.Top) <= 4 && Math.Abs(a.Right - b.Right) <= 4 && Math.Abs(a.Bottom - b.Bottom) <= 4;

    // ------------------------------------------------------------------ fichier

    /// <summary>Écrit l'état (fichier effacé s'il n'y a plus rien). Faux si l'écriture a échoué : on ne touche alors à aucune fenêtre.</summary>
    public static bool Save(IReadOnlyCollection<StageEntry> entries, string? path = null)
    {
        path ??= AppPaths.StageState;
        try
        {
            if (entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            if (path == AppPaths.StageState) AppPaths.EnsureDataFolder();
            var temp = path + ".tmp";
            File.WriteAllText(temp, Format(entries));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Stage Manager : état impossible à écrire", ex);
            return false;
        }
    }

    public static List<StageEntry> Load(string? path = null)
    {
        path ??= AppPaths.StageState;
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Stage Manager : état illisible", ex);
            return [];
        }
    }

    /// <summary>
    /// Rend les fenêtres notées (encore là) puis efface le fichier : mode coupé, module arrêté, fermeture, gardien, lancement suivant.
    /// Asynchrone côté fenêtres (ShowWindowAsync / SetWindowPlacement) : une appli figée ne bloque rien.
    /// </summary>
    public static int RecoverIfNeeded()
    {
        if (!File.Exists(AppPaths.StageState)) return 0;
        int restored = Restore(Load());
        try
        {
            File.Delete(AppPaths.StageState);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Stage Manager : état impossible à effacer", ex);
        }
        if (restored > 0) Log.Info($"Stage Manager : {restored} fenêtre(s) rendue(s)");
        return restored;
    }

    /// <summary>Rend ces fenêtres (celles qui sont encore là et n'ont pas changé).</summary>
    public static int Restore(IEnumerable<StageEntry> entries)
    {
        int restored = 0;
        foreach (var entry in entries)
        {
            var hwnd = new IntPtr(entry.Handle);
            bool exists = IsWindow(hwnd);
            int pid = 0;
            if (exists)
            {
                GetWindowThreadProcessId(hwnd, out uint process);
                pid = (int)process;
            }
            bool iconic = exists && IsIconic(hwnd);
            RECT? now = exists && GetWindowRect(hwnd, out var r) ? r : null;
            var plan = Plan(entry, exists, pid, iconic, now);
            if (plan == StageRestore.Nothing) continue;
            try
            {
                var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(hwnd, ref placement)) continue;
                placement.rcNormalPosition = entry.Original; // même repère (zone de travail) que celui noté
                placement.flags = entry.WasMaximized ? placement.flags | WPF_RESTORETOMAXIMIZED : placement.flags & ~WPF_RESTORETOMAXIMIZED;
                if (plan == StageRestore.Show)
                {
                    placement.showCmd = SW_SHOWMINNOACTIVE; // la place d'abord, toujours réduite…
                    SetWindowPlacement(hwnd, ref placement);
                    ShowWindowAsync(hwnd, SW_SHOWNOACTIVATE); // … puis rendue comme avant (agrandie si elle l'était), sans focus
                }
                else
                {
                    placement.showCmd = entry.WasMaximized ? SW_SHOWMAXIMIZED : SW_SHOWNOACTIVATE;
                    SetWindowPlacement(hwnd, ref placement);
                }
                restored++;
            }
            catch (Exception ex)
            {
                Log.Error("Stage Manager : fenêtre à rendre", ex);
            }
        }
        return restored;
    }

    private const int SW_SHOWNOACTIVATE = 4;

    private static string Rect(RECT r) => string.Join(' ', new[] { r.Left, r.Top, r.Right, r.Bottom }.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    private static bool TryRect(string[] p, int start, out RECT rect)
    {
        rect = default;
        var v = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(p[start + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
        rect = new RECT { Left = v[0], Top = v[1], Right = v[2], Bottom = v[3] };
        return true;
    }
}
