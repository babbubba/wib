namespace WIB.Application.Interfaces;

public interface IReceiptQueue
{
    Task EnqueueAsync(ReceiptQueueItem item, CancellationToken ct);
    Task<ReceiptQueueItem?> TryDequeueAsync(CancellationToken ct);
    Task<long> GetLengthAsync(CancellationToken ct);
    Task<IReadOnlyList<ReceiptQueueItem>> PeekAsync(int take, CancellationToken ct);
}

public class ReceiptQueueItem
{
    public string ObjectKey { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    
    public ReceiptQueueItem() { }
    
    public ReceiptQueueItem(string objectKey, Guid userId)
    {
        ObjectKey = objectKey;
        UserId = userId;
    }
}
