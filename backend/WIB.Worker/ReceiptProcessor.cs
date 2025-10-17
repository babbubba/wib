using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Infrastructure.Data;

namespace WIB.Worker;

public class ReceiptProcessor
{
    private readonly IImageStorage _images;
    private readonly ProcessReceiptCommandHandler _handler;
    private readonly WibDbContext _db;
    private readonly IRedisLogger _redisLogger;

    public ReceiptProcessor(IImageStorage images, ProcessReceiptCommandHandler handler, WibDbContext db, IRedisLogger redisLogger)
    {
        _images = images;
        _handler = handler;
        _db = db;
        _redisLogger = redisLogger;
    }

    public async Task ProcessAsync(string objectKey, Guid userId, CancellationToken ct)
    {
        try
        {
            // Resolve empty UserId (admin operations) to first available user
            if (userId == Guid.Empty)
            {
                await _redisLogger.DebugAsync("Resolving UserId", "Attempting to resolve empty UserId to first available user", new Dictionary<string, object> { ["objectKey"] = objectKey });
                var firstUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(ct);
                if (firstUser == null)
                {
                    throw new InvalidOperationException("No users found in database. Cannot process receipt without a valid UserId.");
                }
                userId = firstUser.Id;
                await _redisLogger.DebugAsync("UserId Resolved", $"Resolved userId to: {userId}", new Dictionary<string, object> { ["objectKey"] = objectKey, ["userId"] = userId.ToString() });
            }

            await _redisLogger.InfoAsync("Downloading Image", $"Downloading image from MinIO: {objectKey}", new Dictionary<string, object> { ["objectKey"] = objectKey });
            await using var stream = await _images.GetAsync(objectKey, ct);

            await _redisLogger.InfoAsync("Processing Receipt", $"Starting OCR/KIE/ML pipeline for: {objectKey}", new Dictionary<string, object> { ["objectKey"] = objectKey, ["userId"] = userId.ToString() });
            await _handler.Handle(new ProcessReceiptCommand(stream, userId, objectKey), ct);
        }
        catch (Exception ex)
        {
            await _redisLogger.ErrorAsync(
                "Receipt Processor Error",
                $"Error in receipt processor for {objectKey}",
                ex,
                new Dictionary<string, object>
                {
                    ["objectKey"] = objectKey,
                    ["userId"] = userId.ToString()
                });
            throw;
        }
    }
}
