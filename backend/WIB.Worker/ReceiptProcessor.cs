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

    public ReceiptProcessor(IImageStorage images, ProcessReceiptCommandHandler handler, WibDbContext db)
    {
        _images = images;
        _handler = handler;
        _db = db;
    }

    public async Task ProcessAsync(string objectKey, Guid userId, CancellationToken ct)
    {
        // Resolve empty UserId (admin operations) to first available user
        if (userId == Guid.Empty)
        {
            var firstUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(ct);
            if (firstUser == null)
            {
                throw new InvalidOperationException("No users found in database. Cannot process receipt without a valid UserId.");
            }
            userId = firstUser.Id;
        }

        await using var stream = await _images.GetAsync(objectKey, ct);
        await _handler.Handle(new ProcessReceiptCommand(stream, userId, objectKey), ct);
    }
}
