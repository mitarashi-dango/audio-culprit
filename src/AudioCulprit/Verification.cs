using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace AudioCulprit;
// Explicit developer verification only. Does not run during normal use.
internal static class Verification
{
    public static void StartSoak(App app, string folder, int durationSeconds = 720)
    {
        Directory.CreateDirectory(folder);
        var process = Process.GetCurrentProcess();
        var watch = Stopwatch.StartNew();
        var previousCpu = process.TotalProcessorTime;
        double previousSeconds = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        var samples = new System.Collections.Generic.List<object>();
        timer.Tick += (_, _) =>
        {
            process.Refresh();
            var seconds = watch.Elapsed.TotalSeconds;
            var cpu = process.TotalProcessorTime;
            samples.Add(new { Seconds = seconds, WorkingSetMB = process.WorkingSet64 / 1048576.0,
                PrivateMB = process.PrivateMemorySize64 / 1048576.0, ManagedMB = GC.GetTotalMemory(false) / 1048576.0,
                CpuPercent = (cpu - previousCpu).TotalSeconds / (seconds - previousSeconds) / Environment.ProcessorCount * 100,
                Handles = process.HandleCount, app.Monitor.Status });
            previousCpu = cpu;
            previousSeconds = seconds;
            File.WriteAllText(Path.Combine(folder, "performance.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            if (seconds < durationSeconds) return;
            timer.Stop();
            app.Shutdown();
            process.Dispose();
        };
        timer.Start();
    }
    public static void StartTray(App app, string folder, string fileName = "tray-runtime.json")
    {
        Directory.CreateDirectory(folder);
        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => { timer.Stop(); process.Refresh(); File.WriteAllText(Path.Combine(folder, fileName), JsonSerializer.Serialize(new { WorkingSetMB = process.WorkingSet64 / 1048576.0, CpuPercent = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, Seconds = watch.Elapsed.TotalSeconds }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(); };
        timer.Start();
    }
    public static void Start(App app, MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        var process = Process.GetCurrentProcess();
        var startCpu = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        var count = 0;
        timer.Tick += (_, _) =>
        {
            count++;
            if (count == 2 || count == 4)
                window.PauseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (count == 1 || count == 3 || count == 5)
            {
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, count == 3 ? "monitor-stopped.png" : count == 5 ? "monitor-restarted.png" : "main-window.png"));
                encoder.Save(stream);
            }
            if (count < 7)
                return;
            timer.Stop();
            process.Refresh();
            File.WriteAllText(Path.Combine(folder, "runtime.json"), JsonSerializer.Serialize(new
            {
                app.Monitor.Status,
                WorkingSetMB = process.WorkingSet64 / 1048576.0,
                CpuPercent = (process.TotalProcessorTime - startCpu).TotalSeconds / elapsed.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                Seconds = elapsed.Elapsed.TotalSeconds
            }, new JsonSerializerOptions { WriteIndented = true }));
            app.Shutdown();
        };
        timer.Start();
    }
}

