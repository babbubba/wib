namespace WIB.Application.Interfaces;

public interface INameMatcher
{
    Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct);
    Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct);
}

