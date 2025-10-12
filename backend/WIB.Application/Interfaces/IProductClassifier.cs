using WIB.Application.Contracts.Ml;

namespace WIB.Application.Interfaces;

public interface IProductClassifier
{
    Task<MlPredictionResult> PredictAsync(string labelRaw, CancellationToken ct);
    Task FeedbackAsync(string labelRaw, string? brand, Guid typeId, Guid? categoryId, CancellationToken ct);
}
