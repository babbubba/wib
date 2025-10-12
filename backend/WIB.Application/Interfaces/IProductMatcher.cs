using WIB.Domain;

namespace WIB.Application.Interfaces;

/// <summary>
/// Service for matching receipt lines to products and creating new products when needed
/// </summary>
public interface IProductMatcher
{
    /// <summary>
    /// Finds an existing product or creates a new one based on receipt line data and ML predictions
    /// </summary>
    /// <param name="labelRaw">Raw label from receipt</param>
    /// <param name="brand">Optional brand information</param>
    /// <param name="predictedTypeId">ML predicted product type</param>
    /// <param name="predictedCategoryId">ML predicted category</param>
    /// <param name="confidence">ML prediction confidence</param>
    /// <param name="confidenceThreshold">Minimum confidence threshold for automatic matching</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>
    /// ProductMatch result with matched/created product and match confidence
    /// Returns null if confidence is below threshold and no exact match found
    /// </returns>
    Task<ProductMatch?> FindOrCreateProductAsync(
        string labelRaw, 
        string? brand, 
        Guid? predictedTypeId, 
        Guid? predictedCategoryId, 
        float confidence,
        float confidenceThreshold = 0.8f,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new product with the provided information
    /// </summary>
    Task<Product> CreateProductAsync(
        string name,
        string? brand,
        Guid? productTypeId,
        Guid? categoryId,
        string? gtin = null,
        CancellationToken ct = default);

    /// <summary>
    /// Finds products by name similarity and type/category match
    /// </summary>
    Task<List<Product>> FindSimilarProductsAsync(
        string labelRaw,
        Guid? typeId,
        Guid? categoryId,
        int maxResults = 5,
        CancellationToken ct = default);
}

/// <summary>
/// Result of product matching operation
/// </summary>
public class ProductMatch
{
    public Product Product { get; set; } = null!;
    public float MatchConfidence { get; set; }
    public ProductMatchType MatchType { get; set; }
    public string? MatchReason { get; set; }
}

/// <summary>
/// Type of product match found
/// </summary>
public enum ProductMatchType
{
    /// <summary>Exact match found by name and type/category</summary>
    ExactMatch,
    
    /// <summary>Similar product found with high confidence</summary>
    SimilarMatch,
    
    /// <summary>New product created</summary>
    NewProduct,
    
    /// <summary>Match based on alias/brand</summary>
    AliasMatch
}