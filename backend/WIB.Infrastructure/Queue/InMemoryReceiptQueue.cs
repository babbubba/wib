using System.Collections.Concurrent;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Queue;

public class InMemoryReceiptQueue : IReceiptQueue
{
    private readonly ConcurrentQueue<ReceiptQueueItem> _queue = new();

    public Task EnqueueAsync(ReceiptQueueItem item, CancellationToken ct)
    {
        _queue.Enqueue(item);
        return Task.CompletedTask;
    }

    public Task<ReceiptQueueItem?> TryDequeueAsync(CancellationToken ct)
    {
        return Task.FromResult(_queue.TryDequeue(out var item) ? item : null);
    }

    public Task<long> GetLengthAsync(CancellationToken ct)
    {
        return Task.FromResult((long)_queue.Count);
    }

    public Task<IReadOnlyList<ReceiptQueueItem>> PeekAsync(int take, CancellationToken ct)
    {
        if (take <= 0) take = 10;
        var arr = _queue.ToArray();
        return Task.FromResult((IReadOnlyList<ReceiptQueueItem>)arr.Take(take).ToList());
    }
}
