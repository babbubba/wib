using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WIB.Application.Interfaces;

namespace WIB.Application.Receipts
{
    public class ProcessReceiptCommandHandler
    {
        private readonly IOcrClient _ocr;
        private readonly IKieClient _kie;
        private readonly IProductClassifier _classifier;
        private readonly IReceiptStorage _storage;
        private readonly INameMatcher _names;
        private readonly IImageStorage _imageStorage;
        private readonly IProductMatcher _productMatcher;
        private readonly IRedisLogger? _redisLogger;

        public ProcessReceiptCommandHandler(
            IOcrClient ocr,
            IKieClient kie,
            IProductClassifier classifier,
            IReceiptStorage storage,
            IImageStorage imageStorage,
            INameMatcher names,
            IProductMatcher productMatcher,
            IRedisLogger? redisLogger = null)
        {
            _ocr = ocr;
            _kie = kie;
            _classifier = classifier;
            _storage = storage;
            _imageStorage = imageStorage;
            _names = names;
            _productMatcher = productMatcher;
            _redisLogger = redisLogger;
        }

        public async Task Handle(ProcessReceiptCommand command, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await command.Image.CopyToAsync(ms, ct);
            ms.Position = 0;

            string? objectKey = command.ObjectKey;
            if (string.IsNullOrEmpty(objectKey))
            {
                try
                {
                    if (_redisLogger != null)
                        await _redisLogger.DebugAsync("Saving Image", "Image not yet saved to storage, saving now", ct: ct);
                    objectKey = await _imageStorage.SaveAsync(ms, null, ct);
                    if (_redisLogger != null)
                        await _redisLogger.DebugAsync("Image Saved", $"Image saved with key: {objectKey}", new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>" }, ct);
                }
                catch (Exception ex)
                {
                    if (_redisLogger != null)
                        await _redisLogger.ErrorAsync("Image Save Failed", "Failed to save image to storage", ex, ct: ct);
                }
                ms.Position = 0;
            }

            try
            {
                if (_redisLogger != null)
                    await _redisLogger.InfoAsync("Calling OCR Service", $"Extracting text from receipt image", new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>" }, ct);

                var ocrResult = await _ocr.ExtractAsync(ms, ct);
                ms.Position = 0;

                if (_redisLogger != null)
                    await _redisLogger.InfoAsync("OCR Complete", $"Text extracted, length: {ocrResult?.Length ?? 0} chars", new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>", ["textLength"] = ocrResult?.Length ?? 0 }, ct);

                var imgBytes = ms.ToArray();

                if (_redisLogger != null)
                    await _redisLogger.InfoAsync("Calling KIE Service", "Extracting structured fields from receipt", new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>" }, ct);

                var kie = await _kie.ExtractFieldsAsync(ocrResult, imgBytes, ct);

                if (_redisLogger != null)
                    await _redisLogger.InfoAsync("KIE Complete", $"Extracted store: {kie.Store?.Name}, lines: {kie.Lines?.Count ?? 0}", new Dictionary<string, object>
                    {
                        ["objectKey"] = objectKey ?? "<null>",
                        ["storeName"] = kie.Store?.Name ?? "<null>",
                        ["lineCount"] = kie.Lines?.Count ?? 0,
                        ["total"] = kie.Totals?.Total ?? 0
                    }, ct);

            Guid? existingStoreId = null;
            if (!string.IsNullOrWhiteSpace(kie.Store.Name))
            {
                var match = await _names.MatchStoreAsync(kie.Store.Name, ct);
                if (match.HasValue) existingStoreId = match.Value.storeId;
            }

            // Validate UserId - should be provided by caller (Worker will resolve Guid.Empty before calling)
            if (command.UserId == Guid.Empty)
            {
                throw new InvalidOperationException("Valid UserId is required to process receipt. Admin operations should resolve UserId before calling handler.");
            }

            var receipt = new WIB.Domain.Receipt
            {
                UserId = command.UserId,
                Store = existingStoreId.HasValue ? null : new WIB.Domain.Store
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
                StoreId = existingStoreId ?? Guid.Empty,
                Date = kie.Datetime,
                Currency = kie.Currency,
                TaxTotal = kie.Totals.Tax,
                Total = kie.Totals.Total,
                RawText = ocrResult,
                ImageObjectKey = objectKey,
                OcrStoreX = kie.StoreOcrX,
                OcrStoreY = kie.StoreOcrY,
                OcrStoreW = kie.StoreOcrW,
                OcrStoreH = kie.StoreOcrH,
                Lines = new List<WIB.Domain.ReceiptLine>()
            };

            if (receipt.StoreLocation != null)
            {
                receipt.StoreLocation.Store = receipt.Store;
            }

            var idx = 0;
            var mlCallCount = 0;
            foreach (var l in kie.Lines)
            {
                if (LooksLikeTotalOrPayment(l.LabelRaw ?? string.Empty))
                    continue;

                var corrected = await _names.CorrectProductLabelAsync(l.LabelRaw ?? string.Empty, ct);
                var labelRaw = (corrected ?? l.LabelRaw) ?? string.Empty;

                try
                {
                    mlCallCount++;
                    if (_redisLogger != null && mlCallCount == 1)
                        await _redisLogger.InfoAsync("Calling ML Service", "Starting ML classification for receipt lines", new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>", ["lineCount"] = kie.Lines.Count }, ct);

                    var pred = await _classifier.PredictAsync(l.LabelRaw ?? string.Empty, ct);
                    var typeId = pred.TypeId;
                    var categoryId = pred.CategoryId;
                    var confidence = pred.Confidence;

                    if (_redisLogger != null)
                        await _redisLogger.DebugAsync("ML Classification", $"Line '{labelRaw}' classified with confidence {confidence:F2}", new Dictionary<string, object>
                        {
                            ["objectKey"] = objectKey ?? "<null>",
                            ["label"] = labelRaw,
                            ["confidence"] = confidence
                        }, ct);

                // Try to match or create product
                Guid? productId = null;
                var productMatch = await _productMatcher.FindOrCreateProductAsync(
                    labelRaw,
                    null, // brand will be extracted from labelRaw by ProductMatcher
                    typeId,
                    categoryId,
                    confidence,
                    confidenceThreshold: 0.75f, // Slightly lower threshold for receipt processing
                    ct);

                if (productMatch != null)
                {
                    productId = productMatch.Product.Id;
                    
                    // Update predictions if product match has better type/category info
                    if (productMatch.Product.ProductTypeId != Guid.Empty)
                        typeId = productMatch.Product.ProductTypeId;
                    if (productMatch.Product.CategoryId.HasValue)
                        categoryId = productMatch.Product.CategoryId;
                }

                receipt.Lines.Add(new WIB.Domain.ReceiptLine
                {
                    LabelRaw = labelRaw,
                    ProductId = productId,
                    Qty = l.Qty,
                    UnitPrice = l.UnitPrice,
                    LineTotal = l.LineTotal,
                    VatRate = l.VatRate,
                    WeightKg = l.WeightKg,
                    PricePerKg = l.PricePerKg,
                    SortIndex = idx++,
                    OcrX = l.OcrX,
                    OcrY = l.OcrY,
                    OcrW = l.OcrW,
                    OcrH = l.OcrH,
                    PredictedTypeId = typeId,
                    PredictedCategoryId = categoryId,
                    PredictionConfidence = (decimal?)confidence
                });
                }
                catch (Exception ex)
                {
                    if (_redisLogger != null)
                        await _redisLogger.ErrorAsync("ML Classification Error", $"Failed to classify line: {labelRaw}", ex, new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>", ["label"] = labelRaw }, ct);
                    // Continue processing other lines despite error
                }
            }

            if (existingStoreId.HasValue && receipt.StoreLocation != null)
            {
                receipt.StoreLocation.StoreId = existingStoreId.Value;
            }

            if (_redisLogger != null)
                await _redisLogger.InfoAsync("Saving Receipt", $"Persisting receipt to database: {receipt.Lines.Count} lines", new Dictionary<string, object>
                {
                    ["objectKey"] = objectKey ?? "<null>",
                    ["lineCount"] = receipt.Lines.Count,
                    ["total"] = receipt.Total,
                    ["store"] = receipt.Store?.Name ?? "<existing>"
                }, ct);

            await _storage.SaveAsync(receipt, ct);

            if (_redisLogger != null)
                await _redisLogger.InfoAsync("Receipt Saved", "Receipt successfully persisted to database", new Dictionary<string, object>
                {
                    ["objectKey"] = objectKey ?? "<null>",
                    ["receiptId"] = receipt.Id.ToString()
                }, ct);
            }
            catch (Exception ex)
            {
                if (_redisLogger != null)
                    await _redisLogger.ErrorAsync("Processing Pipeline Error", "Error in OCR/KIE/ML pipeline", ex, new Dictionary<string, object> { ["objectKey"] = objectKey ?? "<null>" }, ct);
                throw;
            }
        }

        private static bool LooksLikeTotalOrPayment(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            var s = label.Trim().ToLowerInvariant();
            if (s.Contains("totale") || s.Contains("subtotale") || s.Contains("pagato") || s.Contains("contante") || s.Contains("resto") || s.Contains("iban") || s.Contains("carta") || s.Contains("tessera") || s.Contains("sconto"))
                return true;
            bool anyLetter = false; foreach (var ch in s) { if (char.IsLetter(ch)) { anyLetter = true; break; } }
            if (!anyLetter) return true;
            return false;
        }
    }
}




