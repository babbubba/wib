namespace WIB.Application.Interfaces;

public interface IReceiptQueue
{
    Task EnqueueAsync(string objectKey, CancellationToken ct);
    Task<string?> TryDequeueAsync(CancellationToken ct);
    Task<long> GetLengthAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> PeekAsync(int take, CancellationToken ct);
}
