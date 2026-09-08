using AudioCulprit.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

internal static class SoakTest
{
    public static void CheckSaved(string folder)
    {
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "soak-result.json")));
        var pid = result.RootElement.GetProperty("ProcessId").GetInt32();
        using var repository = new HistoryRepository(Path.Combine(folder, "audio-culprit.db"));
        var saved = repository.Load();
        var background = saved.Where(e => e.ProcessId == pid).ToArray();
        if (background.Length != 1 || background[0].DurationMs < 600000 || background[0].EndTimeUtc == null)
            throw new Exception("Product database did not retain one complete ten-minute event");
        foreach (var notice in result.RootElement.GetProperty("Notifications").EnumerateArray())
        {
            var notificationPid = notice.GetProperty("NotificationPid").GetInt32();
            if (!saved.Any(e => e.ProcessId == notificationPid && e.DurationMs > 0 && e.DurationMs < 10000 && e.EndTimeUtc != null))
                throw new Exception("Product database lost a notification");
        }
        Console.WriteLine("PASS: reopened product database retains one 10-minute sound and both separate notifications, with end times and source PIDs");
    }
    public static void Run(string[] args)
    {
        var folder = Path.GetFullPath(args[Array.IndexOf(args, "--soak") + 1]);
        Directory.CreateDirectory(folder);
        var completed = new ConcurrentQueue<AudioEvent>();
        using var monitor = new AudioMonitorService();
        monitor.Completed += completed.Enqueue;
        Thread.Sleep(2500);
        using var output = new WasapiOut();
        output.Init(new SignalGenerator { Gain = .025, Frequency = 440 }.Take(TimeSpan.FromSeconds(610)));
        var clock = Stopwatch.StartNew();
        output.Play();
        var ids = new HashSet<string>();
        var checks = new List<object>();
        var nextNotice = 300;
        while (clock.Elapsed.TotalSeconds < 613)
        {
            foreach (var e in monitor.Active.Where(e => e.ProcessId == Environment.ProcessId)) ids.Add(e.Id);
            if (clock.Elapsed.TotalSeconds >= nextNotice && nextNotice <= 600)
            {
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--tone") { UseShellExecute = false, CreateNoWindow = true })!;
                var found = SpinWait.SpinUntil(() => monitor.Active.Any(e => e.ProcessId == child.Id), 5000);
                var active = monitor.Active.ToArray();
                var wins = SuspicionEvaluator.Latest(active.Concat(completed), [])?.ProcessId == child.Id;
                var background = active.Any(e => e.ProcessId == Environment.ProcessId && e.DurationMs >= 290000 && e.SuspicionScore == 0);
                child.WaitForExit();
                checks.Add(new { AtSeconds = clock.Elapsed.TotalSeconds, NotificationPid = child.Id, Detected = found, NotificationWins = wins, BackgroundSeparate = background });
                if (!found || !wins || !background) throw new Exception("Long-play notification check failed");
                Console.WriteLine($"PASS: notification at {nextNotice}s wins while background remains active");
                nextNotice += 300;
            }
            Thread.Sleep(100);
        }
        var own = completed.Where(e => e.ProcessId == Environment.ProcessId).ToArray();
        var passed = ids.Count == 1 && own.Length == 1 && own[0].DurationMs >= 600000 && own[0].EndTimeUtc != null;
        File.WriteAllText(Path.Combine(folder, "soak-result.json"), JsonSerializer.Serialize(new { Passed = passed, Seconds = clock.Elapsed.TotalSeconds, ProcessId = Environment.ProcessId, ActiveEventIds = ids, Events = own, Notifications = checks }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new Exception("Continuous audio was fragmented, missing, or shorter than ten minutes");
        Console.WriteLine("PASS: 610 seconds real audio completed as one event");
    }
}
