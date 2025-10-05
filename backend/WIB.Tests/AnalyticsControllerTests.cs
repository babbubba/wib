using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.API.Controllers;
using WIB.Domain;
using WIB.Infrastructure.Data;
using Xunit;

namespace WIB.Tests;

public class AnalyticsControllerTests
{
    [Fact]
    public async Task Spending_Aggregates_By_Month()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);

        var store = new Store { Name = "Market" };
        var r1 = new Receipt
        {
            Store = store,
            Date = new DateTimeOffset(new DateTime(2025, 1, 10)),
            Currency = "EUR",
            Total = 3m,
            Lines = new List<ReceiptLine>
            {
                new ReceiptLine { LabelRaw = "A", Qty = 1, UnitPrice = 1m, LineTotal = 1m },
                new ReceiptLine { LabelRaw = "B", Qty = 2, UnitPrice = 1m, LineTotal = 2m }
            }
        };
        var r2 = new Receipt
        {
            Store = store,
            Date = new DateTimeOffset(new DateTime(2025, 2, 5)),
            Currency = "EUR",
            Total = 2m,
            Lines = new List<ReceiptLine>
            {
                new ReceiptLine { LabelRaw = "C", Qty = 1, UnitPrice = 2m, LineTotal = 2m }
            }
        };
        db.Receipts.AddRange(r1, r2);
        await db.SaveChangesAsync();

        var controller = new AnalyticsController(db);
        var res = await controller.Spending(new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), null, null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res);
        var list = Assert.IsAssignableFrom<IEnumerable<dynamic>>(ok.Value!);
        var items = list.Cast<dynamic>().ToList();
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task PriceHistory_Returns_Sorted_Points()
    {
        var options = new DbContextOptionsBuilder<WibDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new WibDbContext(options);

        var pid = Guid.NewGuid();
        var sid = Guid.NewGuid();
        db.PriceHistories.AddRange(
            new PriceHistory { ProductId = pid, StoreId = sid, Date = new DateTime(2025, 2, 1), UnitPrice = 2.0m },
            new PriceHistory { ProductId = pid, StoreId = sid, Date = new DateTime(2025, 1, 1), UnitPrice = 1.5m }
        );
        await db.SaveChangesAsync();

        var controller = new AnalyticsController(db);
        var res = await controller.PriceHistory(pid, sid, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res);
        var list = Assert.IsAssignableFrom<IEnumerable<dynamic>>(ok.Value!);
        var arr = list.Cast<dynamic>().ToArray();
        Assert.True(((DateTime)arr[0].Date) < ((DateTime)arr[1].Date));
    }
}

