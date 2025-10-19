using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using WIB.Application.Contracts.Monitoring;
using WIB.Infrastructure.Logging;

namespace WIB.API.Controllers;

[ApiController]
[Route("monitoring")]
[Authorize(Roles = "wmc")]
public class MonitoringController : ControllerBase
{
    private readonly RedisLogConsumer _logConsumer;
    private readonly ILogger<MonitoringController> _logger;
    private readonly IConfiguration _configuration;

    public MonitoringController(RedisLogConsumer logConsumer, ILogger<MonitoringController> logger, IConfiguration configuration)
    {
        _logConsumer = logConsumer;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Server-Sent Events endpoint for real-time log streaming
    /// </summary>
    [HttpGet("logs/stream")]
    public async Task StreamLogs([FromQuery] string? level = null, [FromQuery] string? source = null, CancellationToken ct = default)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering for SSE

        var heartbeatInterval = TimeSpan.FromSeconds(15);

        try
        {
            _logger.LogInformation("Starting log stream for user, level={Level}, source={Source}", level ?? "all", source ?? "all");

            await Response.WriteAsync(": connected\n\n", ct);
            await Response.Body.FlushAsync(ct);

            await using var enumerator = _logConsumer.StreamLogsAsync(level, source, ct).GetAsyncEnumerator(ct);
            var moveNextTask = enumerator.MoveNextAsync().AsTask();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await moveNextTask.WaitAsync(heartbeatInterval, ct))
                    {
                        break;
                    }
                }
                catch (TimeoutException)
                {
                    await Response.WriteAsync(": keep-alive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var logEntry = enumerator.Current;
                var json = JsonSerializer.Serialize(logEntry);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                moveNextTask = enumerator.MoveNextAsync().AsTask();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Log stream cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming logs");
        }
    }

    /// <summary>
    /// Query recent logs from Redis stream
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<IEnumerable<LogEntryDto>>> GetRecentLogs(
        [FromQuery] int limit = 100,
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        CancellationToken ct = default)
    {
        if (limit <= 0 || limit > 500)
            limit = 100;

        try
        {
            var logs = await _logConsumer.QueryRecentLogsAsync(limit, level, source, ct);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying recent logs");
            return StatusCode(500, new { error = "Failed to query logs" });
        }
    }

    /// <summary>
    /// Get count of recent error logs (last 5 minutes)
    /// </summary>
    [HttpGet("logs/error-count")]
    public async Task<ActionResult<int>> GetErrorCount(CancellationToken ct = default)
    {
        try
        {
            var count = await _logConsumer.GetRecentErrorCountAsync(ct);
            return Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting error count");
            return Ok(0); // Return 0 on error to avoid breaking the UI
        }
    }

    /// <summary>
    /// Get service status for all microservices
    /// </summary>
    [HttpGet("services/status")]
    public async Task<ActionResult<IEnumerable<ServiceStatusDto>>> GetServiceStatus(CancellationToken ct = default)
    {
        var services = new[]
        {
            new { Name = "api", Endpoint = "http://localhost:8080/health/ready" },
            new { Name = "worker", Endpoint = (string?)null }, // Worker doesn't have HTTP endpoint
            new { Name = "ocr", Endpoint = _configuration["Ocr:Endpoint"] + "/health" ?? "http://ocr:8081/health" },
            new { Name = "ml", Endpoint = _configuration["Ml:Endpoint"] + "/health" ?? "http://ml:8082/health" }
        };

        var statusList = new List<ServiceStatusDto>();

        foreach (var service in services)
        {
            var status = new ServiceStatusDto
            {
                Name = service.Name,
                LastCheck = DateTime.UtcNow.ToString("o")
            };

            if (service.Name == "api")
            {
                // API is always running if this endpoint is being hit
                status.Status = "running";
                status.Uptime = "N/A";
            }
            else if (service.Name == "worker")
            {
                // Worker status is inferred from recent log activity
                try
                {
                    var recentLogs = await _logConsumer.QueryRecentLogsAsync(10, null, "worker", ct);
                    var latestLog = recentLogs.FirstOrDefault();

                    if (latestLog != null && DateTime.TryParse(latestLog.Timestamp, out var timestamp))
                    {
                        var age = DateTime.UtcNow - timestamp;
                        if (age.TotalMinutes < 5)
                        {
                            status.Status = "running";
                            status.Uptime = $"Last activity {age.TotalMinutes:F0}m ago";
                        }
                        else
                        {
                            status.Status = "unhealthy";
                            status.Uptime = $"No activity for {age.TotalMinutes:F0}m";
                        }
                    }
                    else
                    {
                        status.Status = "unknown";
                        status.Uptime = "No recent logs";
                    }
                }
                catch
                {
                    status.Status = "unknown";
                    status.Uptime = "N/A";
                }
            }
            else if (service.Endpoint != null)
            {
                // HTTP health check for OCR and ML services
                try
                {
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var response = await httpClient.GetAsync(service.Endpoint, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        status.Status = "running";
                        status.Uptime = "N/A";
                    }
                    else
                    {
                        status.Status = "unhealthy";
                        status.Uptime = $"HTTP {(int)response.StatusCode}";
                    }
                }
                catch (TaskCanceledException)
                {
                    status.Status = "unhealthy";
                    status.Uptime = "Timeout";
                }
                catch (HttpRequestException)
                {
                    status.Status = "stopped";
                    status.Uptime = "Unreachable";
                }
                catch
                {
                    status.Status = "unknown";
                    status.Uptime = "N/A";
                }
            }

            statusList.Add(status);
        }

        return Ok(statusList);
    }
}
