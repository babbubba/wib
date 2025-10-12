using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.API.Controllers;
using WIB.Domain;
using WIB.Infrastructure.Data;
using WIB.Application.Interfaces;
using Xunit;

namespace WIB.Tests;

public class ReceiptControllerTests
{
    private sealed class StubQueue : IReceiptQueue
    {
        public Task EnqueueAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> TryDequeueAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<long> GetLengthAsync(CancellationToken ct) => Task.FromResult(0L);
        public Task<IReadOnlyList<string>> PeekAsync(int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }
    private sealed class StubImages : IImageStorage
    {
        public Task<string> SaveAsync(Stream image, string? contentType, CancellationToken ct) => Task.FromResult("obj");
        public Task<Stream> GetAsync(string objectKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Get_Returns_ReceiptDto()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);

        var store = new Store { Name = "Market" };
        var receipt = new Receipt
        {
            Store = store,
            Date = DateTimeOffset.Parse("2025-01-01T10:00:00Z"),
            Currency = "EUR",
            TaxTotal = 0,
            Total = 2.23m,
            Lines = new List<ReceiptLine>
            {
                new ReceiptLine { LabelRaw = "LATTE 1L", Qty = 1, UnitPrice = 1.23m, LineTotal = 1.23m },
                new ReceiptLine { LabelRaw = "PANE", Qty = 2, UnitPrice = 0.50m, LineTotal = 1.00m }
            }
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync();

        var controller = new ReceiptController(new StubImages(), new StubQueue(), db);
        var result = await controller.Get(receipt.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        dynamic dto = ok.Value!;
        Assert.Equal("EUR", (string)dto.Currency);
    }
}
