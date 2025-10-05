using StackExchange.Redis;
using System.Threading;
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

    public async Task EnqueueAsync(string objectKey, CancellationToken ct)
        => await _db.ListRightPushAsync(_key, objectKey);

    public async Task<string?> TryDequeueAsync(CancellationToken ct)
    {
        var value = await _db.ListLeftPopAsync(_key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<long> GetLengthAsync(CancellationToken ct)
        => await _db.ListLengthAsync(_key);

    public async Task<IReadOnlyList<string>> PeekAsync(int take, CancellationToken ct)
    {
        if (take <= 0) take = 10;
        var vals = await _db.ListRangeAsync(_key, 0, take - 1);
        return vals.Select(v => v.HasValue ? v.ToString() : string.Empty).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        _conn.Dispose();
    }
}
