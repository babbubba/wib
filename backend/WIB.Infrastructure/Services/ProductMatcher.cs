using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class ProductMatcher : IProductMatcher
{
    private readonly WibDbContext _db;
    private static readonly Regex BrandExtractorRegex = new(@"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b", RegexOptions.Compiled);
    private static readonly Regex ProductCleanerRegex = new(@"\b(kg|g|ml|l|pz|pezzi|confezione|conf\.?|x\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ProductMatcher(WibDbContext db)
    {
        _db = db;
    }

    public async Task<ProductMatch?> FindOrCreateProductAsync(
        string labelRaw, 
        string? brand, 
        Guid? predictedTypeId, 
        Guid? predictedCategoryId, 
        float confidence,
        float confidenceThreshold = 0.8f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(labelRaw))
            return null;

        var cleanLabel = CleanProductLabel(labelRaw);
        var extractedBrand = brand ?? ExtractBrandFromLabel(labelRaw);

        // 1. Try exact match first
        var exactMatch = await TryExactMatchAsync(cleanLabel, extractedBrand, predictedTypeId, predictedCategoryId, ct);
        if (exactMatch != null)
            return exactMatch;

        // 2. Try alias match
        var aliasMatch = await TryAliasMatchAsync(cleanLabel, extractedBrand, predictedTypeId, predictedCategoryId, ct);
        if (aliasMatch != null)
            return aliasMatch;

        // 3. Try similar products if confidence is high enough
        if (confidence >= confidenceThreshold)
        {
            var similarMatch = await TrySimilarMatchAsync(cleanLabel, extractedBrand, predictedTypeId, predictedCategoryId, ct);
            if (similarMatch != null && similarMatch.MatchConfidence >= confidenceThreshold)
                return similarMatch;
        }

        // 4. Create new product if we have enough information and confidence
        if (confidence >= confidenceThreshold && (predictedTypeId.HasValue || predictedCategoryId.HasValue))
        {
            var newProduct = await CreateProductAsync(cleanLabel, extractedBrand, predictedTypeId, predictedCategoryId, ct: ct);
            
            return new ProductMatch
            {
                Product = newProduct,
                MatchConfidence = confidence,
                MatchType = ProductMatchType.NewProduct,
                MatchReason = $"Created new product with {confidence:P1} confidence"
            };
        }

        // 5. Return null if confidence too low or no type/category predicted
        return null;
    }

    public async Task<Product> CreateProductAsync(
        string name, 
        string? brand, 
        Guid? productTypeId, 
        Guid? categoryId, 
        string? gtin = null, 
        CancellationToken ct = default)
    {
        var normalizedName = NormalizeName(name);
        
        var product = new Product
        {
            Name = normalizedName,
            Brand = string.IsNullOrWhiteSpace(brand) ? null : NormalizeName(brand),
            GTIN = gtin,
            ProductTypeId = productTypeId ?? await GetDefaultProductTypeAsync(ct),
            CategoryId = categoryId
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        // Create aliases for better future matching
        await CreateProductAliasesAsync(product, name, brand, ct);

        return product;
    }

    public async Task<List<Product>> FindSimilarProductsAsync(
        string labelRaw, 
        Guid? typeId, 
        Guid? categoryId, 
        int maxResults = 5, 
        CancellationToken ct = default)
    {
        var cleanLabel = CleanProductLabel(labelRaw);
        var searchWords = ExtractKeywords(cleanLabel);

        var query = _db.Products.AsQueryable();

        // Filter by type and category if provided
        if (typeId.HasValue)
            query = query.Where(p => p.ProductTypeId == typeId);

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId);

        var products = await query
            .Include(p => p.Aliases)
            .ToListAsync(ct);

        // Calculate similarity scores
        var scoredProducts = products
            .Select(p => new { Product = p, Score = CalculateSimilarityScore(cleanLabel, searchWords, p) })
            .Where(x => x.Score > 0.3f) // Minimum similarity threshold
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Product)
            .ToList();

        return scoredProducts;
    }

    #region Private Methods

    private async Task<ProductMatch?> TryExactMatchAsync(
        string cleanLabel, 
        string? brand, 
        Guid? typeId, 
        Guid? categoryId, 
        CancellationToken ct)
    {
        var query = _db.Products.AsQueryable();

        // Match by name
        query = query.Where(p => p.Name.ToLower() == cleanLabel.ToLower());

        // Match brand if provided
        if (!string.IsNullOrWhiteSpace(brand))
            query = query.Where(p => p.Brand != null && p.Brand.ToLower() == brand.ToLower());

        // Prefer products with matching type/category
        var exactProduct = await query
            .OrderByDescending(p => p.ProductTypeId == typeId ? 1 : 0)
            .ThenByDescending(p => p.CategoryId == categoryId ? 1 : 0)
            .FirstOrDefaultAsync(ct);

        if (exactProduct != null)
        {
            return new ProductMatch
            {
                Product = exactProduct,
                MatchConfidence = 0.95f,
                MatchType = ProductMatchType.ExactMatch,
                MatchReason = "Exact name and brand match"
            };
        }

        return null;
    }

    private async Task<ProductMatch?> TryAliasMatchAsync(
        string cleanLabel,
        string? brand,
        Guid? typeId,
        Guid? categoryId,
        CancellationToken ct)
    {
        var aliasProduct = await _db.ProductAliases
            .Include(a => a.Product)
            .Where(a => a.Alias.ToLower() == cleanLabel.ToLower())
            .Select(a => a.Product)
            .FirstOrDefaultAsync(ct);

        if (aliasProduct != null)
        {
            // Check if type/category matches (bonus for confidence)
            var typeMatch = !typeId.HasValue || aliasProduct.ProductTypeId == typeId;
            var categoryMatch = !categoryId.HasValue || aliasProduct.CategoryId == categoryId;
            
            var confidence = 0.85f;
            if (typeMatch && categoryMatch) confidence = 0.92f;
            else if (typeMatch || categoryMatch) confidence = 0.88f;

            return new ProductMatch
            {
                Product = aliasProduct,
                MatchConfidence = confidence,
                MatchType = ProductMatchType.AliasMatch,
                MatchReason = "Matched via product alias"
            };
        }

        return null;
    }

    private async Task<ProductMatch?> TrySimilarMatchAsync(
        string cleanLabel,
        string? brand,
        Guid? typeId,
        Guid? categoryId,
        CancellationToken ct)
    {
        var similarProducts = await FindSimilarProductsAsync(cleanLabel, typeId, categoryId, 3, ct);
        
        if (similarProducts.Any())
        {
            var bestMatch = similarProducts.First();
            var keywords = ExtractKeywords(cleanLabel);
            var similarity = CalculateSimilarityScore(cleanLabel, keywords, bestMatch);

            if (similarity >= 0.7f)
            {
                return new ProductMatch
                {
                    Product = bestMatch,
                    MatchConfidence = similarity,
                    MatchType = ProductMatchType.SimilarMatch,
                    MatchReason = $"Similar product found ({similarity:P1} similarity)"
                };
            }
        }

        return null;
    }

    private string CleanProductLabel(string labelRaw)
    {
        if (string.IsNullOrWhiteSpace(labelRaw))
            return string.Empty;

        // Remove common packaging/size indicators
        var cleaned = ProductCleanerRegex.Replace(labelRaw, " ");
        
        // Remove extra whitespace and normalize
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        
        return cleaned;
    }

    private string? ExtractBrandFromLabel(string labelRaw)
    {
        if (string.IsNullOrWhiteSpace(labelRaw))
            return null;

        // Look for capitalized words that might be brands
        var matches = BrandExtractorRegex.Matches(labelRaw);
        if (matches.Count > 0)
        {
            // Return the first capitalized sequence as potential brand
            return matches[0].Value;
        }

        return null;
    }

    private List<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        return text.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2) // Skip very short words
            .Distinct()
            .ToList();
    }

    private float CalculateSimilarityScore(string searchLabel, List<string> searchWords, Product product)
    {
        var productWords = ExtractKeywords(product.Name);
        var aliasWords = product.Aliases?.SelectMany(a => ExtractKeywords(a.Alias)).ToList() ?? new List<string>();
        var brandWords = string.IsNullOrEmpty(product.Brand) ? new List<string>() : ExtractKeywords(product.Brand);

        var allProductWords = productWords.Concat(aliasWords).Concat(brandWords).Distinct().ToList();

        if (!searchWords.Any() || !allProductWords.Any())
            return 0f;

        // Jaccard similarity (intersection over union)
        var intersection = searchWords.Intersect(allProductWords).Count();
        var union = searchWords.Union(allProductWords).Count();

        var jaccardScore = (float)intersection / union;

        // Boost score for exact name matches
        if (product.Name.Equals(searchLabel, StringComparison.OrdinalIgnoreCase))
            jaccardScore = Math.Max(jaccardScore, 0.95f);

        // Boost score for brand matches
        if (!string.IsNullOrEmpty(product.Brand) && 
            searchLabel.Contains(product.Brand, StringComparison.OrdinalIgnoreCase))
            jaccardScore += 0.1f;

        return Math.Min(jaccardScore, 1.0f);
    }

    private string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Trim().ToLowerInvariant());
    }

    private async Task<Guid> GetDefaultProductTypeAsync(CancellationToken ct)
    {
        // Try to find a "Generic" or "Unknown" product type, or create one
        var genericType = await _db.ProductTypes
            .FirstOrDefaultAsync(pt => pt.Name.ToLower().Contains("generic") || pt.Name.ToLower().Contains("unknown"), ct);

        if (genericType != null)
            return genericType.Id;

        // Create default type if none exists
        var defaultType = new ProductType { Name = "Generico" };
        _db.ProductTypes.Add(defaultType);
        await _db.SaveChangesAsync(ct);
        
        return defaultType.Id;
    }

    private async Task CreateProductAliasesAsync(Product product, string originalLabel, string? brand, CancellationToken ct)
    {
        var aliases = new List<string>();

        // Add original label as alias if different from normalized name
        if (!product.Name.Equals(originalLabel, StringComparison.OrdinalIgnoreCase))
        {
            aliases.Add(originalLabel.Trim());
        }

        // Add lowercased version
        var lowerName = product.Name.ToLowerInvariant();
        if (!aliases.Any(a => a.Equals(lowerName, StringComparison.OrdinalIgnoreCase)))
        {
            aliases.Add(lowerName);
        }

        // Add brand-prefixed versions if brand exists
        if (!string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(product.Brand))
        {
            var brandPrefixed = $"{product.Brand} {product.Name}";
            if (!aliases.Any(a => a.Equals(brandPrefixed, StringComparison.OrdinalIgnoreCase)))
            {
                aliases.Add(brandPrefixed);
            }
        }

        // Create alias entities
        foreach (var alias in aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            _db.ProductAliases.Add(new ProductAlias
            {
                ProductId = product.Id,
                Alias = alias.Trim()
            });
        }

        if (aliases.Any())
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    #endregion
}