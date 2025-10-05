using System.Collections.Concurrent;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Queue;

public class InMemoryReceiptQueue : IReceiptQueue
{
    private readonly ConcurrentQueue<string> _queue = new();

    public Task EnqueueAsync(string objectKey, CancellationToken ct)
    {
        _queue.Enqueue(objectKey);
        return Task.CompletedTask;
    }

    public Task<string?> TryDequeueAsync(CancellationToken ct)
    {
        return Task.FromResult(_queue.TryDequeue(out var item) ? item : null);
    }

    public Task<long> GetLengthAsync(CancellationToken ct)
    {
        return Task.FromResult((long)_queue.Count);
    }

    public Task<IReadOnlyList<string>> PeekAsync(int take, CancellationToken ct)
    {
        if (take <= 0) take = 10;
        var arr = _queue.ToArray();
        return Task.FromResult((IReadOnlyList<string>)arr.Take(take).ToList());
    }
}
