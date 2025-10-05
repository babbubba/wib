using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.Application.Contracts.Receipts;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("receipts")]
public class ReceiptController : ControllerBase
{
    private readonly IImageStorage _imageStorage;
    private readonly IReceiptQueue _queue;
    private readonly WibDbContext _db;

    public ReceiptController(IImageStorage imageStorage, IReceiptQueue queue, WibDbContext db)
    {
        _imageStorage = imageStorage;
        _queue = queue;
        _db = db;
    }

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var objectKey = await _imageStorage.SaveAsync(stream, file.ContentType, ct);
        await _queue.EnqueueAsync(objectKey, ct);
        return Accepted(new { objectKey });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiptListItemDto>>> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (take <= 0 || take > 100) take = 20;
        var items = await _db.Receipts
            .AsNoTracking()
            .Include(r => r.Store)
            .OrderByDescending(r => r.Date)
            .Skip(skip)
            .Take(take)
            .Select(r => new ReceiptListItemDto
            {
                Id = r.Id,
                Datetime = r.Date,
                StoreName = r.Store != null ? r.Store.Name : null,
                Total = r.Total
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<LabelingItemDto>>> Pending([FromQuery] decimal maxConfidence = 0.6m, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (take <= 0 || take > 200) take = 50;
        var result = await (from l in _db.ReceiptLines.AsNoTracking()
                             join r in _db.Receipts.AsNoTracking() on l.ReceiptId equals r.Id
                             where l.PredictionConfidence == null || l.PredictionConfidence < maxConfidence
                             orderby r.Date descending
                             select new LabelingItemDto
                             {
                                 ReceiptId = r.Id,
                                 ReceiptLineId = l.Id,
                                 Datetime = r.Date,
                                 StoreName = null,
                                 LabelRaw = l.LabelRaw,
                                 PredictedTypeId = l.PredictedTypeId,
                                 PredictedCategoryId = l.PredictedCategoryId,
                                 Confidence = l.PredictionConfidence
                             })
                            .Take(take)
                            .ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts
            .Include(r => r.Store)
            .Include(r => r.StoreLocation)
            .Include(r => r.Lines)
                .ThenInclude(l => l.Product)
                    .ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt == null)
            return NotFound();

        // Build predicted category names map
        var predictedIds = receipt.Lines
            .Where(l => l.PredictedCategoryId.HasValue)
            .Select(l => l.PredictedCategoryId!.Value)
            .Distinct()
            .ToList();
        var catMap = await _db.Categories.AsNoTracking()
            .Where(c => predictedIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var dto = new ReceiptDto
        {
            Id = receipt.Id,
            Store = new ReceiptStoreDto
            {
                Name = receipt.Store?.Name ?? string.Empty,
                Address = receipt.StoreLocation?.Address,
                City = receipt.StoreLocation?.City,
                Chain = receipt.Store?.Chain,
                PostalCode = receipt.StoreLocation?.PostalCode,
                VatNumber = receipt.StoreLocation?.VatNumber
            },
            Datetime = receipt.Date,
            Currency = receipt.Currency,
            Lines = receipt.Lines.OrderBy(l => l.Id).Select(l => {
                var catId = l.PredictedCategoryId ?? l.Product?.CategoryId;
                string? catName = null;
                if (l.PredictedCategoryId.HasValue)
                    catMap.TryGetValue(l.PredictedCategoryId.Value, out catName);
                else
                    catName = l.Product?.Category?.Name;
                return new ReceiptLineDto {
                    LabelRaw = l.LabelRaw,
                    Qty = l.Qty,
                    UnitPrice = l.UnitPrice,
                    LineTotal = l.LineTotal,
                    VatRate = l.VatRate,
                    CategoryId = catId,
                    CategoryName = catName
                };
            }).ToList(),
            Totals = new ReceiptTotalsDto
            {
                Subtotal = receipt.Lines.Sum(x => x.LineTotal),
                Tax = receipt.TaxTotal ?? 0,
                Total = receipt.Total
            }
        };

        return Ok(dto);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt == null || string.IsNullOrEmpty(receipt.ImageObjectKey))
            return NotFound();

        var stream = await _imageStorage.GetAsync(receipt.ImageObjectKey!, ct);
        return File(stream, "image/jpeg");
    }

}
