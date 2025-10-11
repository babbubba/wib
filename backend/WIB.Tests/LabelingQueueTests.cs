using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.API.Controllers;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;
using Xunit;

namespace WIB.Tests;

public class LabelingQueueTests
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
    }

    [Fact]
    public async Task Pending_Filters_By_Confidence()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);

        var r = new Receipt
        {
            Date = DateTimeOffset.Now,
            Currency = "EUR",
            Total = 1,
            Lines = new List<ReceiptLine>
            {
                new ReceiptLine { LabelRaw = "A", LineTotal = 1, UnitPrice = 1, Qty = 1, PredictionConfidence = 0.5m },
                new ReceiptLine { LabelRaw = "B", LineTotal = 1, UnitPrice = 1, Qty = 1, PredictionConfidence = 0.9m },
                new ReceiptLine { LabelRaw = "C", LineTotal = 1, UnitPrice = 1, Qty = 1, PredictionConfidence = null }
            }
        };
        db.Receipts.Add(r);
        await db.SaveChangesAsync();

        Assert.Equal(3, db.ReceiptLines.Count());
        Assert.Equal(2, db.ReceiptLines.Count(l => l.PredictionConfidence == null || l.PredictionConfidence < 0.6m));

        var ctl = new ReceiptController(new StubImages(), new StubQueue(), db);
        var res = await ctl.Pending(0.6m, 100, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value!);
        Assert.Equal(2, list.Count());
    }
}
