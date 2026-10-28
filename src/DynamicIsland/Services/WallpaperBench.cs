using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Mode développeur <c>--snapshot &lt;dossier&gt; --wallpaper-bench &lt;fichier&gt;</c> : coût du fond animé pour UN
/// écran 1920×1080 (fenêtre hors écran, même shader et même cadence que sur le bureau), animé puis en pause.
/// CPU et GPU 3D de l'app seulement. Le bureau n'est jamais touché.
/// </summary>
internal static class WallpaperBench
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);

    /// <param name="capture">Image de la fenêtre animée (rendu GPU, capturé par PrintWindow) : preuve que le shader dessine.</param>
    public static async Task<string> RunAsync(string? capture = null)
    {
        var effect = new AnimatedAuroraEffect();
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -32000,
            Top = 0,
            Width = 1920,
            Height = 1080,
            Content = new Rectangle { Fill = Brushes.Black, Effect = effect },
        };
        window.Show();
        try
        {
            await Task.Delay(1000);
            var lines = new List<string>
            {
                $"Rendu WPF : niveau {RenderCapability.Tier >> 16}, ps_3_0 : {RenderCapability.IsPixelShaderVersionSupported(3, 0)}",
                $"Fond animé — mesure du {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.ProcessorCount} cœurs logiques, shader disponible : {AnimatedAuroraEffect.IsAvailable}",
                $"Un écran 1920×1080, {Wallpapers.AnimationFrameRate} images/s.",
                "  " + await MeasureAsync(effect, animate: true),
                "  " + await MeasureAsync(effect, animate: false),
            };
            if (capture != null)
            {
                lines.Add("Témoin rouge : " + await CaptureCloakedAsync(null, System.IO.Path.ChangeExtension(capture, ".rouge.png")));
                lines.Add("Aurore : " + await CaptureCloakedAsync(new AnimatedAuroraEffect(), capture));
            }
            return string.Join(Environment.NewLine, lines);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Rendu GPU réel d'une petite fenêtre (320×180) posée sur l'écran mais masquée par DWM (« cloaked » : invisible
    /// pour l'utilisateur) et rangée sous toutes les autres, capturée par PrintWindow. Masquage refusé : elle est
    /// envoyée hors écran avant d'apparaître, et rien n'est capturé.
    /// </summary>
    private static async Task<string> CaptureCloakedAsync(AnimatedAuroraEffect? effect, string path)
    {
        effect?.SetFrame(16.0 / 9, 12.3);
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 0,
            Top = 0,
            Width = 320,
            Height = 180,
            Content = new Rectangle { Fill = effect == null ? Brushes.Red : Brushes.Black, Effect = effect },
        };
        bool cloaked = false;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            int on = 1;
            cloaked = Interop.DockNative.DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref on, sizeof(int)) == 0;
            if (!cloaked) window.Left = -32000;
            Interop.NativeMethods.SetWindowPos(hwnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0,
                Interop.NativeMethods.SWP_NOMOVE | Interop.NativeMethods.SWP_NOSIZE | Interop.NativeMethods.SWP_NOACTIVATE);
        };
        window.Show();
        try
        {
            await Task.Delay(700);
            return cloaked ? Capture(window, path) : "masquage refusé par DWM : pas de capture";
        }
        finally
        {
            window.Close();
        }
    }

    private static string Capture(Window window, string path)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        int width = (int)Math.Round(window.ActualWidth), height = (int)Math.Round(window.ActualHeight);
        using var bitmap = new System.Drawing.Bitmap(Math.Max(1, width), Math.Max(1, height));
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            bool ok = PrintWindow(hwnd, dc, 2); // PW_RENDERFULLCONTENT : contenu DirectX compris
            graphics.ReleaseHdc(dc);
            if (!ok) return "Capture : PrintWindow a échoué";
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 4);
        return $"Capture : {path} (pixel au centre-haut {center.R},{center.G},{center.B})";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    private static async Task<string> MeasureAsync(AnimatedAuroraEffect effect, bool animate)
    {
        using var gpu = new GpuMeter($"pid_{Environment.ProcessId}_*engtype_3D", "Mesure du fond animé : GPU");
        gpu.Sample();
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        int frames = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Wallpapers.FrameInterval };
        timer.Tick += (_, _) =>
        {
            effect.SetFrame(16.0 / 9, clock.Elapsed.TotalSeconds);
            frames++;
        };
        if (animate) timer.Start();
        await Task.Delay(Duration);
        timer.Stop();
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100;
        double? gpuPercent = gpu.Sample();
        return $"{(animate ? "animé" : "en pause")} : CPU {cpu:0.0} % d'un cœur ({cpu / Environment.ProcessorCount:0.00} % du PC), "
               + $"GPU 3D {(gpuPercent is double g ? $"{g:0.0} %" : "indisponible")}, {frames / clock.Elapsed.TotalSeconds:0} images/s";
    }
}
