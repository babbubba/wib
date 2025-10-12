using System.Net.Http.Headers;
using System.Text.Json;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Clients;

public class OcrClient : IOcrClient
{
    private readonly HttpClient _http;

    public OcrClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> ExtractAsync(Stream image, CancellationToken ct)
    {
        // Copy the incoming stream into a separate MemoryStream so that disposing
        // the HTTP content does not close the caller's stream (which is reused later).
        var temp = new MemoryStream();
        if (image.CanSeek)
        {
            try { image.Position = 0; } catch { /* ignore */ }
        }
        await image.CopyToAsync(temp, ct);
        temp.Position = 0;

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(temp);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent, "file", "receipt.jpg");

        using var resp = await _http.PostAsync("/extract", content, ct);
        resp.EnsureSuccessStatusCode();
        using var s = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("text", out var textProp))
        {
            return textProp.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}
