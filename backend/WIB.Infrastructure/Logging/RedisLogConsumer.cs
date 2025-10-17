using StackExchange.Redis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WIB.Application.Contracts.Monitoring;

namespace WIB.Infrastructure.Logging;

public class RedisLogConsumer : IAsyncDisposable
{
    private readonly ConnectionMultiplexer _conn;
    private readonly IDatabase _db;
    private readonly string _streamKey;

    public RedisLogConsumer(string connectionString, string streamKey = "app_logs")
    {
        _streamKey = streamKey;
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        _conn = ConnectionMultiplexer.Connect(options);
        _db = _conn.GetDatabase();
    }

    /// <summary>
    /// Stream logs in real-time from Redis stream starting from the last ID
    /// </summary>
    public async IAsyncEnumerable<LogEntryDto> StreamLogsAsync(
        string? level = null,
        string? source = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastId = "$"; // Start from the newest messages

        while (!ct.IsCancellationRequested)
        {
            StreamEntry[] entries;
            try
            {
                entries = await _db.StreamReadAsync(_streamKey, lastId, count: 100);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RedisLogConsumer] Error streaming logs: {ex.Message}");
                await Task.Delay(2000, ct);
                continue;
            }

            foreach (var entry in entries)
            {
                lastId = entry.Id;

                var json = entry.Values.FirstOrDefault(v => v.Name == "json").Value;
                if (json.IsNullOrEmpty)
                    continue;

                LogEntryDto? logEntry;
                try
                {
                    logEntry = JsonSerializer.Deserialize<LogEntryDto>(json.ToString());
                }
                catch (JsonException)
                {
                    continue;
                }

                if (logEntry == null)
                    continue;

                // Apply filters
                if (!string.IsNullOrEmpty(level) && !logEntry.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(source) && !logEntry.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return logEntry;
            }

            // If no new messages, wait a bit before polling again
            if (entries.Length == 0)
            {
                await Task.Delay(500, ct);
            }
        }
    }

    /// <summary>
    /// Query recent logs from Redis stream
    /// </summary>
    public async Task<List<LogEntryDto>> QueryRecentLogsAsync(
        int limit = 100,
        string? level = null,
        string? source = null,
        CancellationToken ct = default)
    {
        try
        {
            // Read last N messages from stream
            var entries = await _db.StreamReadAsync(_streamKey, "0-0", count: limit * 2); // Read more to account for filtering

            var logs = new List<LogEntryDto>();

            foreach (var entry in entries.Reverse()) // Most recent first
            {
                var json = entry.Values.FirstOrDefault(v => v.Name == "json").Value;
                if (json.IsNullOrEmpty)
                    continue;

                LogEntryDto? logEntry;
                try
                {
                    logEntry = JsonSerializer.Deserialize<LogEntryDto>(json.ToString());
                }
                catch (JsonException)
                {
                    continue;
                }

                if (logEntry == null)
                    continue;

                // Apply filters
                if (!string.IsNullOrEmpty(level) && !logEntry.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(source) && !logEntry.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                    continue;

                logs.Add(logEntry);

                if (logs.Count >= limit)
                    break;
            }

            return logs;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RedisLogConsumer] Error querying logs: {ex.Message}");
            return new List<LogEntryDto>();
        }
    }

    /// <summary>
    /// Get count of recent error logs (last 5 minutes)
    /// </summary>
    public async Task<int> GetRecentErrorCountAsync(CancellationToken ct = default)
    {
        try
        {
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
            var entries = await _db.StreamReadAsync(_streamKey, "0-0", count: 500);

            var count = 0;
            foreach (var entry in entries.Reverse())
            {
                var json = entry.Values.FirstOrDefault(v => v.Name == "json").Value;
                if (json.IsNullOrEmpty)
                    continue;

                LogEntryDto? logEntry;
                try
                {
                    logEntry = JsonSerializer.Deserialize<LogEntryDto>(json.ToString());
                }
                catch (JsonException)
                {
                    continue;
                }

                if (logEntry == null)
                    continue;

                // Check if log is from the last 5 minutes
                if (DateTime.TryParse(logEntry.Timestamp, out var timestamp))
                {
                    if (timestamp < fiveMinutesAgo)
                        continue;

                    if (logEntry.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                        count++;
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RedisLogConsumer] Error getting error count: {ex.Message}");
            return 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        _conn.Dispose();
    }
}
