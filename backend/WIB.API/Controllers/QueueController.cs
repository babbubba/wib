using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

public record QueueStatusDto(long Length, List<string> Pending);
public record ReprocessRequest(Guid? ReceiptId, string? ObjectKey);

[ApiController]
[Authorize(Roles = "wmc")]
[Route("queue")]
public class QueueController : ControllerBase
{
    private readonly IReceiptQueue _queue;
    private readonly WibDbContext _db;
    public QueueController(IReceiptQueue queue, WibDbContext db)
    {
        _queue = queue;
        _db = db;
    }

    [HttpGet("status")]
    public async Task<ActionResult<QueueStatusDto>> Status([FromQuery] int take = 20, CancellationToken ct = default)
    {
        if (take <= 0 || take > 100) take = 20;
        var len = await _queue.GetLengthAsync(ct);
        var list = await _queue.PeekAsync(take, ct);

        // Hide items that are already processed (present in Receipts.ImageObjectKey)
        // to avoid showing the same image as both "in coda" and "già processata" in WMC.
        var keys = list.ToList();
        if (keys.Count > 0)
        {
            var used = await _db.Receipts.AsNoTracking()
                .Where(r => r.ImageObjectKey != null && keys.Contains(r.ImageObjectKey!))
                .Select(r => r.ImageObjectKey!)
                .ToListAsync(ct);
            if (used.Count > 0)
            {
                var usedSet = used.ToHashSet();
                keys = keys.Where(k => !usedSet.Contains(k)).ToList();
            }
        }

        return Ok(new QueueStatusDto(len, keys));
    }

    [HttpPost("reprocess")]
    public async Task<IActionResult> Reprocess([FromBody] ReprocessRequest req, CancellationToken ct)
    {
        string? objectKey = req.ObjectKey;
        if (objectKey is null && req.ReceiptId.HasValue)
        {
            var rec = await _db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.ReceiptId.Value, ct);
            if (rec == null || string.IsNullOrWhiteSpace(rec.ImageObjectKey)) return NotFound();
            objectKey = rec.ImageObjectKey;
        }
        if (string.IsNullOrWhiteSpace(objectKey)) return BadRequest("Missing objectKey or receiptId");
        await _queue.EnqueueAsync(objectKey!, ct);
        return Accepted(new { objectKey });
    }
}
