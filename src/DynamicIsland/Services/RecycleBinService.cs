using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Dock;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Corbeille de Windows (6B) : contenu (nombre d'éléments, taille) lu toutes les 4 s en arrière-plan quand la cellule est
/// affichée ; fichiers déposés = mis à la corbeille (réversible, Windows avertit s'il ne peut pas les y mettre) ; vider =
/// seulement après la confirmation de l'appelant.
/// </summary>
public static class RecycleBinService
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private static bool _started;
    private static bool _reading;

    public static long Count { get; private set; }
    public static long Size { get; private set; }

    /// <summary>Contenu changé (thread UI).</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        Timer.Tick += (_, _) =>
        {
            if (!GamingMode.IsActive) Refresh(); // 7A : rien à lire pendant une partie
        };
        SettingsStore.Changed += Apply;
        Apply();
    }

    public static void Stop()
    {
        Timer.Stop();
        SettingsStore.Changed -= Apply;
        _started = false;
    }

    private static void Apply()
    {
        bool wanted = !ModuleGuard.IsDisabled(ModuleGuard.Status) && SettingsStore.Current.Dock.ShowsStatus(StatusItem.Trash);
        if (wanted == Timer.IsEnabled) return;
        if (wanted)
        {
            Refresh();
            Timer.Start();
        }
        else
        {
            Timer.Stop();
        }
    }

    public static void Refresh()
    {
        if (_reading) return;
        _reading = true;
        Task.Run(Query).ContinueWith(task =>
        {
            _reading = false;
            if (task.Exception != null || task.Result is not { } info) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (info.Count == Count && info.Size == Size) return;
                (Count, Size) = info;
                Changed?.Invoke();
            });
        }, TaskScheduler.Default);
    }

    internal static (long Count, long Size)? Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64NumItems, info.i64Size) : null;
    }

    /// <summary>Met ces fichiers / dossiers à la corbeille. Faux si l'opération a été annulée ou a échoué.</summary>
    public static bool Recycle(IReadOnlyList<string> paths, IntPtr owner)
    {
        if (paths.Count == 0) return false;
        var operation = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', paths) + "\0\0",
            // Corbeille (annulable) ; Windows demande lui-même si un élément ne peut pas y aller (trop gros, réseau…).
            fFlags = FOF_ALLOWUNDO | FOF_WANTNUKEWARNING,
        };
        int result = SHFileOperation(ref operation);
        Refresh();
        if (result != 0) Log.Info($"Corbeille : mise à la corbeille refusée ou échouée (code {result})");
        return result == 0 && !operation.fAnyOperationsAborted;
    }

    /// <summary>Vide la corbeille (l'appelant a déjà demandé confirmation).</summary>
    public static void Empty(IntPtr owner)
    {
        int result = SHEmptyRecycleBin(owner, null, SHERB_NOCONFIRMATION);
        if (result != 0 && result != unchecked((int)0x8000FFFF)) Log.Info($"Corbeille : vidage, code {result}"); // E_UNEXPECTED = déjà vide
        Refresh();
    }

    public static void Open() =>
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true })?.Dispose();

    // ------------------------------------------------------------------ interop

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;
    private const uint SHERB_NOCONFIRMATION = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr owner, string? rootPath, uint flags);
}
