using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Storage;

namespace WIB.API.Controllers;

public record StorageListResponse(List<string> Keys);
public record StorageBulkReprocessRequest(List<string> ObjectKeys);

[ApiController]
[Authorize(Roles = "wmc")]
[Route("storage")] 
public class StorageController : ControllerBase
{
    private readonly MinioOptions _opts;
    private readonly WibDbContext _db;
    private readonly IReceiptQueue _queue;

    public StorageController(IOptions<MinioOptions> opts, WibDbContext db, IReceiptQueue queue)
    {
        _opts = opts.Value;
        _db = db;
        _queue = queue;
    }

    [HttpGet("receipts")]
    public async Task<ActionResult<StorageListResponse>> ListReceipts([FromQuery] string? prefix, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (take <= 0 || take > 200) take = 50;

        var client = new MinioClient()
            .WithEndpoint(_opts.Endpoint)
            .WithCredentials(_opts.AccessKey, _opts.SecretKey)
            .WithSSL(_opts.WithSSL)
            .Build();

        // Ensure bucket exists; if not, return empty list instead of 500
        var bucketExists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_opts.Bucket), ct);
        if (!bucketExists)
            return Ok(new StorageListResponse(new List<string>()));

        var keys = new List<string>();
        var args = new ListObjectsArgs()
            .WithBucket(_opts.Bucket)
            .WithPrefix(prefix ?? string.Empty)
            .WithRecursive(true);

        try
        {
            var observable = client.ListObjectsAsync(args, cancellationToken: ct);
            var tcs = new TaskCompletionSource();
            var sub = observable.Subscribe(item => { if (item != null && item.Key != null) { if (keys.Count < take) keys.Add(item.Key); } },
                ex => tcs.TrySetException(ex),
                () => tcs.TrySetResult());
            await tcs.Task.ConfigureAwait(false);
            sub.Dispose();
        }
        catch
        {
            // In case of transient errors, degrade gracefully
            return Ok(new StorageListResponse(new List<string>()));
        }
        return Ok(new StorageListResponse(keys));
    }

    [HttpGet("receipts/unprocessed")]
    public async Task<ActionResult<StorageListResponse>> ListUnprocessed([FromQuery] string? prefix, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (take <= 0 || take > 200) take = 50;
        var usedList = await _db.Receipts.AsNoTracking()
            .Where(r => r.ImageObjectKey != null)
            .Select(r => r.ImageObjectKey!)
            .ToListAsync(ct);
        var used = usedList.ToHashSet();

        var client = new MinioClient()
            .WithEndpoint(_opts.Endpoint)
            .WithCredentials(_opts.AccessKey, _opts.SecretKey)
            .WithSSL(_opts.WithSSL)
            .Build();

        // Ensure bucket exists; if not, there are no objects to return
        var bucketExists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_opts.Bucket), ct);
        if (!bucketExists)
            return Ok(new StorageListResponse(new List<string>()));

        var result = new List<string>();
        var args = new ListObjectsArgs().WithBucket(_opts.Bucket).WithPrefix(prefix ?? string.Empty).WithRecursive(true);
        try
        {
            var observable = client.ListObjectsAsync(args, cancellationToken: ct);
            var tcs = new TaskCompletionSource();
            var sub = observable.Subscribe(item =>
            {
                if (item?.Key != null && !used.Contains(item.Key) && result.Count < take)
                    result.Add(item.Key);
            },
            ex => tcs.TrySetException(ex),
            () => tcs.TrySetResult());
            await tcs.Task.ConfigureAwait(false);
            sub.Dispose();
        }
        catch
        {
            return Ok(new StorageListResponse(new List<string>()));
        }
        return Ok(new StorageListResponse(result));
    }

    [HttpGet("object")]
    public async Task<IActionResult> GetObject([FromQuery] string objectKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return BadRequest();
        var ms = new MemoryStream();
        var client = new MinioClient()
            .WithEndpoint(_opts.Endpoint)
            .WithCredentials(_opts.AccessKey, _opts.SecretKey)
            .WithSSL(_opts.WithSSL)
            .Build();
        try
        {
            // If bucket doesn't exist or object missing, return NotFound
            var bucketExists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_opts.Bucket), ct);
            if (!bucketExists) return NotFound();
            var args = new GetObjectArgs().WithBucket(_opts.Bucket).WithObject(objectKey).WithCallbackStream(s => s.CopyTo(ms));
            await client.GetObjectAsync(args, ct);
        }
        catch
        {
            return NotFound();
        }
        ms.Position = 0;
        return File(ms, "image/jpeg");
    }

    [HttpPost("reprocess/bulk")]
    public async Task<IActionResult> ReprocessBulk([FromBody] StorageBulkReprocessRequest req, CancellationToken ct)
    {
        if (req.ObjectKeys == null || req.ObjectKeys.Count == 0) return BadRequest("Empty objectKeys");
        int enq = 0;
        foreach (var k in req.ObjectKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            await _queue.EnqueueAsync(k, ct);
            enq++;
        }
        return Accepted(new { enqueued = enq });
    }
}
