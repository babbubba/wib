using StackExchange.Redis;
using System.Threading;
using System.Text.Json;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Queue;

public class RedisReceiptQueue : IReceiptQueue, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _conn;
    private readonly IDatabase _db;
    private readonly string _key;

    public RedisReceiptQueue(string connectionString, string key = "wib:receipts")
    {
        _key = key;
        _conn = ConnectWithRetry(connectionString);
        _db = _conn.GetDatabase();
    }

    private static ConnectionMultiplexer ConnectWithRetry(string connectionString)
    {
        var attempts = 0;
        var maxAttempts = 30;
        Exception? last = null;
        while (attempts++ < maxAttempts)
        {
            try
            {
                var options = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = Math.Max(1, options.ConnectRetry);
                options.ConnectTimeout = Math.Max(5000, options.ConnectTimeout);
                return ConnectionMultiplexer.Connect(options);
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(2000);
            }
        }
        throw new InvalidOperationException($"Unable to connect to Redis after {maxAttempts} attempts", last);
    }

    public async Task EnqueueAsync(ReceiptQueueItem item, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(item);
        await _db.ListRightPushAsync(_key, json);
    }

    public async Task<ReceiptQueueItem?> TryDequeueAsync(CancellationToken ct)
    {
        var value = await _db.ListLeftPopAsync(_key);
        if (!value.HasValue) return null;
        
        try
        {
            return JsonSerializer.Deserialize<ReceiptQueueItem>(value.ToString()!);
        }
        catch (JsonException)
        {
            // Handle legacy format (plain objectKey string) for backward compatibility
            return new ReceiptQueueItem(value.ToString()!, Guid.Empty);
        }
    }

    public async Task<long> GetLengthAsync(CancellationToken ct)
        => await _db.ListLengthAsync(_key);

    public async Task<IReadOnlyList<ReceiptQueueItem>> PeekAsync(int take, CancellationToken ct)
    {
        if (take <= 0) take = 10;
        var vals = await _db.ListRangeAsync(_key, 0, take - 1);
        var items = new List<ReceiptQueueItem>();
        
        foreach (var val in vals)
        {
            if (!val.HasValue) continue;
            
            try
            {
                var item = JsonSerializer.Deserialize<ReceiptQueueItem>(val.ToString()!);
                if (item != null) items.Add(item);
            }
            catch (JsonException)
            {
                // Handle legacy format
                items.Add(new ReceiptQueueItem(val.ToString()!, Guid.Empty));
            }
        }
        
        return items;
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        _conn.Dispose();
    }
}
