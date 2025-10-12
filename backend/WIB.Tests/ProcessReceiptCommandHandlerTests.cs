using Microsoft.EntityFrameworkCore;
using WIB.Application.Contracts.Ml;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Domain;
using WIB.Infrastructure.Clients;
using WIB.Infrastructure.Data;
using Xunit;

namespace WIB.Tests;

public class ProcessReceiptCommandHandlerTests
{
    private sealed class StubOcr : IOcrClient
    {
        public Task<string> ExtractAsync(Stream image, CancellationToken ct) => Task.FromResult("ocr-text");
    }
    private sealed class StubKie : IKieClient
    {
        public Task<WIB.Application.Contracts.Kie.ReceiptDraft> ExtractFieldsAsync(string ocrResult, CancellationToken ct)
            => ExtractFieldsAsync(ocrResult, null, ct);

        public Task<WIB.Application.Contracts.Kie.ReceiptDraft> ExtractFieldsAsync(string ocrResult, byte[]? imageBytes, CancellationToken ct)
            => Task.FromResult(new WIB.Application.Contracts.Kie.ReceiptDraft
            {
                Store = new WIB.Application.Contracts.Kie.ReceiptDraftStore { Name = "Test" },
                Datetime = DateTimeOffset.Parse("2025-01-01T10:00:00Z"),
                Currency = "EUR",
                Lines =
                [
                    new WIB.Application.Contracts.Kie.ReceiptDraftLine { LabelRaw = "LATTE 1L", Qty = 1, UnitPrice = 1.23m, LineTotal = 1.23m }
                ],
                Totals = new WIB.Application.Contracts.Kie.ReceiptDraftTotals { Subtotal = 1.23m, Tax = 0m, Total = 1.23m }
            });
    }
    private sealed class StubClf : IProductClassifier
    {
        public Task<MlPredictionResult> PredictAsync(string labelRaw, CancellationToken ct) => 
            Task.FromResult(new MlPredictionResult { TypeId = null, CategoryId = null, Confidence = 0f });
        public Task FeedbackAsync(string labelRaw, string? brand, Guid typeId, Guid? categoryId, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class StubImg : IImageStorage
    {
        public Task<string> SaveAsync(Stream image, string? contentType, CancellationToken ct) => Task.FromResult("2025/01/01/test.jpg");
        public Task<Stream> GetAsync(string objectKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 }));
        public Task DeleteAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class StubNames : INameMatcher
    {
        public Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct) => Task.FromResult<(Guid, string)?>(null);
        public Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct) => Task.FromResult<string?>(null);
    }
    
    private sealed class StubProductMatcher : IProductMatcher
    {
        public Task<ProductMatch?> FindOrCreateProductAsync(string labelRaw, string? brand, Guid? predictedTypeId, Guid? predictedCategoryId, float confidence, float confidenceThreshold = 0.8f, CancellationToken ct = default)
            => Task.FromResult<ProductMatch?>(null); // No product matching in tests
            
        public Task<Product> CreateProductAsync(string name, string? brand, Guid? productTypeId, Guid? categoryId, string? gtin = null, CancellationToken ct = default)
            => Task.FromResult(new Product { Id = Guid.NewGuid(), Name = name, Brand = brand, ProductTypeId = productTypeId ?? Guid.NewGuid(), CategoryId = categoryId });
            
        public Task<List<Product>> FindSimilarProductsAsync(string labelRaw, Guid? typeId, Guid? categoryId, int maxResults = 5, CancellationToken ct = default)
            => Task.FromResult(new List<Product>());
    }

    [Fact]
    public async Task Handle_Saves_Image_And_Receipt()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);

        var storage = new ReceiptStorage(db);
        var handler = new ProcessReceiptCommandHandler(new StubOcr(), new StubKie(), new StubClf(), storage, new StubImg(), new StubNames(), new StubProductMatcher());

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        await handler.Handle(new ProcessReceiptCommand(ms), CancellationToken.None);

        var saved = db.Receipts.Single();
        Assert.Equal("2025/01/01/test.jpg", saved.ImageObjectKey);
        Assert.Equal("EUR", saved.Currency);
        Assert.Equal(1.23m, saved.Total);
        Assert.Single(saved.Lines);
        Assert.Equal("LATTE 1L", saved.Lines.First().LabelRaw);
    }
}
