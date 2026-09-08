using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace AudioCulprit.Core;

public sealed class AudioMonitorService : IDisposable, IMMNotificationClient
{
    private sealed record Session(AudioSessionControl Control, AudioActivityDetector Detector, AudioEvent Metadata);
    private sealed record Device(MMDevice Endpoint, Dictionary<string, Session> Sessions);
    private readonly Dictionary<string, Device> devices = new();
    private readonly ConcurrentQueue<Action> commands = new();
    private readonly Thread worker;
    private readonly ManualResetEventSlim stop = new();
    private volatile bool enabled = true, refresh = true;
    private AudioEvent[] active = [];
    private readonly object lifecycle = new();
    private bool disposed;
    private readonly Func<MMDeviceEnumerator> createEnumerator;
    private readonly Action<MMDeviceEnumerator, IMMNotificationClient> register;
    public Task<int> SetMuteAsync(AudioEvent target, bool muted)
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifecycle)
        {
            if (disposed) return Task.FromException<int>(new ObjectDisposedException(nameof(AudioMonitorService)));
            commands.Enqueue(() =>
            {
                var changed = 0;
                try
                {
                    foreach (var d in devices.Values)
                    foreach (var s in d.Sessions.Values)
                    {
                        var matches = !string.IsNullOrEmpty(target.ProcessPath)
                            ? string.Equals(target.ProcessPath, s.Metadata.ProcessPath, StringComparison.OrdinalIgnoreCase)
                            : target.DeviceId == s.Metadata.DeviceId && target.SessionId == s.Metadata.SessionId;
                        if (!matches || s.Control.State == AudioSessionState.AudioSessionStateExpired) continue;
                        s.Control.SimpleAudioVolume.Mute = muted;
                        changed++;
                    }
                    result.SetResult(changed);
                }
                catch (Exception ex) { result.SetException(ex); }
            });
        }
        return result.Task;
    }
    public IReadOnlyList<AudioEvent> Active => active;
    public string Status { get; private set; } = UiText.Get("Preparing");
    public bool Enabled
    {
        get => enabled; set
        {
            enabled = value;
            commands.Enqueue(() => { if (!value) Flush(); });
        }
    }
    public event Action<AudioEvent>? Completed;
    public AudioMonitorService() : this(() => new MMDeviceEnumerator(), (e, client) => e.RegisterEndpointNotificationCallback(client)) { }
    internal AudioMonitorService(Func<MMDeviceEnumerator> createEnumerator, Action<MMDeviceEnumerator, IMMNotificationClient> register)
    {
        this.createEnumerator = createEnumerator;
        this.register = register;
        worker = new Thread(Run) { IsBackground = true, Name = "Core Audio monitor" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }
    private void Publish(AudioEvent? e)
    {
        if (e != null)
            Completed?.Invoke(e);
    }
    private static AudioEvent Metadata(AudioSessionControl s, MMDevice d)
    {
        var pid = 0;
        var name = "Unknown";
        string? path = null;
        var type = "Unknown";
        try
        {
            pid = checked((int)s.GetProcessID);
            if (s.IsSystemSoundsSession)
            {
                name = "System Sounds";
                type = "SystemSounds";
            }
            else
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName + ".exe";
                type = "Application";
                try
                {
                    path = p.MainModule?.FileName;
                }
                catch { }
            }
        }
        catch { }
        return new AudioEvent { SessionId = s.GetSessionInstanceIdentifier, ProcessId = pid, ProcessName = name, DisplayName = name, ProcessPath = path, EventType = type, DeviceId = d.ID, DeviceName = d.FriendlyName };
    }
    private void Discover(MMDeviceEnumerator enumerator)
    {
        var seen = new HashSet<string>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string? id = null;
            try
            {
                id = endpoint.ID;
                seen.Add(id);
                if (!devices.TryGetValue(id, out var d))
                {
                    d = new Device(endpoint, new());
                    endpoint.AudioSessionManager.OnSessionCreated += (_, session) => commands.Enqueue(() =>
                    {
                        if (devices.TryGetValue(id, out var current) && ReferenceEquals(current, d)) Add(d, new AudioSessionControl(session));
                        else System.Runtime.InteropServices.Marshal.ReleaseComObject(session);
                    });
                    // Register only after activation and subscription succeed.
                    devices.Add(id, d);
                }
                else
                    endpoint.Dispose();
                var collection = d.Endpoint.AudioSessionManager.Sessions;
                for (var i = 0; i < collection.Count; i++)
                    Add(d, collection[i]);
            }
            catch
            {
                if (id != null && devices.ContainsKey(id)) Remove(id);
                endpoint.Dispose();
                // Retry on the next discovery; continue sampling healthy endpoints.
            }
        }
        foreach (var id in devices.Keys.Except(seen).ToArray())
            Remove(id);
    }
    private void Add(Device d, AudioSessionControl s)
    {
        try
        {
            var id = s.GetSessionInstanceIdentifier;
            if (d.Sessions.ContainsKey(id))
            {
                s.Dispose();
                return;
            }
            var metadata = Metadata(s, d.Endpoint);
            d.Sessions.Add(id, new Session(s, new AudioActivityDetector(metadata), metadata));
        }
        catch { s.Dispose(); }
    }
    private void Remove(string id)
    {
        var d = devices[id];
        foreach (var s in d.Sessions.Values)
        {
            Publish(s.Detector.Finish());
            s.Control.Dispose();
        }
        d.Endpoint.Dispose();
        devices.Remove(id);
    }
    private void Flush()
    {
        foreach (var d in devices.Values)
        foreach (var s in d.Sessions.Values)
            Publish(s.Detector.Finish());
        active = [];
    }
    private void Run()
    {
        MMDeviceEnumerator? enumerator = null;
        var registered = false;
        var clock = Stopwatch.StartNew();
        double nextDiscovery = 0;
        try
        {
            while (!stop.IsSet)
            {
                try
                {
                    while (commands.TryDequeue(out var action))
                        action();
                    if (enumerator == null)
                    {
                        try
                        {
                            enumerator = createEnumerator();
                            register(enumerator, this);
                            registered = true;
                        }
                        catch (Exception ex)
                        {
                            Status = UiText.Get("InitializingRetry") + ex.Message;
                            try { enumerator?.Dispose(); } catch { }
                            enumerator = null;
                            stop.Wait(2000);
                            continue;
                        }
                    }
                    var ms = clock.Elapsed.TotalMilliseconds;
                    if (refresh || ms >= nextDiscovery)
                    {
                        refresh = false;
                        nextDiscovery = ms + 2000;
                        Discover(enumerator);
                    }
                    var snapshot = new List<AudioEvent>();
                    if (enabled)
                    foreach (var d in devices.Values.ToArray())
                    foreach (var pair in d.Sessions.ToArray())
                    {
                        var s = pair.Value;
                        try
                        {
                            if (s.Control.State == AudioSessionState.AudioSessionStateExpired)
                            {
                                Publish(s.Detector.Finish());
                                s.Control.Dispose();
                                d.Sessions.Remove(pair.Key);
                                continue;
                            }
                            var v = s.Control.SimpleAudioVolume;
                            Publish(s.Detector.Sample(ms, DateTime.UtcNow, s.Control.AudioMeterInformation.MasterPeakValue, v.Volume, v.Mute));
                            if (s.Detector.Snapshot is { } e)
                                snapshot.Add(e);
                        }
                        catch (System.Runtime.InteropServices.COMException) { Remove(d.Endpoint.ID); refresh = true; break; }
                    }
                    active = snapshot.ToArray();
                    Status = enabled ? devices.Count == 0 ? UiText.Get("WaitingDevice") : UiText.Format("DeviceStatus", devices.Count) : UiText.Get("MonitoringPaused");
                }
                catch (Exception ex) { Status = UiText.Get("MonitoringRetry") + ex.Message; refresh = true; }
                stop.Wait(50);
            }
        }
        finally
        {
            while (commands.TryDequeue(out var action)) { try { action(); } catch { } }
            try { Flush(); } catch { }
            foreach (var id in devices.Keys.ToArray()) { try { Remove(id); } catch { } }
            if (registered) { try { enumerator!.UnregisterEndpointNotificationCallback(this); } catch { } }
            try { enumerator?.Dispose(); } catch { }
        }
    }
    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            stop.Set();
        }
        worker.Join();
        stop.Dispose();
    }
    public void OnDeviceStateChanged(string id, DeviceState state) => refresh = true;
    public void OnDeviceAdded(string id) => refresh = true;
    public void OnDeviceRemoved(string id) => refresh = true;
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => refresh = true;
    public void OnPropertyValueChanged(string id, PropertyKey key) => refresh = true;
}

