using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

/// <summary>
/// Volume d'une seule appli (ses sessions audio Core Audio, comme dans le mélangeur de volume de Windows),
/// sans toucher au volume général. Sert au curseur de l'onglet Musique : l'appli qui joue la musique est
/// retrouvée d'après l'identifiant de sa session média (« Spotify.exe », « Chrome », « SpotifyAB…!Spotify »).
/// Les sessions sont relues au plus toutes les 2 s ; tout se passe sur le thread UI.
/// </summary>
public sealed class AppVolumeService
{
    private const int ClsCtxAll = 0x17;
    private static readonly TimeSpan RescanAfter = TimeSpan.FromSeconds(2);
    /// <summary>Identifie nos propres changements auprès de Windows.</summary>
    private static Guid _context = new("6F1C7D0E-5A63-4C3E-9C1B-2D8E4B7A9F10");

    private IMMDeviceEnumerator? _enumerator;
    private readonly List<(ISimpleAudioVolume Volume, object Session)> _matched = [];
    private string? _matchedFor;
    private DateTime _scannedAt = DateTime.MinValue;
    private bool _errorLogged;

    /// <summary>Volume (0 à 1) et sourdine de l'appli ; false si elle n'a pas de session audio.</summary>
    public bool TryRead(string? sourceAppId, out double level, out bool muted)
    {
        level = 0;
        muted = false;
        Refresh(sourceAppId);
        foreach (var (volume, _) in _matched)
        {
            if (volume.GetMasterVolume(out float scalar) != 0) continue;
            volume.GetMute(out muted);
            level = scalar;
            return true;
        }
        return false;
    }

    /// <summary>Change le volume de toutes les sessions de l'appli (un navigateur en a souvent plusieurs).</summary>
    public void SetLevel(string? sourceAppId, double level)
    {
        Refresh(sourceAppId);
        float value = (float)Math.Clamp(level, 0, 1);
        foreach (var (volume, _) in _matched)
        {
            volume.SetMasterVolume(value, ref _context);
            if (value > 0 && volume.GetMute(out bool muted) == 0 && muted) volume.SetMute(false, ref _context);
        }
    }

    private void Refresh(string? sourceAppId)
    {
        sourceAppId ??= "";
        if (_matchedFor == sourceAppId && DateTime.UtcNow - _scannedAt < RescanAfter) return;
        _matchedFor = sourceAppId;
        _scannedAt = DateTime.UtcNow;
        Release();
        if (sourceAppId.Length == 0) return;

        try
        {
            Scan(sourceAppId);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Release();
            if (_errorLogged) return;
            _errorLogged = true; // une seule ligne de journal, pas une toutes les 2 s
            Log.Error("Volume de l'appli musique", ex);
        }
    }

    private void Scan(string sourceAppId)
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

            var target = Target.From(sourceAppId);
            var names = new Dictionary<uint, string>();
            var playing = new List<(ISimpleAudioVolume Volume, object Session, string Process)>();
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0) continue;
                bool kept = false;
                if (session is IAudioSessionControl2 control && session is ISimpleAudioVolume volume
                    && control.IsSystemSoundsSession() != 0 && control.GetProcessId(out uint pid) == 0 && pid != Environment.ProcessId)
                {
                    var process = ProcessName(pid, names);
                    control.GetSessionInstanceIdentifier(out var identifier);
                    if (target.Matches(process, identifier ?? ""))
                    {
                        _matched.Add((volume, session));
                        kept = true;
                    }
                    else if (control.GetState(out var state) == 0 && state == AudioSessionState.Active
                             && session is IAudioMeterInformation meter && meter.GetPeakValue(out float peak) == 0 && peak > 0.001f)
                    {
                        playing.Add((volume, session, process));
                        kept = true;
                    }
                }
                if (!kept) Marshal.ReleaseComObject(session);
            }

            // Identifiant inconnu (Firefox, appli exotique) : une seule appli qui fait du son → c'est elle.
            if (_matched.Count == 0 && playing.Count > 0 && playing.All(p => p.Process == playing[0].Process))
            {
                foreach (var (volume, session, _) in playing) _matched.Add((volume, session));
                playing.Clear();
            }
            foreach (var (_, session, _) in playing) Marshal.ReleaseComObject(session);
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(device);
        }
    }

    private static string ProcessName(uint pid, Dictionary<uint, string> cache)
    {
        if (cache.TryGetValue(pid, out var name)) return name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName.ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            name = "";
        }
        cache[pid] = name;
        return name;
    }

    private void Release()
    {
        foreach (var (_, session) in _matched) Marshal.ReleaseComObject(session);
        _matched.Clear();
    }

    /// <summary>Ce qu'on cherche dans les sessions audio pour un identifiant de session média.</summary>
    private sealed record Target(string? Process, string? PackageName, string? Publisher)
    {
        public static Target From(string appId)
        {
            var id = appId.Trim();
            int bang = id.IndexOf('!');
            if (bang > 0)
            {
                // Appli du Store : « Nom.Paquet_éditeur!App » ; le chemin de son exe contient « Nom.Paquet_version_…__éditeur ».
                var family = id[..bang];
                int underscore = family.LastIndexOf('_');
                return underscore > 0
                    ? new Target(null, family[..underscore].ToLowerInvariant(), family[(underscore + 1)..].ToLowerInvariant())
                    : new Target(null, family.ToLowerInvariant(), null);
            }
            var exe = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
            return new Target(System.IO.Path.GetFileName(exe).ToLowerInvariant(), null, null);
        }

        public bool Matches(string process, string identifier)
        {
            if (Process != null) return process.Length > 0 && process == Process;
            var lower = identifier.ToLowerInvariant();
            return PackageName != null && lower.Contains(PackageName) && (Publisher == null || lower.Contains(Publisher));
        }
    }
}
