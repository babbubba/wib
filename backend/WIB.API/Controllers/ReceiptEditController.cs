using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WIB.Application.Contracts.Receipts;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;
using WIB.Domain;

namespace WIB.API.Controllers
{
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
            // Input validation
            var validationResult = ValidateRequest(body);
            if (validationResult != null)
                return validationResult;

            // Use a single transaction to avoid concurrency issues
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                // Load receipt with related data
                var receipt = await LoadReceiptAsync(id, ct);
                if (receipt == null)
                    return NotFound();

                var feedbackTargets = new List<(string Label, Guid TypeId, Guid CategoryId)>();

                // Update receipt basic properties
                await UpdateReceiptBasicPropertiesAsync(receipt, body, ct);

                // Update store location if needed
                await UpdateStoreLocationAsync(receipt, body, ct);

                // Process line modifications
                await ProcessLineModificationsAsync(receipt, body, feedbackTargets, ct);

                // Track existing lines count before adding new ones (for reordering)
                var existingLinesCountBeforeAdd = receipt.Lines.Count;

                // Add new lines
                await AddNewLinesAsync(receipt, body, feedbackTargets, ct);

                // Apply line reordering (only to existing lines, not newly added)
                ApplyLineReordering(receipt, body, existingLinesCountBeforeAdd);

                // Finalize receipt (single SaveChanges at the end)
                await FinalizeReceiptAsync(receipt, feedbackTargets, ct);

                await tx.CommitAsync(ct);
                return NoContent();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        #region Private Methods

        private IActionResult? ValidateRequest(EditReceiptRequest body)
        {
            // Validate new lines
            if (body.AddLines?.Any(line => string.IsNullOrWhiteSpace(line.LabelRaw)) == true)
                return BadRequest("addLines[].labelRaw è obbligatorio");

            // Validate line modifications have valid indices
            if (body.Lines?.Any(line => line.Index < 0) == true)
                return BadRequest("lines[].index deve essere >= 0");

            // Validate reordering array if present
            if (body.Order != null && (body.Order.Any(i => i < 0) || body.Order.Distinct().Count() != body.Order.Count))
                return BadRequest("order[] deve contenere indici validi e unici");

            // Validate currency format if provided
            if (!string.IsNullOrWhiteSpace(body.Currency) && body.Currency.Length > 10)
                return BadRequest("currency troppo lunga (max 10 caratteri)");

            // Validate numeric fields in new lines
            if (body.AddLines?.Any(line => line.Qty < 0 || line.UnitPrice < 0 || line.LineTotal < 0) == true)
                return BadRequest("addLines[] non può contenere valori negativi per qty, unitPrice, lineTotal");

            // Validate numeric fields in line updates
            if (body.Lines?.Any(line => line.Qty < 0 || line.UnitPrice < 0 || line.LineTotal < 0) == true)
                return BadRequest("lines[] non può contenere valori negativi per qty, unitPrice, lineTotal");

            return null;
        }

        private async Task<Receipt?> LoadReceiptAsync(Guid id, CancellationToken ct)
        {
            return await _db.Receipts
                .Include(r => r.Store)
                .Include(r => r.StoreLocation)
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
        }

        private async Task UpdateReceiptBasicPropertiesAsync(Receipt receipt, EditReceiptRequest body, CancellationToken ct)
        {
            // Update store
            if (!string.IsNullOrWhiteSpace(body.StoreName))
            {
                await UpdateReceiptStoreAsync(receipt, body.StoreName.Trim(), ct);
            }

            // Update datetime and currency
            if (body.Datetime.HasValue) 
                receipt.Date = body.Datetime.Value;

            if (!string.IsNullOrWhiteSpace(body.Currency)) 
                receipt.Currency = body.Currency;
        }

        private async Task UpdateReceiptStoreAsync(Receipt receipt, string storeName, CancellationToken ct)
        {
            // Delegate rename/merge logic to StoreService (EF-only, no raw SQL)
            var storeSvc = HttpContext.RequestServices.GetRequiredService<IStoreService>();
            var result = await storeSvc.RenameOrMergeAsync(receipt.StoreId, storeName, ct);
            receipt.StoreId = result.Id;
            receipt.Store = result;
        }

        private async Task UpdateStoreLocationAsync(Receipt receipt, EditReceiptRequest body, CancellationToken ct)
        {
            if (!HasStoreLocationData(body)) 
                return;

            var locationData = ExtractStoreLocationData(body);
            var existingLocation = await FindExistingLocationAsync(receipt.StoreId, locationData, ct);
            
            if (existingLocation == null)
            {
                existingLocation = CreateNewStoreLocation(receipt.StoreId, locationData);
                _db.StoreLocations.Add(existingLocation);
                // Defer SaveChanges to the end (single transaction)
            }
            else if (!string.IsNullOrWhiteSpace(locationData.VatNumber))
            {
                existingLocation.VatNumber = locationData.VatNumber;
            }
            
            receipt.StoreLocationId = existingLocation.Id;
            receipt.StoreLocation = existingLocation;
        }

        private bool HasStoreLocationData(EditReceiptRequest body)
        {
            return !string.IsNullOrWhiteSpace(body.StoreAddress) ||
                   !string.IsNullOrWhiteSpace(body.StoreCity) ||
                   !string.IsNullOrWhiteSpace(body.StorePostalCode) ||
                   !string.IsNullOrWhiteSpace(body.StoreVatNumber);
        }

        private (string? Address, string? City, string? PostalCode, string? VatNumber) ExtractStoreLocationData(EditReceiptRequest body)
        {
            return (
                body.StoreAddress?.Trim(),
                body.StoreCity?.Trim(),
                body.StorePostalCode?.Trim(),
                body.StoreVatNumber?.Trim()
            );
        }

        private async Task<StoreLocation?> FindExistingLocationAsync(Guid storeId, (string? Address, string? City, string? PostalCode, string? VatNumber) locationData, CancellationToken ct)
        {
            return await _db.StoreLocations.FirstOrDefaultAsync(
                sl => sl.StoreId == storeId &&
                      sl.Address == locationData.Address &&
                      sl.City == locationData.City &&
                      sl.PostalCode == locationData.PostalCode,
                ct);
        }

        private StoreLocation CreateNewStoreLocation(Guid storeId, (string? Address, string? City, string? PostalCode, string? VatNumber) locationData)
        {
            return new StoreLocation
            {
                StoreId = storeId,
                Address = locationData.Address,
                City = locationData.City,
                PostalCode = locationData.PostalCode,
                VatNumber = locationData.VatNumber
            };
        }

        private async Task ProcessLineModificationsAsync(Receipt receipt, EditReceiptRequest body, List<(string Label, Guid TypeId, Guid CategoryId)> feedbackTargets, CancellationToken ct)
        {
            if (body.Lines == null || body.Lines.Count == 0) 
                return;

            var orderedLines = receipt.Lines.OrderBy(l => l.SortIndex).ThenBy(l => l.Id).ToList();
            
            // Process removals first (in descending order to maintain indices)
            ProcessLineRemovals(receipt, body, orderedLines);
            
            // Process updates
            await ProcessLineUpdatesAsync(body, orderedLines, feedbackTargets, ct);
        }

        private void ProcessLineRemovals(Receipt receipt, EditReceiptRequest body, List<ReceiptLine> orderedLines)
        {
            var removals = body.Lines
                .Where(p => p.Remove == true)
                .OrderByDescending(p => p.Index)
                .ToList();
                
            foreach (var removal in removals)
            {
                if (removal.Index >= 0 && removal.Index < orderedLines.Count)
                {
                    var lineToRemove = orderedLines[removal.Index];
                    receipt.Lines.Remove(lineToRemove);
                    orderedLines.RemoveAt(removal.Index);
                }
            }
        }

        private async Task ProcessLineUpdatesAsync(EditReceiptRequest body, List<ReceiptLine> orderedLines, List<(string Label, Guid TypeId, Guid CategoryId)> feedbackTargets, CancellationToken ct)
        {
            var updates = body.Lines.Where(p => p.Remove != true);
            
            foreach (var patch in updates)
            {
                if (patch.Index < 0 || patch.Index >= orderedLines.Count) 
                    continue;

                var line = orderedLines[patch.Index];
                var wasModified = await ApplyLineUpdatesAsync(line, patch, ct);
                
                if (wasModified && line.PredictedCategoryId.HasValue)
                {
                    var typeId = line.PredictedTypeId ?? line.Product?.ProductTypeId;
                    if (typeId.HasValue)
                    {
                        feedbackTargets.Add((line.LabelRaw, typeId.Value, line.PredictedCategoryId.Value));
                    }
                }
            }
        }

        private async Task<bool> ApplyLineUpdatesAsync(ReceiptLine line, EditReceiptLine patch, CancellationToken ct)
        {
            bool wasModified = false;

            // Update basic properties
            if (!string.IsNullOrWhiteSpace(patch.LabelRaw))
            {
                var updated = patch.LabelRaw.Trim();
                if (!string.Equals(updated, line.LabelRaw, StringComparison.Ordinal))
                {
                    line.LabelRaw = updated;
                    wasModified = true;
                }
            }

            if (patch.Qty.HasValue) { line.Qty = patch.Qty.Value; wasModified = true; }
            if (patch.UnitPrice.HasValue) { line.UnitPrice = patch.UnitPrice.Value; wasModified = true; }
            if (patch.LineTotal.HasValue) { line.LineTotal = patch.LineTotal.Value; wasModified = true; }
            if (patch.VatRate.HasValue) line.VatRate = patch.VatRate.Value;

            // Update type and category predictions
            wasModified |= await UpdateLinePredictionsAsync(line, patch, ct);

            return wasModified;
        }

        private async Task<bool> UpdateLinePredictionsAsync(ReceiptLine line, EditReceiptLine patch, CancellationToken ct)
        {
            bool wasModified = false;

            // Update ProductType
            if (patch.FinalTypeId.HasValue || !string.IsNullOrWhiteSpace(patch.FinalTypeName))
            {
                var typeId = await ResolveProductTypeIdAsync(patch, ct);
                if (typeId.HasValue)
                {
                    line.PredictedTypeId = typeId;
                    wasModified = true;
                }
            }

            // Update Category
            if (patch.FinalCategoryId.HasValue || !string.IsNullOrWhiteSpace(patch.FinalCategoryName))
            {
                var categoryId = await ResolveCategoryIdAsync(patch, ct);
                if (categoryId.HasValue)
                {
                    line.PredictedCategoryId = categoryId;
                    line.PredictionConfidence = 1.0m;
                    wasModified = true;
                }
            }

            return wasModified;
        }

        private async Task<Guid?> ResolveProductTypeIdAsync(EditReceiptLine patch, CancellationToken ct)
        {
            if (patch.FinalTypeId.HasValue)
                return patch.FinalTypeId;

            if (!string.IsNullOrWhiteSpace(patch.FinalTypeName))
            {
                return await FindOrCreateProductTypeAsync(patch.FinalTypeName.Trim(), ct);
            }

            return null;
        }

        private async Task<Guid?> ResolveCategoryIdAsync(EditReceiptLine patch, CancellationToken ct)
        {
            if (patch.FinalCategoryId.HasValue)
                return patch.FinalCategoryId;

            if (!string.IsNullOrWhiteSpace(patch.FinalCategoryName))
            {
                return await FindOrCreateCategoryAsync(patch.FinalCategoryName.Trim(), ct);
            }

            return null;
        }

        private async Task AddNewLinesAsync(Receipt receipt, EditReceiptRequest body, List<(string Label, Guid TypeId, Guid CategoryId)> feedbackTargets, CancellationToken ct)
        {
            if (body.AddLines == null || body.AddLines.Count == 0)
                return;

            foreach (var newLineData in body.AddLines)
            {
                var newLine = await CreateNewReceiptLineAsync(receipt, newLineData, ct);

                // Reset the Id to let EF Core generate it
                newLine.Id = Guid.Empty;

                receipt.Lines.Add(newLine);

                // Explicitly mark as Added to ensure EF Core tracks it correctly
                _db.Entry(newLine).State = Microsoft.EntityFrameworkCore.EntityState.Added;

                // Add to feedback if both type and category are set
                if (newLine.PredictedTypeId.HasValue && newLine.PredictedCategoryId.HasValue)
                {
                    feedbackTargets.Add((newLine.LabelRaw, newLine.PredictedTypeId.Value, newLine.PredictedCategoryId.Value));
                }
            }
        }

        private async Task<ReceiptLine> CreateNewReceiptLineAsync(Receipt receipt, NewReceiptLine newLineData, CancellationToken ct)
        {
            // Resolve quantities and totals
            var (qty, unitPrice, lineTotal) = ResolveLineQuantitiesAndTotals(newLineData);
            
            // Resolve type and category
            var typeId = await ResolveNewLineProductTypeId(newLineData, ct);
            var categoryId = await ResolveNewLineCategoryId(newLineData, ct);
            
            // Calculate next sort index
            var nextIndex = (receipt.Lines.Any() ? receipt.Lines.Max(x => (int?)x.SortIndex) ?? -1 : -1) + 1;

            return new ReceiptLine
            {
                LabelRaw = newLineData.LabelRaw.Trim(),
                Qty = qty,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                VatRate = newLineData.VatRate,
                SortIndex = nextIndex,
                ProductId = null,
                PredictedTypeId = typeId,
                PredictedCategoryId = categoryId,
                PredictionConfidence = categoryId.HasValue ? 1.0m : null
            };
        }

        private (decimal qty, decimal unitPrice, decimal lineTotal) ResolveLineQuantitiesAndTotals(NewReceiptLine newLineData)
        {
            var qty = newLineData.Qty;
            var unitPrice = newLineData.UnitPrice;
            var lineTotal = newLineData.LineTotal;
            
            // Apply business rules for quantity/price calculation
            if (qty <= 0 && unitPrice >= 0 && lineTotal == 0) 
                qty = 1;
            if (lineTotal == 0 && unitPrice >= 0 && qty >= 0) 
                lineTotal = unitPrice * qty;
                
            return (qty, unitPrice, lineTotal);
        }

        private async Task<Guid?> ResolveNewLineProductTypeId(NewReceiptLine newLineData, CancellationToken ct)
        {
            if (newLineData.FinalTypeId.HasValue)
                return newLineData.FinalTypeId;

            if (!string.IsNullOrWhiteSpace(newLineData.FinalTypeName))
            {
                return await FindOrCreateProductTypeAsync(newLineData.FinalTypeName.Trim(), ct);
            }

            return null;
        }

        private async Task<Guid?> ResolveNewLineCategoryId(NewReceiptLine newLineData, CancellationToken ct)
        {
            if (newLineData.FinalCategoryId.HasValue)
                return newLineData.FinalCategoryId;

            if (!string.IsNullOrWhiteSpace(newLineData.FinalCategoryName))
            {
                return await FindOrCreateCategoryAsync(newLineData.FinalCategoryName.Trim(), ct);
            }

            return null;
        }

        private void ApplyLineReordering(Receipt receipt, EditReceiptRequest body, int existingLinesCount)
        {
            // If Order is null or empty, skip reordering
            if (body.Order == null || body.Order.Count == 0)
                return;

            // If order count doesn't match existing lines count (before addLines), skip
            if (body.Order.Count != existingLinesCount)
                return;

            // Get only existing lines (exclude newly added lines by using ChangeTracker)
            // Lines in "Added" state should not be reordered
            var existingLines = receipt.Lines
                .Where(l => _db.Entry(l).State != Microsoft.EntityFrameworkCore.EntityState.Added)
                .OrderBy(l => l.SortIndex)
                .ThenBy(l => l.Id)
                .ToList();

            // Verify count matches
            if (existingLines.Count != existingLinesCount)
                return;

            var isValidOrder = body.Order.All(i => i >= 0 && i < existingLines.Count) &&
                              body.Order.Distinct().Count() == existingLines.Count;

            if (!isValidOrder)
                return;

            // Apply new sort indices based on the order array
            for (int newPosition = 0; newPosition < body.Order.Count; newPosition++)
            {
                var originalIndex = body.Order[newPosition];
                existingLines[originalIndex].SortIndex = newPosition;
            }
        }

        private async Task FinalizeReceiptAsync(Receipt receipt, List<(string Label, Guid TypeId, Guid CategoryId)> feedbackTargets, CancellationToken ct)
        {
            // Recalculate total
            receipt.Total = receipt.Lines.Sum(x => x.LineTotal);
            
            // Save changes
            await _db.SaveChangesAsync(ct);
            
            // Send ML feedback
            foreach (var target in feedbackTargets)
            {
                await _classifier.FeedbackAsync(target.Label, null, target.TypeId, target.CategoryId, ct);
            }
        }

        private async Task<Guid> FindOrCreateProductTypeAsync(string typeName, CancellationToken ct)
        {
            var lower = typeName.ToLowerInvariant();
            var existing = await _db.ProductTypes.FirstOrDefaultAsync(t => t.Name.ToLower() == lower, ct);
            
            if (existing != null)
                return existing.Id;

            var normalized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
            var newType = new ProductType { Name = normalized };
            _db.ProductTypes.Add(newType);
            // Defer SaveChanges to the end (single transaction)
            
            return newType.Id;
        }

        private async Task<Guid> FindOrCreateCategoryAsync(string categoryName, CancellationToken ct)
        {
            var lower = categoryName.ToLowerInvariant();
            var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == lower, ct);
            
            if (existing != null)
                return existing.Id;

            var normalized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
            var newCategory = new Category { Name = normalized };
            _db.Categories.Add(newCategory);
            // Defer SaveChanges to the end (single transaction)
            
            return newCategory.Id;
        }

        #endregion
    }
}
