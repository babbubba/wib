using System.Text.Json.Serialization;

namespace WIB.Application.Contracts.Monitoring;

public class LogEntryDto
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

public class ServiceStatusDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown"; // "running", "stopped", "unhealthy"

    [JsonPropertyName("uptime")]
    public string? Uptime { get; set; }

    [JsonPropertyName("lastCheck")]
    public string LastCheck { get; set; } = string.Empty;
}
