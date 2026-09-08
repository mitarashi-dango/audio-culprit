namespace AudioCulprit.Core;

// Independent of the storage lock, including events still waiting to be saved.
public sealed class HistorySnapshot
{
    private readonly object gate = new();
    private readonly Dictionary<string, AudioEvent> events = new();
    private IgnoreRule[] rules = [];
    public void Add(AudioEvent item) { lock (gate) events[item.Id] = item; }
    public void SetRules(IEnumerable<IgnoreRule> items) { lock (gate) rules = items.ToArray(); }
    public void Clear() { lock (gate) events.Clear(); }
    public AudioEvent[] ReadAll() { lock (gate) return events.Values.ToArray(); }
    public void Remove(IEnumerable<AudioEvent> items)
    {
        lock (gate)
            foreach (var item in items)
                if (events.TryGetValue(item.Id, out var current) && current == item) events.Remove(item.Id);
    }
    public AudioEvent[] Read()
    {
        lock (gate)
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            return events.Values.Where(e => e.StartTimeUtc >= cutoff)
                .OrderByDescending(e => e.StartTimeUtc).Take(10000).ToArray();
        }
    }
    public IgnoreRule[] ReadRules() { lock (gate) return rules.ToArray(); }
    public AudioEvent? Latest(IEnumerable<AudioEvent> active)
    {
        lock (gate)
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var current = active.ToArray();
            var ids = current.Select(e => e.Id).ToHashSet();
            return SuspicionEvaluator.Latest(current.Concat(events.Values.Where(e => e.StartTimeUtc >= cutoff && !ids.Contains(e.Id))), rules);
        }
    }
    public void Prune()
    {
        lock (gate)
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var item in events.Values.OrderByDescending(e => e.StartTimeUtc).ToArray().Select((e, index) => (e, index)))
                if (item.index >= 10000 || item.e.StartTimeUtc < cutoff) events.Remove(item.e.Id);
        }
    }
}
