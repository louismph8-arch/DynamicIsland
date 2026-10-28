using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Services;

/// <summary>
/// Luminosité des écrans externes via DDC/CI (dxva2). Le bus DDC est lent (~50 ms par
/// commande) : lectures et écritures passent par un thread dédié, et les écritures sont
/// regroupées (seule la dernière valeur demandée est envoyée). S'applique à tous les écrans
/// compatibles ; la valeur affichée est celle de l'écran principal.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private sealed record DdcMonitor(IntPtr Handle, uint Min, uint Max);

    private readonly List<DdcMonitor> _monitors = [];
    private readonly AutoResetEvent _signal = new(false);
    private Thread? _worker;
    private int _pendingSet = -1;
    private int _pendingRead;
    private DateTime _lastRead = DateTime.MinValue;
    private DateTime _lastWrite = DateTime.MinValue;
    private volatile bool _disposed;

    /// <summary>Levé (sur un thread quelconque) quand l'état ou la valeur lue change.</summary>
    public event Action? Changed;

    public bool IsReady { get; private set; }
    public bool IsSupported => _monitors.Count > 0;
    /// <summary>Luminosité en pourcentage (0 à 100).</summary>
    public int Current { get; private set; } = 50;

    public Task InitializeAsync() => Task.Run(() =>
    {
        try { Enumerate(); }
        catch (Exception ex) { Log.Error("Luminosité : détection des écrans", ex); }

        IsReady = true;
        if (IsSupported)
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "DDC/CI" };
            _worker.Start();
        }
        Changed?.Invoke();
    });

    public void Set(int percent)
    {
        if (!IsSupported) return;
        Current = Math.Clamp(percent, 0, 100);
        Interlocked.Exchange(ref _pendingSet, Current);
        _signal.Set();
    }

    /// <summary>Relit la valeur réelle (boutons de l'écran) — au plus toutes les 15 s.</summary>
    public void RequestRead()
    {
        if (!IsSupported || DateTime.UtcNow - _lastRead < TimeSpan.FromSeconds(15)) return;
        _lastRead = DateTime.UtcNow;
        Interlocked.Exchange(ref _pendingRead, 1);
        _signal.Set();
    }

    private void Enumerate()
    {
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) => { handles.Add(monitor); return true; },
            IntPtr.Zero);

        // L'écran principal en premier : c'est lui qui donne la valeur affichée.
        var primary = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        handles.Sort((a, b) => (b == primary).CompareTo(a == primary));

        foreach (var hMonitor in handles)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) continue;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) continue;

            foreach (var pm in physical)
            {
                if (TryRead(pm.hPhysicalMonitor, out uint min, out uint current, out uint max))
                {
                    if (_monitors.Count == 0) Current = ToPercent(current, min, max);
                    _monitors.Add(new DdcMonitor(pm.hPhysicalMonitor, min, max));
                }
                else
                {
                    DestroyPhysicalMonitor(pm.hPhysicalMonitor);
                }
            }
        }
        _lastRead = DateTime.UtcNow;
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            _signal.WaitOne();
            while (!_disposed)
            {
                int value = Interlocked.Exchange(ref _pendingSet, -1);
                if (value >= 0)
                {
                    foreach (var m in _monitors)
                        SetMonitorBrightness(m.Handle, (uint)Math.Round(m.Min + (m.Max - m.Min) * value / 100.0));
                    _lastWrite = DateTime.UtcNow;
                    Thread.Sleep(40); // laisse respirer le bus DDC/CI
                    continue;
                }

                if (Interlocked.Exchange(ref _pendingRead, 0) == 1)
                {
                    // Pas de relecture juste après une écriture : l'écran renverrait l'ancienne valeur.
                    if (DateTime.UtcNow - _lastWrite > TimeSpan.FromSeconds(3) &&
                        TryRead(_monitors[0].Handle, out uint min, out uint current, out uint max))
                    {
                        Current = ToPercent(current, min, max);
                        Changed?.Invoke();
                    }
                    continue;
                }
                break;
            }
        }
    }

    private static bool TryRead(IntPtr handle, out uint min, out uint current, out uint max)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (GetMonitorBrightness(handle, out min, out current, out max) && max > min) return true;
            Thread.Sleep(30);
        }
        min = current = max = 0;
        return false;
    }

    private static int ToPercent(uint value, uint min, uint max) =>
        (int)Math.Round((value - min) * 100.0 / (max - min));

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
        _worker?.Join(500);
        foreach (var m in _monitors) DestroyPhysicalMonitor(m.Handle);
        _monitors.Clear();
    }
}
