using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Live;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Tools;

/// <summary>
/// Mémo vocal (tuile Mémo) : enregistrement du micro par défaut de Windows avec l'API « waveIn » de winmm (aucune dépendance),
/// en WAV 16 bits mono 44,1 kHz dans Documents\Opaline\Mémos. Seulement au clic ; un 2e clic l'arrête (et au plus tard après
/// <see cref="MaxLength"/>). Pendant l'enregistrement : activité en direct dans la pilule (point rouge + temps écoulé), point
/// orange « micro utilisé » de Windows. Un fil à part attend les tampons (événement de winmm, aucune boucle qui tourne à vide) ;
/// l'en-tête du fichier est mis à jour toutes les 2 s (un arrêt brutal laisse un fichier lisible). Fermeture de l'app ou module
/// arrêté = l'enregistrement s'arrête proprement.
/// </summary>
internal static class VoiceMemo
{
    public const int SampleRate = 44100;
    public const short Bits = 16;
    public const short Channels = 1;
    private const int BufferCount = 4;
    private const int BufferMilliseconds = 100;
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan HeaderEvery = TimeSpan.FromSeconds(2);

    private static Recorder? _recorder;
    private static DispatcherTimer? _tick;
    private static bool _hooked;
    private static (bool Recording, TimeSpan Elapsed)? _demo;

    public static bool IsRecording => _demo?.Recording ?? _recorder != null;

    public static TimeSpan Elapsed => _demo?.Elapsed ?? _recorder?.Elapsed ?? TimeSpan.Zero;

    public static string? LastFile { get; private set; }
    public static TimeSpan LastLength { get; private set; }

    /// <summary>Message (clé de texte) du dernier échec ; null sinon.</summary>
    public static string? Problem { get; private set; }

    /// <summary>Un micro est branché (sinon la tuile ne prend pas de place).</summary>
    public static bool Supported
    {
        get
        {
            if (_demo != null) return true;
            try
            {
                return waveInGetNumDevs() > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Documents\Opaline\Mémos (créé au premier enregistrement).</summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name, "Mémos");

    public static void Toggle(TileContext context)
    {
        if (_demo != null) return;
        if (_recorder != null) Stop();
        else Start(context);
    }

    private static void Start(TileContext context)
    {
        Hook();
        Problem = null;
        string path;
        try
        {
            Directory.CreateDirectory(Folder);
            path = Path.Combine(Folder, $"{Loc.T("tiles.memo.file")} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Mémo vocal : dossier", ex);
            Problem = "tiles.memo.error.folder";
            return;
        }
        var recorder = new Recorder(path);
        if (recorder.Open() is { } error)
        {
            Problem = error;
            return;
        }
        _recorder = recorder;
        recorder.Finished += (file, length) => Application.Current?.Dispatcher.BeginInvoke(() => OnFinished(recorder, file, length));
        recorder.Run();
        Log.Info("Mémo vocal : enregistrement commencé");
        _tick ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick -= OnTick;
        _tick.Tick += OnTick;
        _tick.Start();
        OnTick(null, EventArgs.Empty);
    }

    /// <summary>Arrête l'enregistrement en cours (attend la fin de l'écriture, 2 s au plus).</summary>
    public static void Stop()
    {
        var recorder = _recorder;
        if (recorder == null) return;
        recorder.Stop(wait: true);
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (_recorder is not { } recorder)
        {
            _tick?.Stop();
            return;
        }
        if (recorder.Elapsed >= MaxLength)
        {
            Stop();
            return;
        }
        LiveActivities.Set(LiveActivity.Recording(recorder.Elapsed));
    }

    private static void OnFinished(Recorder recorder, string? file, TimeSpan length)
    {
        if (ReferenceEquals(_recorder, recorder)) _recorder = null;
        _tick?.Stop();
        LiveActivities.Remove(LiveIds.Recording);
        if (file != null)
        {
            LastFile = file;
            LastLength = length;
            Log.Info($"Mémo vocal : enregistré ({length.TotalSeconds:0} s)");
        }
        else if (Problem == null) Problem = "tiles.memo.error.device";
    }

    /// <summary>Fermeture de l'app et module arrêté : l'enregistrement est fini proprement (fichier complet).</summary>
    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        if (Application.Current != null) Application.Current.Exit += (_, _) => Stop();
        ModuleGuard.Register(ModuleGuard.MemoTile, Stop);
    }

    public static void OpenFolder()
    {
        try
        {
            if (LastFile != null && File.Exists(LastFile)) Process.Start("explorer.exe", $"/select,\"{LastFile}\"")?.Dispose();
            else
            {
                Directory.CreateDirectory(Folder);
                Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Mémo vocal : ouverture du dossier", ex);
        }
    }

    /// <summary>En-tête WAV (RIFF, PCM) pour <paramref name="dataBytes"/> octets de son : calcul pur (auto-test).</summary>
    internal static byte[] Header(int dataBytes)
    {
        var header = new byte[44];
        using var writer = new BinaryWriter(new MemoryStream(header));
        int blockAlign = Channels * Bits / 8;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write(Bits);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        return header;
    }

    /// <summary>Rendus --snapshot : état d'exemple (rien n'est enregistré).</summary>
    internal static void UseDemo(bool recording, TimeSpan elapsed, TimeSpan? last)
    {
        _demo = (recording, elapsed);
        Problem = null;
        LastLength = last ?? TimeSpan.Zero;
        LastFile = last != null ? "demo.wav" : null;
    }

    /// <summary>Fin de l'exemple : retour à l'état réel (rien en cours).</summary>
    internal static void ClearDemo()
    {
        _demo = null;
        LastFile = null;
        LastLength = TimeSpan.Zero;
    }

    // ------------------------------------------------------------------ enregistreur (fil à part)

    private sealed class Recorder(string path)
    {
        private readonly AutoResetEvent _signal = new(false);
        private readonly Stopwatch _clock = new();
        private readonly IntPtr[] _headers = new IntPtr[BufferCount];
        private readonly bool[] _queued = new bool[BufferCount];
        private IntPtr _device;
        private FileStream? _file;
        private Thread? _thread;
        private volatile bool _stopping;
        private int _dataBytes;

        public event Action<string?, TimeSpan>? Finished;

        public TimeSpan Elapsed => _clock.Elapsed;

        /// <summary>Ouvre le micro et le fichier ; une clé de texte d'erreur, ou null.</summary>
        public string? Open()
        {
            var format = new WaveFormat
            {
                FormatTag = 1, Channels = Channels, SamplesPerSec = SampleRate, AvgBytesPerSec = SampleRate * Channels * Bits / 8,
                BlockAlign = (short)(Channels * Bits / 8), BitsPerSample = Bits, Size = 0,
            };
            int result = waveInOpen(out _device, WaveMapper, ref format, _signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            if (result != 0)
            {
                Log.Info($"Mémo vocal : micro indisponible (waveInOpen = {result})");
                return result == MmsysErrAllocated ? "tiles.memo.error.busy" : "tiles.memo.error.device";
            }
            try
            {
                _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                _file.Write(Header(0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Mémo vocal : fichier", ex);
                waveInClose(_device);
                return "tiles.memo.error.folder";
            }
            int bytes = format.AvgBytesPerSec * BufferMilliseconds / 1000;
            int size = Marshal.SizeOf<WaveHeader>();
            for (int i = 0; i < BufferCount; i++)
            {
                var header = new WaveHeader { Data = Marshal.AllocHGlobal(bytes), BufferLength = bytes };
                _headers[i] = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(header, _headers[i], false);
                waveInPrepareHeader(_device, _headers[i], size);
                _queued[i] = waveInAddBuffer(_device, _headers[i], size) == 0;
            }
            return null;
        }

        public void Run()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Mémo vocal" };
            _thread.Start();
        }

        public void Stop(bool wait)
        {
            _stopping = true;
            _signal.Set();
            if (wait && _thread != null && _thread != Thread.CurrentThread) _thread.Join(TimeSpan.FromSeconds(2));
        }

        private void Loop()
        {
            string? file = null;
            int size = Marshal.SizeOf<WaveHeader>();
            var lastHeader = Stopwatch.StartNew();
            try
            {
                waveInStart(_device);
                _clock.Start();
                while (!_stopping)
                {
                    _signal.WaitOne(500);
                    Drain(requeue: !_stopping, size);
                    if (lastHeader.Elapsed >= HeaderEvery)
                    {
                        WriteHeader();
                        lastHeader.Restart();
                    }
                }
                waveInReset(_device); // rend les tampons en cours, marqués « faits »
                _clock.Stop();
                Drain(requeue: false, size);
                WriteHeader();
                file = path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Mémo vocal : écriture", ex);
            }
            finally
            {
                for (int i = 0; i < BufferCount; i++)
                {
                    if (_headers[i] == IntPtr.Zero) continue;
                    waveInUnprepareHeader(_device, _headers[i], size);
                    Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHeader>(_headers[i]).Data);
                    Marshal.FreeHGlobal(_headers[i]);
                    _headers[i] = IntPtr.Zero;
                }
                waveInClose(_device);
                _file?.Dispose();
                _signal.Dispose();
                if (file == null || _dataBytes == 0) TryDelete(path, ref file);
                Finished?.Invoke(file, TimeSpan.FromSeconds((double)_dataBytes / (SampleRate * Channels * Bits / 8)));
            }
        }

        /// <summary>Écrit les tampons remplis (dans l'ordre), puis les rend à winmm sauf à l'arrêt.</summary>
        private void Drain(bool requeue, int size)
        {
            for (int i = 0; i < BufferCount; i++)
            {
                if (!_queued[i]) continue;
                var header = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
                if ((header.Flags & WhdrDone) == 0) continue;
                _queued[i] = false;
                if (header.BytesRecorded > 0 && _file != null)
                {
                    var data = new byte[header.BytesRecorded];
                    Marshal.Copy(header.Data, data, 0, data.Length);
                    _file.Write(data);
                    _dataBytes += data.Length;
                }
                if (!requeue) continue;
                header.Flags &= ~WhdrDone;
                header.BytesRecorded = 0;
                Marshal.StructureToPtr(header, _headers[i], false);
                _queued[i] = waveInAddBuffer(_device, _headers[i], size) == 0;
            }
        }

        private void WriteHeader()
        {
            if (_file == null) return;
            long position = _file.Position;
            _file.Position = 0;
            _file.Write(Header(_dataBytes));
            _file.Position = position;
            _file.Flush();
        }

        private static void TryDelete(string file, ref string? result)
        {
            result = null; // rien d'enregistré : pas de fichier vide qui traîne
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Mémo vocal : fichier vide", ex);
            }
        }
    }

    // ------------------------------------------------------------------ winmm

    private const int WaveMapper = -1;
    private const int CallbackEvent = 0x00050000;
    private const int WhdrDone = 0x00000001;
    private const int MmsysErrAllocated = 4;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr device, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr device);
}

/// <summary>
/// Tuile Mémo vocal : clic = enregistrer / arrêter (icône et gros bouton rouges pendant l'enregistrement, temps écoulé en
/// chiffres), clic droit = dossier des mémos (le dernier sélectionné).
/// </summary>
internal sealed class VoiceMemoFace : ToolFace
{
    private readonly SquircleBorder? _button;
    private readonly TextBlock? _buttonGlyph;
    private readonly SquircleBorder? _stop;

    public VoiceMemoFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        if (size == TileSize.S) return;
        double side = size == TileSize.L ? ToolTileShape.ButtonLarge : ToolTileShape.Button;
        (_button, _buttonGlyph) = RoundButton(side);
        // Pendant l'enregistrement : un carré blanc plein au centre du bouton rouge (« arrêter », comme un magnétophone).
        _stop = new SquircleBorder { Width = side * ToolTileShape.StopRatio, Height = side * ToolTileShape.StopRatio, CornerRadius = Corners.XS, Background = Paint.Frozen(Swatch.White), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var grid = new Grid();
        grid.Children.Add(_button);
        grid.Children.Add(_stop);
        SetAccessory(grid);
    }

    protected override void Update()
    {
        bool recording = VoiceMemo.IsRecording;
        var tone = recording ? ToolTone.Alert : ToolTone.Idle;
        SetTone(Icons.Record, tone);
        string line;
        if (recording) line = Loc.T("tiles.memo.stop");
        else if (VoiceMemo.Problem is { } problem) line = Loc.T(problem);
        else if (VoiceMemo.LastFile != null) line = Loc.F("tiles.memo.last", TimerService.Format(VoiceMemo.LastLength));
        else line = Loc.T("tiles.memo.hint");
        SetTexts(recording ? TimerService.Format(VoiceMemo.Elapsed) : null, line);
        if (_button != null && _buttonGlyph != null && _stop != null)
        {
            PaintButton(_button, _buttonGlyph, recording ? "" : Icons.Record, tone);
            _stop.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
