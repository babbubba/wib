using WIB.Application.Interfaces;
using WIB.Application.Receipts;

namespace WIB.Worker;

public class ReceiptProcessor
{
    private readonly IImageStorage _images;
    private readonly ProcessReceiptCommandHandler _handler;

    public ReceiptProcessor(IImageStorage images, ProcessReceiptCommandHandler handler)
    {
        _images = images;
        _handler = handler;
    }

    public async Task ProcessAsync(string objectKey, CancellationToken ct)
    {
        await using var stream = await _images.GetAsync(objectKey, ct);
        await _handler.Handle(new ProcessReceiptCommand(stream, objectKey), ct);
    }
}
