using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

/// <summary>
/// Mélangeur de volume (correctifs urgents, point 22) : les sessions audio Core Audio de la sortie par défaut, comme le
/// mélangeur de Windows, une ligne par appli (toutes les sessions d'un même programme ensemble : un navigateur en a souvent
/// plusieurs). Seules les applis qui font du son ou dont la session est active sont montrées. Lecture à la demande (l'onglet
/// relit au plus toutes les 2 s, seulement ouvert) ; tout se passe sur le thread de l'interface. Ne touche JAMAIS au volume
/// d'une appli sans un geste de l'utilisateur (curseur, bouton muet).
/// </summary>
public sealed class AudioMixerService
{
    private const int ClsCtxAll = 0x17;
    /// <summary>Clé de la session « Sons système ».</summary>
    public const string SystemKey = "#system";
    /// <summary>Identifie nos propres changements auprès de Windows.</summary>
    private static Guid _context = new("2B8E61C4-7D1A-4F0B-A7C3-5E9D0F4A8C21");
    private static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Une appli du mélangeur (sessions regroupées).</summary>
    public sealed record Entry(string Key, string Name, string? ExePath, bool SystemSounds, double Level, bool Muted, float Peak);

    /// <summary>Une session lue, avant le regroupement par programme (séparé pour être vérifié par --self-test).</summary>
    internal readonly record struct RawSession(string Key, string Name, string? ExePath, bool SystemSounds, double Level, bool Muted, float Peak);

    private IMMDeviceEnumerator? _enumerator;
    /// <summary>Sessions gardées depuis la dernière lecture (pour régler leur volume), par clé d'appli.</summary>
    private readonly List<(string Key, ISimpleAudioVolume Volume, object Session)> _sessions = [];
    private bool _errorLogged;

    /// <summary>Applis qui font du son (ou ont une session active), celles qui jouent d'abord, les sons système en dernier.</summary>
    public IReadOnlyList<Entry> Read()
    {
        Release();
        var raw = new List<RawSession>();
        try
        {
            Scan(raw);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Release();
            if (!_errorLogged)
            {
                _errorLogged = true; // une seule ligne de journal, pas une toutes les 2 s
                Log.Error("Mélangeur de volume", ex);
            }
        }
        return Merge(raw);
    }

    /// <summary>Volume (0 à 1) de toutes les sessions de l'appli ; un volume au-dessus de 0 enlève la sourdine.</summary>
    public void SetLevel(string key, double level)
    {
        float value = (float)Math.Clamp(level, 0, 1);
        foreach (var (k, volume, _) in _sessions)
        {
            if (k != key) continue;
            volume.SetMasterVolume(value, ref _context);
            if (value > 0 && volume.GetMute(out bool muted) == 0 && muted) volume.SetMute(false, ref _context);
        }
    }

    /// <summary>Sourdine de l'appli (toutes ses sessions).</summary>
    public void SetMute(string key, bool mute)
    {
        foreach (var (k, volume, _) in _sessions)
            if (k == key) volume.SetMute(mute, ref _context);
    }

    /// <summary>Regroupe les sessions par programme : volume le plus haut, muet seulement si toutes le sont, crête la plus forte.</summary>
    internal static List<Entry> Merge(IEnumerable<RawSession> sessions) =>
        sessions
            .GroupBy(s => s.Key)
            .Select(g =>
            {
                var first = g.First();
                return new Entry(g.Key, first.Name, g.Select(s => s.ExePath).FirstOrDefault(p => p != null), first.SystemSounds,
                    g.Max(s => s.Level), g.All(s => s.Muted), g.Max(s => s.Peak));
            })
            .OrderBy(e => e.SystemSounds)
            .ThenByDescending(e => e.Peak > 0.001f)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private void Scan(List<RawSession> raw)
    {
        _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) != 0 || device == null) return;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance) != 0 || instance is not IAudioSessionManager2 manager) return;
            int hr = manager.GetSessionEnumerator(out sessions);
            Marshal.ReleaseComObject(manager);
            if (hr != 0 || sessions == null || sessions.GetCount(out int count) != 0) return;

            var paths = new Dictionary<uint, string?>();
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0) continue;
                if (Describe(session, paths) is { } entry)
                {
                    raw.Add(entry);
                    _sessions.Add((entry.Key, (ISimpleAudioVolume)session, session));
                }
                else
                {
                    Marshal.ReleaseComObject(session);
                }
            }
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>La session telle qu'on la montre, ou null si elle ne fait pas de son (ou si c'est Opaline elle-même).</summary>
    private static RawSession? Describe(object session, Dictionary<uint, string?> paths)
    {
        if (session is not IAudioSessionControl2 control || session is not ISimpleAudioVolume volume) return null;
        if (control.GetProcessId(out uint pid) != 0 || pid == Environment.ProcessId) return null;
        float peak = session is IAudioMeterInformation meter && meter.GetPeakValue(out float p) == 0 ? p : 0;
        bool active = control.GetState(out var state) == 0 && state == AudioSessionState.Active;
        if (!active && peak <= 0.001f) return null;
        if (volume.GetMasterVolume(out float level) != 0) return null;
        volume.GetMute(out bool muted);

        if (control.IsSystemSoundsSession() == 0)
            return new RawSession(SystemKey, Loc.T("mixer.system_sounds"), null, true, level, muted, peak);

        if (!paths.TryGetValue(pid, out var path)) paths[pid] = path = DockNative.GetProcessPath(pid);
        string key = path != null ? Path.GetFileNameWithoutExtension(path).ToLowerInvariant() : "pid:" + pid;
        return new RawSession(key, NameOf(path, DisplayName(control), pid), path, false, level, muted, peak);
    }

    /// <summary>Nom affiché : la description de l'exe (« Google Chrome »), sinon le nom de la session, sinon celui du programme.</summary>
    private static string NameOf(string? path, string? sessionName, uint pid)
    {
        if (path != null && NameCache.TryGetValue(path, out var cached)) return cached;
        string? name = null;
        if (path != null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description)) name = description;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // fichier illisible : on passe au nom suivant
            }
        }
        name ??= !string.IsNullOrWhiteSpace(sessionName) && !sessionName.StartsWith('@') ? sessionName.Trim() : null;
        name ??= path != null ? Path.GetFileNameWithoutExtension(path) : ProcessName(pid);
        if (path != null) NameCache[path] = name;
        return name;
    }

    private static string? DisplayName(IAudioSessionControl2 control)
    {
        if (control.GetDisplayName(out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    /// <summary>Rend les sessions gardées (avant chaque lecture et quand l'onglet se ferme).</summary>
    public void Release()
    {
        foreach (var (_, _, session) in _sessions) Marshal.ReleaseComObject(session);
        _sessions.Clear();
    }
}
