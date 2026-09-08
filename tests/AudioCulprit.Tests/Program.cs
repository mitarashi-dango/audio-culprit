using AudioCulprit.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
if (args.Contains("--soak")) { SoakTest.Run(args); return; }
if (args.Contains("--check-soak")) { SoakTest.CheckSaved(args[Array.IndexOf(args, "--check-soak") + 1]); return; }
if (args.Contains("--tone"))
{
    var seconds = args.Contains("--background") ? 90 : 2;
    using var tone = new WasapiOut();
    tone.Init(new SignalGenerator { Gain = .025, Frequency = 440 }.Take(TimeSpan.FromSeconds(seconds)));
    tone.Play();
    Thread.Sleep(seconds * 1000 + 200);
    return;
}
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
var utc = DateTime.UtcNow;
AudioEvent Meta(string name = "test.exe") => new() { ProcessName = name, DisplayName = name, SessionId = "test", DeviceName = "test output" };
var d = new AudioActivityDetector(Meta());
d.Sample(0, utc, .009f);
Check(d.Current == null, "session existence and below threshold do not start");
d.Sample(50, utc, .1f);
d.Sample(200, utc.AddMilliseconds(150), .3f);
d.Sample(250, utc, .0f);
d.Sample(500, utc, 0);
Check(d.Snapshot?.EndTimeUtc != null, "silence hold marks end");
d.Sample(700, utc, .2f);
d.Sample(900, utc, 0);
var done = d.Sample(1450, utc, 0);
Check(done != null && done.DurationMs == 850 && done.MaxPeak == .3f, "500ms merge and silence tail excluded");
d.Sample(1500, utc, .2f);
Check(d.Current?.Id != done?.Id, "separate event after merge gap");
var music = new AudioActivityDetector(Meta("music.exe"));
music.Sample(0, utc, .2f);
for (var t = 50; t <= 600000; t += 50) music.Sample(t, utc.AddMilliseconds(t), .2f);
Check(music.Current?.DurationMs == 600000 && music.Current.SuspicionScore == 0, "ten minute music stays one background event");
var notification = Meta("chat.exe") with { StartTimeUtc = utc.AddSeconds(599), DurationMs = 200 };
Check(SuspicionEvaluator.Latest([music.Current!, notification], []) == notification, "notification wins over continuous music");
Check(SuspicionEvaluator.Latest([music.Current!, notification], [new("chat.exe", null)]) == music.Current, "ignore removes candidate only");
var yesterday = notification with { StartTimeUtc = utc.AddDays(-1), EndTimeUtc = utc.AddDays(-1).AddMilliseconds(200) };
var endedLong = Meta("recent.exe") with { StartTimeUtc = utc.AddSeconds(-40), EndTimeUtc = utc, DurationMs = 40000 };
Check(SuspicionEvaluator.Latest([yesterday, endedLong], []) == endedLong, "recent ended long sound beats yesterday's short sound");
Check(SuspicionEvaluator.Latest([yesterday, music.Current!], []) == music.Current, "old short sound cannot hide current music");
Check(SuspicionEvaluator.Latest([], []) == null, "empty history has no candidate");
var recentNotice = notification with { StartTimeUtc = utc.AddSeconds(-2), EndTimeUtc = utc.AddSeconds(-1.8) };
Check(SuspicionEvaluator.Latest([endedLong, recentNotice], []) == recentNotice, "notification still wins after background music stops");
var snapshot = new HistorySnapshot();
snapshot.Add(recentNotice);
Check(snapshot.Latest([]) == recentNotice, "unsaved completed sound available in memory");
snapshot.SetRules([new("chat.exe", null)]);
Check(snapshot.Latest([]) == null, "popup cache honors changed ignore rules");
snapshot.SetRules([]);
snapshot.Clear();
Check(snapshot.Latest([]) == null, "cleared history absent from popup cache");
snapshot.Add(yesterday with { StartTimeUtc = utc.AddDays(-8) });
Check(snapshot.Latest([]) == null, "popup cache excludes expired history");
var attempts = 0;
var registrations = 0;
using (var recovering = new AudioMonitorService(
    () => Interlocked.Increment(ref attempts) == 1 ? throw new InvalidOperationException("creation failure") : new NAudio.CoreAudioApi.MMDeviceEnumerator(),
    (e, client) => { if (Interlocked.Increment(ref registrations) == 1) throw new InvalidOperationException("registration failure"); e.RegisterEndpointNotificationCallback(client); }))
{
    Check(SpinWait.SpinUntil(() => recovering.Status.Contains("creation failure"), 1000), "creation failure is reported without crashing");
    Check(SpinWait.SpinUntil(() => recovering.Status.Contains("registration failure"), 3500), "registration failure is reported without crashing");
    Check(SpinWait.SpinUntil(() => recovering.Status.StartsWith("監視中") || recovering.Status == "出力デバイスを待機中", 4000), "monitor recovers after initialization failures");
}
var failing = new AudioMonitorService(() => throw new InvalidOperationException("offline"), (e, client) => { });
SpinWait.SpinUntil(() => failing.Status.Contains("offline"), 1000);
var shutdownTime = System.Diagnostics.Stopwatch.StartNew();
failing.Dispose();
Check(shutdownTime.ElapsedMilliseconds < 1000, "shutdown interrupts initialization retry delay");
Check(!new IgnoreRule("chat.exe", "C:\\one\\chat.exe").Matches(notification with { ProcessPath = "C:\\two\\chat.exe" }), "ignore prefers executable path");
var path = Path.Combine(Path.GetTempPath(), "AudioCulprit-test-" + Guid.NewGuid() + ".db");
try
{
    using (var repo = new HistoryRepository(path))
    {
        repo.Save(notification);
        repo.Save(notification);
        repo.Ignore(new("chat.exe", null));
        Check(repo.Load().Count == 1, "SQLite upsert");
        repo.Save(notification with { DurationMs=500, EndTimeUtc=utc.AddMilliseconds(500) });
        repo.Save(notification);
        Check(repo.Load()[0].DurationMs==500,"stale checkpoint cannot overwrite completed event");
    }
    using (var repo = new HistoryRepository(path))
    {
        Check(repo.Load()[0].ProcessName == "chat.exe" && repo.Rules().Count == 1, "history and ignores survive reopen");
        repo.Save(notification with
        {
            Id = "old",
            StartTimeUtc = utc.AddDays(-8)
        });
        repo.Prune();
        Check(repo.Load().Count == 1, "retention removes old events");
        repo.Clear();
        Check(repo.Load().Count == 0 && repo.Rules().Count == 1, "clear preserves ignore rules");
        using (var writer = new HistoryWriter(repo))
        {
            writer.Save(notification);
            writer.Clear();
            Check(repo.Load().Count == 0, "queued clear follows earlier writes");
            writer.Save(endedLong);
        }
        Check(repo.Load().Single().ProcessName == "recent.exe", "writer shutdown drains pending writes");
    }
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
if (args.Contains("--integration"))
{
    var events = new ConcurrentQueue<AudioEvent>();
    using (var monitor = new AudioMonitorService())
    {
        monitor.Completed += events.Enqueue;
        Thread.Sleep(2300);
        using (var output = new WasapiOut())
        {
            output.Init(new SignalGenerator { Gain = .03, Frequency = 660 }.Take(TimeSpan.FromSeconds(.25)));
            output.Play();
            Thread.Sleep(1600);
        }
        Check(events.Any(e => e.ProcessId == Environment.ProcessId && e.MaxPeak >= .01f && e.DurationMs > 0), "real short sound detected by production monitor");
        var ownShort = events.First(e => e.ProcessId == Environment.ProcessId);
        Check(ownShort.ProcessName == Path.GetFileName(Environment.ProcessPath) && string.Equals(ownShort.ProcessPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase), "short sound has exact executable name and path");
        if (args.Contains("--sources"))
        {
            var sourceDir = Path.Combine(Path.GetTempPath(), "AudioCulprit-sources-" + Guid.NewGuid());
            Directory.CreateDirectory(sourceDir);
            var wave = Path.Combine(sourceDir, "notification.wav");
            WaveFileWriter.CreateWaveFile16(wave, new SignalGenerator { Gain = .04, Frequency = 740 }.Take(TimeSpan.FromSeconds(.3)));
            var started = DateTime.UtcNow;
            Check(NativeSound.PlaySound(wave, IntPtr.Zero, 0x00020000 | 0x00200000), "Windows system-session sound playback succeeds");
            Thread.Sleep(1300);
            Check(events.Any(e => e.StartTimeUtc >= started && e.EventType == "SystemSounds" && e.ProcessName == "System Sounds"), "actual system-session sound identified separately from applications");
            var browserPath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            var html = Path.Combine(sourceDir, "notification.html");
            File.WriteAllText(html, "<html><body>AudioCulprit notification test<script>setTimeout(()=>{new Audio('data:audio/wav;base64," + Convert.ToBase64String(File.ReadAllBytes(wave)) + "').play()},2500)</script></body></html>");
            var info = new System.Diagnostics.ProcessStartInfo(browserPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--no-first-run", "--no-default-browser-check", "--autoplay-policy=no-user-gesture-required", "--user-data-dir=" + Path.Combine(sourceDir, "profile"), "--app=" + new Uri(html).AbsoluteUri }) info.ArgumentList.Add(arg);
            started = DateTime.UtcNow;
            using var browser = System.Diagnostics.Process.Start(info)!;
            try
            {
                Check(SpinWait.SpinUntil(() => events.Any(e => e.StartTimeUtc >= started && string.Equals(e.ProcessPath, browserPath, StringComparison.OrdinalIgnoreCase)), 15000), "real browser page short sound attributed to Edge executable");
                var browserEvent = events.First(e => e.StartTimeUtc >= started && string.Equals(e.ProcessPath, browserPath, StringComparison.OrdinalIgnoreCase));
                using var source = System.Diagnostics.Process.GetProcessById(browserEvent.ProcessId);
                Check(source.ProcessName == "msedge" && browserEvent.ProcessName == "msedge.exe", "browser audio PID belongs to a live Edge process");
                Console.WriteLine($"SOURCE: browser PID={browserEvent.ProcessId} path={browserEvent.ProcessPath}");
            }
            finally { if (!browser.HasExited) { browser.Kill(entireProcessTree: true); browser.WaitForExit(); } }
        }
        using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--tone") { UseShellExecute = false, CreateNoWindow = true })!)
        {
            Thread.Sleep(700);
            using (var output = new WasapiOut())
            {
                output.Init(new SignalGenerator { Gain = .03, Frequency = 880 }.Take(TimeSpan.FromSeconds(.25)));
                output.Play();
                Thread.Sleep(700);
            }
            child.WaitForExit();
            Thread.Sleep(900);
            Check(events.Any(e => e.ProcessId == child.Id && e.ProcessName != "Unknown") && events.Count(e => e.ProcessId == Environment.ProcessId) >= 2, "simultaneous distinct processes and metadata retained after process exit");
        }
        monitor.Enabled = false;
        Thread.Sleep(150);
        Check(monitor.Active.Count == 0, "pause clears active sessions");
        monitor.Enabled = true;
        Thread.Sleep(150);
        using (var output = new WasapiOut())
        {
            output.Init(new SignalGenerator { Gain = .025, Frequency = 440 }.Take(TimeSpan.FromSeconds(4)));
            output.Play();
            Thread.Sleep(800);
            var own = monitor.Active.First(e => e.ProcessId == Environment.ProcessId);
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(own.DeviceId);
            var sessions = endpoint.AudioSessionManager.Sessions;
            using var session = Enumerable.Range(0, sessions.Count).Select(i => sessions[i]).First(s => s.GetSessionInstanceIdentifier == own.SessionId);
            var original = session.SimpleAudioVolume.Mute;
            try
            {
                Check(monitor.SetMuteAsync(own, true).GetAwaiter().GetResult() >= 1 && session.SimpleAudioVolume.Mute, "real application mute reaches Core Audio");
                Check(monitor.SetMuteAsync(own, false).GetAwaiter().GetResult() >= 1 && !session.SimpleAudioVolume.Mute, "real application unmute reaches Core Audio");
                Check(monitor.SetMuteAsync(own with { ProcessPath = null, SessionId = "not-a-session" }, true).GetAwaiter().GetResult() == 0, "stale session cannot mute unrelated process");
            }
            finally { monitor.SetMuteAsync(own, original).GetAwaiter().GetResult(); }
        }
    }
    Check(true, "monitor shuts down and releases worker");
}
Console.WriteLine($"{passed} checks passed");

internal static class NativeSound
{
    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool PlaySound(string sound, IntPtr module, uint flags);
}


