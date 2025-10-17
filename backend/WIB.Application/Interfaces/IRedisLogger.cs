using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WIB.Application.Interfaces;

public interface IRedisLogger
{
    /// <summary>
    /// Log a message to Redis stream with specified severity level
    /// </summary>
    Task LogAsync(
        LogSeverity severity,
        string title,
        string message,
        string source,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Log a verbose/debug message
    /// </summary>
    Task VerboseAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Log a debug message
    /// </summary>
    Task DebugAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Log an informational message
    /// </summary>
    Task InfoAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Log a warning message
    /// </summary>
    Task WarningAsync(string title, string message, Dictionary<string, object>? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Log an error message
    /// </summary>
    Task ErrorAsync(string title, string message, Exception? exception = null, Dictionary<string, object>? metadata = null, CancellationToken ct = default);
}

public enum LogSeverity
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}
