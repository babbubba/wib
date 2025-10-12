using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using WIB.Application.Contracts.Receipts;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("receipts")]
public class ReceiptEditController : ControllerBase
{
    private readonly WibDbContext _db;
    private readonly IProductClassifier _classifier;

    public ReceiptEditController(WibDbContext db, IProductClassifier classifier)
    {
        _db = db;
        _classifier = classifier;
    }

    [HttpPost("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditReceiptRequest body, CancellationToken ct)
    {
        var receipt = await _db.Receipts
            .Include(r => r.Store)
            .Include(r => r.StoreLocation)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt == null)
            return NotFound();

        var feedbackTargets = new List<(string Label, Guid TypeId, Guid CategoryId)>();

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

        if (body.Datetime.HasValue)
            receipt.Date = body.Datetime.Value;

        if (!string.IsNullOrWhiteSpace(body.Currency))
            receipt.Currency = body.Currency!;

        if (!string.IsNullOrWhiteSpace(body.StoreAddress)
            || !string.IsNullOrWhiteSpace(body.StoreCity)
            || !string.IsNullOrWhiteSpace(body.StorePostalCode)
            || !string.IsNullOrWhiteSpace(body.StoreVatNumber))
        {
            var addr = body.StoreAddress?.Trim();
            var city = body.StoreCity?.Trim();
            var cap = body.StorePostalCode?.Trim();
            var vat = body.StoreVatNumber?.Trim();

            var existingLocation = await _db.StoreLocations.FirstOrDefaultAsync(
                sl => sl.StoreId == receipt.StoreId
                    && sl.Address == addr
                    && sl.City == city
                    && sl.PostalCode == cap,
                ct);

            if (existingLocation == null)
            {
                existingLocation = new WIB.Domain.StoreLocation
                {
                    StoreId = receipt.StoreId,
                    Address = addr,
                    City = city,
                    PostalCode = cap,
                    VatNumber = vat
                };
                _db.StoreLocations.Add(existingLocation);
                await _db.SaveChangesAsync(ct);
            }
            else if (!string.IsNullOrWhiteSpace(vat))
            {
                existingLocation.VatNumber = vat;
            }

            receipt.StoreLocationId = existingLocation.Id;
            receipt.StoreLocation = existingLocation;
        }

        if (body.Lines != null && body.Lines.Count > 0)
        {
            var arr = receipt.Lines.OrderBy(l => l.SortIndex).ThenBy(l => l.Id).ToList();
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
                if (patch.Index < 0 || patch.Index >= arr.Count)
                    continue;

                var line = arr[patch.Index];
                var originalLabel = line.LabelRaw;
                var lineTouched = false;

                if (!string.IsNullOrWhiteSpace(patch.LabelRaw))
                {
                    var updated = patch.LabelRaw.Trim();
                    if (!string.Equals(updated, originalLabel, StringComparison.Ordinal))
                    {
                        line.LabelRaw = updated;
                        lineTouched = true;
                    }
                }

                if (patch.Qty.HasValue)
                {
                    line.Qty = patch.Qty.Value;
                    lineTouched = true;
                }

                if (patch.UnitPrice.HasValue)
                {
                    line.UnitPrice = patch.UnitPrice.Value;
                    lineTouched = true;
                }

                if (patch.LineTotal.HasValue)
                {
                    line.LineTotal = patch.LineTotal.Value;
                    lineTouched = true;
                }

                if (patch.VatRate.HasValue)
                    line.VatRate = patch.VatRate.Value;

                if (patch.FinalCategoryId.HasValue || !string.IsNullOrWhiteSpace(patch.FinalCategoryName))
                {
                    Guid? catId = patch.FinalCategoryId;
                    if (!catId.HasValue && !string.IsNullOrWhiteSpace(patch.FinalCategoryName))
                    {
                        var cname = patch.FinalCategoryName.Trim();
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
                        line.PredictedCategoryId = catId;
                        line.PredictionConfidence = 1.0m;
                        lineTouched = true;
                    }
                }

                if (lineTouched && line.PredictedCategoryId.HasValue)
                {
                    var typeId = line.PredictedTypeId ?? line.Product?.ProductTypeId;
                    if (typeId.HasValue)
                    {
                        feedbackTargets.Add((line.LabelRaw, typeId.Value, line.PredictedCategoryId.Value));
                    }
                }
            }
        }

        if (body.AddLines != null && body.AddLines.Count > 0)
        {
            foreach (var add in body.AddLines)
            {
                if (string.IsNullOrWhiteSpace(add.LabelRaw))
                {
                    return BadRequest("addLines[].labelRaw mancante");
                }
                // Normalizza numerici mancanti: qty/unitPrice/lineTotal
                // Evita null in value types e garantisce un payload completo lato server
                var qty = add.Qty;
                var unitPrice = add.UnitPrice;
                var lineTotal = add.LineTotal;
                if (qty <= 0 && unitPrice >= 0 && lineTotal == 0)
                {
                    qty = 1;
                }
                if (lineTotal == 0 && unitPrice >= 0 && qty >= 0)
                {
                    lineTotal = unitPrice * qty;
                }
                Guid? catId = add.FinalCategoryId;
                if (!catId.HasValue && !string.IsNullOrWhiteSpace(add.FinalCategoryName))
                {
                    var cname = add.FinalCategoryName.Trim();
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

                var nextIndex = (receipt.Lines.Any() ? receipt.Lines.Max(x => (int?)x.SortIndex) ?? -1 : -1) + 1;
                var newLine = new WIB.Domain.ReceiptLine
                {
                    LabelRaw = add.LabelRaw.Trim(),
                    Qty = qty,
                    UnitPrice = unitPrice,
                    LineTotal = lineTotal,
                    VatRate = add.VatRate,
                    SortIndex = nextIndex,
                    ProductId = null,
                    PredictedCategoryId = catId,
                    PredictionConfidence = catId.HasValue ? 1.0m : null
                };

                receipt.Lines.Add(newLine);

                if (catId.HasValue)
                {
                    var typeId = newLine.PredictedTypeId ?? newLine.Product?.ProductTypeId;
                    if (typeId.HasValue)
                    {
                        feedbackTargets.Add((newLine.LabelRaw, typeId.Value, catId.Value));
                    }
                }
            }
        }

        if (body.Order != null && body.Order.Count == receipt.Lines.Count)
        {
            var current = receipt.Lines.OrderBy(l => l.SortIndex).ThenBy(l => l.Id).ToList();
            // Applica solo se l'array è una permutazione valida degli indici correnti
            var valid = body.Order.All(i => i >= 0 && i < current.Count) && body.Order.Distinct().Count() == current.Count;
            if (valid)
            {
                for (int newPos = 0; newPos < body.Order.Count; newPos++)
                {
                    var origIdx = body.Order[newPos];
                    current[origIdx].SortIndex = newPos;
                }
            }
        }

        receipt.Total = receipt.Lines.Sum(x => x.LineTotal);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("La ricevuta è stata modificata nel frattempo. Riprova dopo aver ricaricato.");
        }

        foreach (var target in feedbackTargets)
        {
            await _classifier.FeedbackAsync(target.Label, null, target.TypeId, target.CategoryId, ct);
        }

        return NoContent();
    }
}
