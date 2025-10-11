using WIB.Application.Interfaces;

namespace WIB.Application.Receipts;

public class ProcessReceiptCommandHandler
{
    private readonly IOcrClient _ocr;
    private readonly IKieClient _kie;
    private readonly IProductClassifier _classifier;
    private readonly IReceiptStorage _storage;
    private readonly IImageStorage _imageStorage;

    public ProcessReceiptCommandHandler(IOcrClient ocr, IKieClient kie, IProductClassifier classifier, IReceiptStorage storage, IImageStorage imageStorage)
    {
        _ocr = ocr;
        _kie = kie;
        _classifier = classifier;
        _storage = storage;
        _imageStorage = imageStorage;
    }

    public async Task Handle(ProcessReceiptCommand command, CancellationToken ct)
    {
        // Copy stream to memory to reuse for OCR
        using var ms = new MemoryStream();
        await command.Image.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Use provided object key (from orchestrator) or save new image (from API direct path)
        string? objectKey = command.ObjectKey;
        if (string.IsNullOrEmpty(objectKey))
        {
            try { objectKey = await _imageStorage.SaveAsync(ms, null, ct); } catch { /* swallow for now */ }
            ms.Position = 0;
        }

        var ocrResult = await _ocr.ExtractAsync(ms, ct);
        var kie = await _kie.ExtractFieldsAsync(ocrResult, ct);

        var receipt = new WIB.Domain.Receipt
        {
            Store = new WIB.Domain.Store
            {
                Name = kie.Store.Name,
                Chain = kie.Store.Chain
            },
            StoreLocation = new WIB.Domain.StoreLocation
            {
                Address = kie.Store.Address,
                City = kie.Store.City,
                PostalCode = kie.Store.PostalCode,
                VatNumber = kie.Store.VatNumber
            },
            Date = kie.Datetime,
            Currency = kie.Currency,
            TaxTotal = kie.Totals.Tax,
            Total = kie.Totals.Total,
            RawText = ocrResult,
            ImageObjectKey = objectKey,
            Lines = new List<WIB.Domain.ReceiptLine>()
        };

        // Link location to store for proper FK assignment
        if (receipt.StoreLocation != null)
        {
            receipt.StoreLocation.Store = receipt.Store;
        }

        foreach (var l in kie.Lines)
        {
            var (typeId, categoryId, confidence) = await _classifier.PredictAsync(l.LabelRaw, ct);
            receipt.Lines.Add(new WIB.Domain.ReceiptLine
            {
                LabelRaw = l.LabelRaw,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                LineTotal = l.LineTotal,
                VatRate = l.VatRate,
                WeightKg = l.WeightKg,
                PricePerKg = l.PricePerKg,
                PredictedTypeId = typeId,
                PredictedCategoryId = categoryId,
                PredictionConfidence = (decimal?)confidence
            });
        }

        // TODO: classificazione prodotti e popolamento ProductId
        await _storage.SaveAsync(receipt, ct);
    }
}
