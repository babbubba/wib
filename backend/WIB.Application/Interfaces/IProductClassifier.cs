namespace WIB.Application.Interfaces;

public interface IProductClassifier
{
    Task<(Guid? TypeId, Guid? CategoryId, float Confidence)> PredictAsync(string labelRaw, CancellationToken ct);
    Task FeedbackAsync(string labelRaw, string? brand, Guid typeId, Guid? categoryId, CancellationToken ct);
}
