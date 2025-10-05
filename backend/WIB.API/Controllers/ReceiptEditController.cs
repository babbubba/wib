using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WIB.Application.Contracts.Receipts;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("receipts")]
public class ReceiptEditController : ControllerBase
{
    private readonly WibDbContext _db;

    public ReceiptEditController(WibDbContext db)
    {
        _db = db;
    }

    [HttpPost("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditReceiptRequest body, CancellationToken ct)
    {
        var receipt = await _db.Receipts
            .Include(r => r.Store)
            .Include(r => r.StoreLocation)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(body.StoreName))
        {
            var name = body.StoreName.Trim();
            var lower = name.ToLowerInvariant();
            var store = await _db.Stores.FirstOrDefaultAsync(s => s.Name.ToLower() == lower, ct);
            if (store == null)
            {
                var norm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
                store = new WIB.Domain.Store { Name = norm };
                _db.Stores.Add(store);
                await _db.SaveChangesAsync(ct);
            }
            receipt.StoreId = store.Id;
            receipt.Store = store;
        }

        if (body.Datetime.HasValue) receipt.Date = body.Datetime.Value;
        if (!string.IsNullOrWhiteSpace(body.Currency)) receipt.Currency = body.Currency!;
        // Optional store fields -> upsert a StoreLocation linked to this store
        if (!string.IsNullOrWhiteSpace(body.StoreAddress) || !string.IsNullOrWhiteSpace(body.StoreCity) || !string.IsNullOrWhiteSpace(body.StorePostalCode) || !string.IsNullOrWhiteSpace(body.StoreVatNumber))
        {
            var addr = body.StoreAddress?.Trim();
            var city = body.StoreCity?.Trim();
            var cap = body.StorePostalCode?.Trim();
            var vat = body.StoreVatNumber?.Trim();
            var loc = await _db.StoreLocations.FirstOrDefaultAsync(sl => sl.StoreId == receipt.StoreId && sl.Address == addr && sl.City == city && sl.PostalCode == cap, ct);
            if (loc == null)
            {
                loc = new WIB.Domain.StoreLocation { StoreId = receipt.StoreId, Address = addr, City = city, PostalCode = cap, VatNumber = vat };
                _db.StoreLocations.Add(loc);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(vat)) loc.VatNumber = vat;
            }
            receipt.StoreLocationId = loc.Id;
            receipt.StoreLocation = loc;
        }

        if (body.Lines != null && body.Lines.Count > 0)
        {
            var arr = receipt.Lines.OrderBy(l => l.Id).ToList();
            // Apply removals first (descending by index) to avoid shifting
            var removals = body.Lines.Where(p => p.Remove == true).OrderByDescending(p => p.Index).ToList();
            foreach (var rm in removals)
            {
                if (rm.Index >= 0 && rm.Index < arr.Count)
                {
                    var line = arr[rm.Index];
                    receipt.Lines.Remove(line);
                    arr.RemoveAt(rm.Index);
                }
            }

            foreach (var patch in body.Lines.Where(p => p.Remove != true))
            {
                if (patch.Index < 0 || patch.Index >= arr.Count) continue;
                var line = arr[patch.Index];
                if (!string.IsNullOrWhiteSpace(patch.LabelRaw)) line.LabelRaw = patch.LabelRaw!.Trim();
                if (patch.Qty.HasValue) line.Qty = patch.Qty.Value;
                if (patch.UnitPrice.HasValue) line.UnitPrice = patch.UnitPrice.Value;
                if (patch.LineTotal.HasValue) line.LineTotal = patch.LineTotal.Value;
                if (patch.VatRate.HasValue) line.VatRate = patch.VatRate.Value;

                if (patch.FinalCategoryId.HasValue || !string.IsNullOrWhiteSpace(patch.FinalCategoryName))
                {
                    Guid? catId = patch.FinalCategoryId;
                    if (!catId.HasValue && !string.IsNullOrWhiteSpace(patch.FinalCategoryName))
                    {
                        var cname = patch.FinalCategoryName!.Trim();
                        var clower = cname.ToLowerInvariant();
                        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == clower, ct);
                        if (cat == null)
                        {
                            var cnorm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clower);
                            cat = new WIB.Domain.Category { Name = cnorm };
                            _db.Categories.Add(cat);
                            await _db.SaveChangesAsync(ct);
                        }
                        catId = cat.Id;
                    }
                    if (catId.HasValue)
                    {
                        // Avoid creating Product without ProductType; set category as confirmed on line
                        line.PredictedCategoryId = catId;
                        line.PredictionConfidence = 1.0m;
                    }
                }
            }
        }

        // Add new lines if provided
        if (body.AddLines != null && body.AddLines.Count > 0)
        {
            foreach (var add in body.AddLines)
            {
                Guid? catId = add.FinalCategoryId;
                if (!catId.HasValue && !string.IsNullOrWhiteSpace(add.FinalCategoryName))
                {
                    var cname = add.FinalCategoryName!.Trim();
                    var clower = cname.ToLowerInvariant();
                    var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == clower, ct);
                    if (cat == null)
                    {
                        var cnorm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clower);
                        cat = new WIB.Domain.Category { Name = cnorm };
                        _db.Categories.Add(cat);
                        await _db.SaveChangesAsync(ct);
                    }
                    catId = cat.Id;
                }
                var newLine = new WIB.Domain.ReceiptLine
                {
                    LabelRaw = add.LabelRaw.Trim(),
                    Qty = add.Qty,
                    UnitPrice = add.UnitPrice,
                    LineTotal = add.LineTotal,
                    VatRate = add.VatRate,
                    ProductId = null,
                    PredictedCategoryId = catId,
                    PredictionConfidence = catId.HasValue ? 1.0m : null
                };
                receipt.Lines.Add(newLine);
            }
        }

        // Recompute receipt total if lines changed
        receipt.Total = receipt.Lines.Sum(x => x.LineTotal);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
