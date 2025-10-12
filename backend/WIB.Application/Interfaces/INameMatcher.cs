namespace WIB.Application.Interfaces;

public interface INameMatcher
{
    Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct);
    Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct);
    
    /// <summary>
    /// Enhanced store matching using additional location data (address, city, VAT number)
    /// for improved accuracy across all types of commercial activities
    /// </summary>
    Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, string? address, string? city, string? vatNumber, CancellationToken ct);
}

