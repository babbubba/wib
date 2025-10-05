using Microsoft.EntityFrameworkCore;
using WIB.Domain;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Clients;
using Xunit;

namespace WIB.Tests;

public class ReceiptStorageTests
{
    [Fact]
    public async Task SaveAsync_Persists_Receipt()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);
        var storage = new ReceiptStorage(db);

        var receipt = new Receipt
        {
            Date = DateTimeOffset.UtcNow,
            Currency = "EUR",
            Total = 1.23m,
            RawText = "raw",
            Lines = new List<ReceiptLine> {
                new ReceiptLine { LabelRaw = "milk", Qty = 1, UnitPrice = 1.23m, LineTotal = 1.23m }
            }
        };

        var saved = await storage.SaveAsync(receipt, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(1, db.Receipts.Count());
        Assert.Equal(1, db.ReceiptLines.Count());
    }
}

