using System.Collections.Concurrent;

namespace AudioCulprit.Core;

// Only this worker waits for storage. Dispose drains all accepted work.
public sealed class HistoryWriter : IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread worker;
    private readonly HistoryRepository repository;
    public event Action<Exception>? Failed;

    public HistoryWriter(HistoryRepository repository)
    {
        this.repository = repository;
        worker = new Thread(Run) { IsBackground = true, Name = "Audio history writer" };
        worker.Start();
    }

    public void Save(AudioEvent item) => queue.Add(() => repository.Save(item));
    public void Prune() => queue.Add(repository.Prune);
    public void Clear()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            try { repository.Clear(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        completion.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            for (var attempt = 0; ; attempt++)
            {
                try { action(); break; }
                catch (Exception ex)
                {
                    if (attempt < 2) { Thread.Sleep(100); continue; }
                    Failed?.Invoke(ex);
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
        worker.Join();
        queue.Dispose();
    }
}
