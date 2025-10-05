using System.Net.Http.Json;
using WIB.Application.Contracts.Ml;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Clients;

public class ProductClassifier : IProductClassifier
{
    private readonly HttpClient _http;

    public ProductClassifier(HttpClient http)
    {
        _http = http;
    }

    public async Task<(Guid? TypeId, Guid? CategoryId, float Confidence)> PredictAsync(string labelRaw, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync("/predict", new { labelRaw }, ct);
        resp.EnsureSuccessStatusCode();
        var sugg = await resp.Content.ReadFromJsonAsync<MlSuggestionsResponse>(cancellationToken: ct);
        Guid? typeId = sugg?.TypeCandidates?.FirstOrDefault()?.Id;
        Guid? catId = sugg?.CategoryCandidates?.FirstOrDefault()?.Id;
        float conf = 0f;
        if (sugg != null)
        {
            var t = sugg.TypeCandidates.FirstOrDefault()?.Conf ?? 0f;
            var c = sugg.CategoryCandidates.FirstOrDefault()?.Conf ?? 0f;
            conf = Math.Max(t, c);
        }
        return (typeId, catId, conf);
    }

    public async Task FeedbackAsync(string labelRaw, string? brand, Guid typeId, Guid? categoryId, CancellationToken ct)
    {
        var req = new MlFeedbackRequest
        {
            LabelRaw = labelRaw,
            Brand = brand,
            FinalTypeId = typeId,
            FinalCategoryId = categoryId
        };
        using var resp = await _http.PostAsJsonAsync("/feedback", req, ct);
        resp.EnsureSuccessStatusCode();
    }
}
