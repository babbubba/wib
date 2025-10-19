using StackExchange.Redis;
using System.Text.Json;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Logging;

public class RedisLogger : IRedisLogger, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _conn;
    private readonly IDatabase _db;
    private readonly string _streamKey;
    private readonly string _source;
    private readonly int _maxStreamLength;
    private readonly LogSeverity _minLogLevel;
    private readonly bool _enabled;

    public RedisLogger(string connectionString, string source, string streamKey = "app_logs", int maxStreamLength = 10000, LogSeverity minLogLevel = LogSeverity.Info)
    {
        _streamKey = streamKey;
        _source = source;
        _maxStreamLength = maxStreamLength;
        _minLogLevel = minLogLevel;

        try
        {
            _conn = ConnectWithRetry(connectionString);
            _db = _conn.GetDatabase();
            _enabled = true;
        }
        catch (Exception ex)
        {
            // Fallback to console logging if Redis is unavailable
            Console.Error.WriteLine($"[RedisLogger] Failed to connect to Redis: {ex.Message}");
            Console.Error.WriteLine("[RedisLogger] Falling back to console-only logging");
            _enabled = false;
            _conn = null!;
            _db = null!;
        }
    }

    private static ConnectionMultiplexer ConnectWithRetry(string connectionString)
    {
        var attempts = 0;
        var maxAttempts = 10;
        Exception? last = null;
        while (attempts++ < maxAttempts)
        {
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = Math.Max(1, options.ConnectRetry);
                options.ConnectTimeout = Math.Max(5000, options.ConnectTimeout);
                return ConnectionMultiplexer.Connect(options);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempts < maxAttempts)
                    Thread.Sleep(1000);
            }
        }
        throw new InvalidOperationException($"Unable to connect to Redis after {maxAttempts} attempts", last);
    }

    public async Task LogAsync(
        LogSeverity severity,
        string title,
        string message,
        string source,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        // Check if log level is enabled
        if (severity < _minLogLevel)
            return;

        var logEntry = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            level = severity.ToString().ToUpperInvariant(),
            source = source,
            title = title,
            message = message,
            metadata = metadata ?? new Dictionary<string, object>()
        };

        // Always log to console as fallback
        var consoleColor = severity switch
        {
            LogSeverity.Error => ConsoleColor.Red,
            LogSeverity.Warning => ConsoleColor.Yellow,
            LogSeverity.Info => ConsoleColor.Cyan,
            LogSeverity.Debug => ConsoleColor.Gray,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = consoleColor;
            Console.WriteLine($"[{logEntry.timestamp}] [{logEntry.level}] [{logEntry.source}] {logEntry.title}: {logEntry.message}");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }

        if (!_enabled)
            return;

        try
        {
            var json = JsonSerializer.Serialize(logEntry);
            var nameValueEntries = new NameValueEntry[]
            {
                new("json", json)
            };

            // Use XADD with MAXLEN to automatically trim the stream
            await _db.StreamAddAsync(_streamKey, nameValueEntries, maxLength: _maxStreamLength, useApproximateMaxLength: true);
        }
        catch (Exception ex)
        {
            // NEVER throw from logger - just log to console
            Console.Error.WriteLine($"[RedisLogger] Failed to publish log: {ex.Message}");
        }
    }

    public Task VerboseAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
        => LogAsync(LogSeverity.Verbose, title, message, _source, metadata, ct);

    public Task DebugAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
        => LogAsync(LogSeverity.Debug, title, message, _source, metadata, ct);

    public Task InfoAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
        => LogAsync(LogSeverity.Info, title, message, _source, metadata, ct);

    public Task WarningAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
        => LogAsync(LogSeverity.Warning, title, message, _source, metadata, ct);

    public Task ErrorAsync(string title, string message, Exception? exception = null, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        metadata ??= new Dictionary<string, object>();

        if (exception != null)
        {
            metadata["exceptionType"] = exception.GetType().Name;
            metadata["exceptionMessage"] = exception.Message;
            metadata["stackTrace"] = exception.StackTrace ?? string.Empty;

            if (exception.InnerException != null)
            {
                metadata["innerExceptionType"] = exception.InnerException.GetType().Name;
                metadata["innerExceptionMessage"] = exception.InnerException.Message;
            }
        }

        return LogAsync(LogSeverity.Error, title, message, _source, metadata, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_enabled && _conn != null)
        {
            await _conn.CloseAsync();
            _conn.Dispose();
        }
    }
}
