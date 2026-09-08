namespace AudioCulprit.Core;

public sealed record AudioEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string SessionId { get; init; } = "";
    public int ProcessId
    {
        get; init;
    }
    public string ProcessName { get; init; } = "Unknown";
    public string? ProcessPath
    {
        get; init;
    }
    public string DisplayName { get; init; } = "Unknown";
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DateTime StartTimeUtc
    {
        get; init;
    }
    public DateTime? EndTimeUtc
    {
        get; init;
    }
    public double DurationMs
    {
        get; init;
    }
    public float MaxPeak
    {
        get; init;
    }
    public float SessionVolume
    {
        get; init;
    }
    public bool WasMuted
    {
        get; init;
    }
    public string EventType { get; init; } = "Application";
    public int SuspicionScore
    {
        get; init;
    }
    public string TimeLabel => StartTimeUtc.ToLocalTime().ToString("HH:mm:ss");
    public string SourceLabel => EventType == "SystemSounds"
        ? "Windows システム音 · 原因のアプリ・機器は未特定"
        : ProcessId > 0 ? $"音声セッションの PID: {ProcessId} · {ProcessName}"
        : "音源プロセスを取得できませんでした";
    public string DurationLabel => DurationMs >= 60000 ? $"{(int)(DurationMs / 60000)}分{DurationMs / 1000 % 60:00}秒" : $"{DurationMs / 1000:0.00}秒";
    public string PeakLabel => $"{MaxPeak:P0}";
    public string StateLabel => EndTimeUtc != null ? "終了" : DurationMs >= 30000 ? "継続再生" : "再生中";
}
public sealed record IgnoreRule(string ProcessName, string? ProcessPath)
{
    public bool Matches(AudioEvent e) => !string.IsNullOrEmpty(ProcessPath)
     ? string.Equals(ProcessPath, e.ProcessPath, StringComparison.OrdinalIgnoreCase)
     : string.Equals(ProcessName, e.ProcessName, StringComparison.OrdinalIgnoreCase);
    public override string ToString() => ProcessPath ?? ProcessName;
}
public static class SuspicionEvaluator
{
    public static AudioEvent? Latest(IEnumerable<AudioEvent> events, IEnumerable<IgnoreRule> rules)
    {
        var ignored = rules.ToArray();
        var candidates = events.Where(e => !ignored.Any(r => r.Matches(e))).ToArray();
        if (candidates.Length == 0) return null;
        static DateTime LastSound(AudioEvent e) => e.EndTimeUtc ?? e.StartTimeUtc.AddMilliseconds(e.DurationMs);
        var newest = candidates.Max(LastSound);
        // Background playback loses priority only to sounds in the same recent window.
        return candidates.Where(e => LastSound(e) >= newest.AddSeconds(-30))
            .OrderBy(e => e.DurationMs >= 30000 ? 1 : 0)
            .ThenByDescending(LastSound).ThenByDescending(e => e.StartTimeUtc).First();
    }
}

// Monotonic elapsed milliseconds are supplied by the monitor; UTC is only an event label.
public sealed class AudioActivityDetector(AudioEvent metadata)
{
    public AudioEvent? Current
    {
        get; private set;
    }
    private double started, lastSample, silentSince = -1;
    public AudioEvent? Sample(double ms, DateTime utc, float peak, float volume = 1, bool muted = false)
    {
        lastSample = ms;
        AudioEvent? finished = null;
        if (Current != null && silentSince >= 0 && ms - silentSince > 500)
            finished = Finish();
        if (Current == null)
        {
            if (peak < .01f)
                return finished;
            started = ms;
            silentSince = -1;
            Current = metadata with
            {
                Id = Guid.NewGuid().ToString(),
                StartTimeUtc = utc,
                MaxPeak = peak,
                SessionVolume = volume,
                WasMuted = muted,
                SuspicionScore = 100
            };
        }
        else if (peak < .005f)
        {
            if (silentSince < 0)
                silentSince = ms;
        }
        else if (silentSince < 0 || ms - silentSince < 250 || peak >= .01f)
            silentSince = -1;
        var duration = Math.Max(0, (silentSince >= 0 ? silentSince : ms) - started);
        Current = Current with
        {
            DurationMs = duration,
            MaxPeak = Math.Max(Current.MaxPeak, peak),
            SessionVolume = volume,
            WasMuted = Current.WasMuted || muted,
            SuspicionScore = duration < 10000 ? 100 : 0
        };
        return finished;
    }
    public AudioEvent? Finish()
    {
        var e = Current;
        Current = null;
        silentSince = -1;
        return e == null ? null : e with
        {
            EndTimeUtc = e.StartTimeUtc.AddMilliseconds(e.DurationMs)
        };
    }
    public AudioEvent? Snapshot => Current == null ? null : silentSince >= 0 && lastSample - silentSince >= 250 ? Current with { EndTimeUtc = Current.StartTimeUtc.AddMilliseconds(Current.DurationMs) } : Current;
}

