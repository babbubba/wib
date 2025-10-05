using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.Application.Contracts.Analytics;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly WibDbContext _db;

    public AnalyticsController(WibDbContext db)
    {
        _db = db;
    }

    [HttpGet("spending")]
    public async Task<IActionResult> Spending([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? storeId, [FromQuery] Guid? categoryId, CancellationToken ct)
    {
        var lines = _db.ReceiptLines
            .Include(l => l.Receipt)
            .Include(l => l.Product)
            .AsNoTracking()
            .Where(l => l.Receipt.Date >= from && l.Receipt.Date <= to);

        if (storeId.HasValue)
            lines = lines.Where(l => l.Receipt.StoreId == storeId.Value);

        if (categoryId.HasValue)
            lines = lines.Where(l => l.Product != null && l.Product.CategoryId == categoryId.Value);

        var result = await lines
            .GroupBy(l => new { l.Receipt.Date.Year, l.Receipt.Date.Month })
            .Select(g => new SpendingAggregateDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                StoreId = storeId,
                CategoryId = categoryId,
                Amount = g.Sum(x => x.LineTotal)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(ct);

        return Ok(result);
    }

    [HttpGet("price-history")]
    public async Task<IActionResult> PriceHistory([FromQuery] Guid productId, [FromQuery] Guid? storeId, CancellationToken ct)
    {
        var q = _db.PriceHistories.AsNoTracking().Where(p => p.ProductId == productId);
        if (storeId.HasValue)
            q = q.Where(p => p.StoreId == storeId.Value);

        var points = await q.OrderBy(p => p.Date)
            .Select(p => new PriceHistoryPointDto { Date = p.Date, UnitPrice = p.UnitPrice })
            .ToListAsync(ct);
        return Ok(points);
    }
}
