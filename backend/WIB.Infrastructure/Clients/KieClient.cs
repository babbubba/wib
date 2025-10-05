using System.Net.Http.Json;
using WIB.Application.Contracts.Kie;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Clients;

public class KieClient : IKieClient
{
    private readonly HttpClient _http;

    public KieClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ReceiptDraft> ExtractFieldsAsync(string ocrResult, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync("/kie", new { text = ocrResult }, ct);
        resp.EnsureSuccessStatusCode();
        var draft = await resp.Content.ReadFromJsonAsync<ReceiptDraft>(cancellationToken: ct);
        return draft ?? new ReceiptDraft();
    }
}
