using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Clients;

public class ReceiptStorage : IReceiptStorage
{
    private readonly WibDbContext _db;

    public ReceiptStorage(WibDbContext db)
    {
        _db = db;
    }

    public async Task<Receipt> SaveAsync(Receipt receipt, CancellationToken ct)
    {
        _db.Receipts.Add(receipt);
        // First save: ensures Store/Receipt/Lines get IDs
        await _db.SaveChangesAsync(ct);

        // Create/update price history points for lines linked to a Product
        // Use the receipt date (day precision) and the store from the receipt
        if (receipt.StoreId != Guid.Empty && receipt.Lines?.Count > 0)
        {
            var storeId = receipt.StoreId;
            var day = receipt.Date.UtcDateTime.Date;

            // Aggregate by ProductId within this receipt and take the minimum unit price for the day
            var byProduct = receipt.Lines
                .Where(l => l.ProductId.HasValue)
                .GroupBy(l => l.ProductId!.Value)
                .Select(g => new {
                    ProductId = g.Key,
                    UnitPrice = g.Min(x => x.UnitPrice),
                    PricePerKg = g.Where(x => x.PricePerKg.HasValue).Select(x => x.PricePerKg!.Value).DefaultIfEmpty().Min()
                })
                .ToList();

            foreach (var item in byProduct)
            {
                var exists = await _db.PriceHistories
                    .AsNoTracking()
                    .AnyAsync(p => p.ProductId == item.ProductId && p.StoreId == storeId && p.Date == day, ct);
                if (!exists)
                {
                    _db.PriceHistories.Add(new PriceHistory
                    {
                        ProductId = item.ProductId,
                        StoreId = storeId,
                        Date = day,
                        UnitPrice = item.UnitPrice,
                        PricePerKg = item.PricePerKg == 0 ? null : item.PricePerKg
                    });
                }
            }
            await _db.SaveChangesAsync(ct);
        }

        return receipt;
    }
}
