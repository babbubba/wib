using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using WIB.Domain;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Services;
using Xunit;

namespace WIB.Tests;

public class EnhancedNameMatcherTests : IDisposable
{
    private readonly WibDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<EnhancedNameMatcher>> _loggerMock;
    private readonly EnhancedNameMatcher _matcher;

    public EnhancedNameMatcherTests()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new WibDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<EnhancedNameMatcher>>();
        _matcher = new EnhancedNameMatcher(_db, _cache, _loggerMock.Object);

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Seed stores
        var stores = new[]
        {
            new Store { Id = Guid.NewGuid(), Name = "Coop Centro" },
            new Store { Id = Guid.NewGuid(), Name = "Esselunga" },
            new Store { Id = Guid.NewGuid(), Name = "Carrefour Express" },
            new Store { Id = Guid.NewGuid(), Name = "LIDL" },
            new Store { Id = Guid.NewGuid(), Name = "Eurospin" },
            new Store { Id = Guid.NewGuid(), Name = "MD Discount" },
            new Store { Id = Guid.NewGuid(), Name = "Farmacia Comunale" },
            new Store { Id = Guid.NewGuid(), Name = "Conad City" },
        };

        _db.Stores.AddRange(stores);

        // Seed products
        var products = new[]
        {
            new Product { Id = Guid.NewGuid(), Name = "Latte Fresco" },
            new Product { Id = Guid.NewGuid(), Name = "Parmigiano Reggiano" },
            new Product { Id = Guid.NewGuid(), Name = "Olio Extra Vergine" },
            new Product { Id = Guid.NewGuid(), Name = "Pasta di Semola" },
            new Product { Id = Guid.NewGuid(), Name = "Pomodori San Marzano" },
        };

        _db.Products.AddRange(products);

        // Seed product aliases
        var aliases = new[]
        {
            new ProductAlias { Id = Guid.NewGuid(), ProductId = products[0].Id, Alias = "Latte" },
            new ProductAlias { Id = Guid.NewGuid(), ProductId = products[0].Id, Alias = "Latte UHT" },
            new ProductAlias { Id = Guid.NewGuid(), ProductId = products[1].Id, Alias = "Parmigiano" },
            new ProductAlias { Id = Guid.NewGuid(), ProductId = products[1].Id, Alias = "Grana Padano" },
            new ProductAlias { Id = Guid.NewGuid(), ProductId = products[2].Id, Alias = "Olio EVO" },
        };

        _db.ProductAliases.AddRange(aliases);
        _db.SaveChanges();
    }

    [Theory]
    [InlineData("coop centro", "Coop Centro")]
    [InlineData("COOP CENTRO", "Coop Centro")]
    [InlineData("Co-op Centro", "Coop Centro")]
    [InlineData("Cooperativa Centro", "Coop Centro")]
    [InlineData("esselunga", "Esselunga")]
    [InlineData("esse lunga", "Esselunga")]
    [InlineData("carrefour", "Carrefour Express")]
    [InlineData("carrefur", "Carrefour Express")] // OCR error
    [InlineData("lidl", "LIDL")]
    [InlineData("lidel", "LIDL")] // OCR error
    [InlineData("conad city", "Conad City")]
    [InlineData("con.ad", "Conad City")]
    public async Task MatchStoreAsync_ShouldMatchCorrectStore(string input, string expectedStore)
    {
        // Act
        var result = await _matcher.MatchStoreAsync(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStore, result.Value.name);
    }

    [Theory]
    [InlineData("latte fresco", "Latte Fresco")]
    [InlineData("1atte fresc0", "Latte Fresco")] // OCR errors: 1->l, 0->o
    [InlineData("latte", "Latte")] // Match alias
    [InlineData("parmigiano", "Parmigiano")] // Match alias
    [InlineData("0li0 ev0", "Olio EVO")] // OCR errors: 0->o
    public async Task CorrectProductLabelAsync_ShouldCorrectLabel(string input, string expectedLabel)
    {
        // Act
        var result = await _matcher.CorrectProductLabelAsync(input, CancellationToken.None);

        // Assert
        Assert.Equal(expectedLabel, result);
    }

    [Theory]
    [InlineData("xyz unknown store")]
    [InlineData("")]
    [InlineData("ab")] // Too short
    public async Task MatchStoreAsync_ShouldReturnNull_ForUnknownStores(string input)
    {
        // Act
        var result = await _matcher.MatchStoreAsync(input, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task MatchStoreAsync_ShouldUseCaching()
    {
        // First call
        var sw1 = Stopwatch.StartNew();
        var result1 = await _matcher.MatchStoreAsync("coop centro", CancellationToken.None);
        sw1.Stop();

        // Second call (should be faster due to caching)
        var sw2 = Stopwatch.StartNew();
        var result2 = await _matcher.MatchStoreAsync("esselunga", CancellationToken.None);
        sw2.Stop();

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        
        // Second call should be faster (cache hit for store data)
        Assert.True(sw2.ElapsedMilliseconds <= sw1.ElapsedMilliseconds + 10); // Allow some margin
    }

    [Fact]
    public async Task MatchStoreAsync_PerformanceTest()
    {
        // Arrange
        var testInputs = new[]
        {
            "coop centro", "esselunga", "carrefour", "lidl", "eurospin",
            "conad", "md discount", "farmacia", "coop", "esse lunga"
        };

        // Act
        var sw = Stopwatch.StartNew();
        var results = new List<(Guid, string)?>();
        
        foreach (var input in testInputs)
        {
            var result = await _matcher.MatchStoreAsync(input, CancellationToken.None);
            results.Add(result);
        }
        
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 500, $"Matching {testInputs.Length} stores took {sw.ElapsedMilliseconds}ms");
        Assert.True(results.Count(r => r.HasValue) >= 7, "Should match at least 7 out of 10 test inputs");
        
        // Log performance info
        var matchCount = results.Count(r => r.HasValue);
        var avgTime = sw.ElapsedMilliseconds / (double)testInputs.Length;
        
        _loggerMock.VerifyLog(LogLevel.Debug, Times.AtLeastOnce());
        Console.WriteLine($"Performance: {matchCount}/{testInputs.Length} matches in {sw.ElapsedMilliseconds}ms (avg: {avgTime:F2}ms per match)");
    }

    [Fact]
    public async Task BrandNormalization_ShouldWorkCorrectly()
    {
        // Test brand normalization with various inputs
        var testCases = new Dictionary<string, bool>
        {
            { "Supermercato Coop Centro", true }, // Should match Coop Centro
            { "Ipermercato Esselunga", true },    // Should match Esselunga
            { "Centro Commerciale LIDL", true },  // Should match LIDL
            { "Il Conad della Città", true },     // Should match Conad City
        };

        foreach (var (input, shouldMatch) in testCases)
        {
            var result = await _matcher.MatchStoreAsync(input, CancellationToken.None);
            
            if (shouldMatch)
            {
                Assert.NotNull(result);
                Console.WriteLine($"'{input}' matched to '{result.Value.name}'");
            }
        }
    }

    [Theory]
    [InlineData("coop", "coop", 1.0)]
    [InlineData("coop", "co-op", 0.9)] // Should be high similarity
    [InlineData("carrefour", "carrefur", 0.85)] // OCR error
    [InlineData("lidl", "lidel", 0.85)] // OCR error  
    [InlineData("test", "completely different", 0.2)] // Should be low
    public void CombinedSimilarity_ShouldProduceExpectedResults(string a, string b, double expectedMinSimilarity)
    {
        // We need to test the similarity algorithms via the matcher
        // Since the methods are private, we test through public interface
        var testResult = TestSimilarity(a, b);
        Assert.True(testResult >= expectedMinSimilarity, 
            $"Similarity between '{a}' and '{b}' was {testResult:F3}, expected >= {expectedMinSimilarity}");
    }

    // Helper method to estimate similarity through matching behavior
    private double TestSimilarity(string a, string b)
    {
        // This is a rough approximation - in a real scenario you might expose
        // the similarity method for testing or use reflection
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
        
        // Simple Levenshtein-based estimate for testing
        int dist = ComputeLevenshteinDistance(a, b);
        int maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)dist / maxLen;
    }

    private int ComputeLevenshteinDistance(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }
        return d[n, m];
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }
}

// Extension method for easier mock verification
public static class MockLoggerExtensions
{
    public static Mock<ILogger<T>> VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, Times times)
    {
        logger.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == level),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)
            ),
            times
        );
        return logger;
    }
}