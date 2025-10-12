using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
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

        public ProcessReceiptCommandHandler(
            IOcrClient ocr, 
            IKieClient kie, 
            IProductClassifier classifier, 
            IReceiptStorage storage, 
            IImageStorage imageStorage, 
            INameMatcher names,
            IProductMatcher productMatcher)
        {
            _ocr = ocr;
            _kie = kie;
            _classifier = classifier;
            _storage = storage;
            _imageStorage = imageStorage;
            _names = names;
            _productMatcher = productMatcher;
        }

        public async Task Handle(ProcessReceiptCommand command, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await command.Image.CopyToAsync(ms, ct);
            ms.Position = 0;

            string? objectKey = command.ObjectKey;
            if (string.IsNullOrEmpty(objectKey))
            {
                try { objectKey = await _imageStorage.SaveAsync(ms, null, ct); } catch { }
                ms.Position = 0;
            }

            var ocrResult = await _ocr.ExtractAsync(ms, ct);
            ms.Position = 0;
            var imgBytes = ms.ToArray();
            var kie = await _kie.ExtractFieldsAsync(ocrResult, imgBytes, ct);

            Guid? existingStoreId = null;
            if (!string.IsNullOrWhiteSpace(kie.Store.Name))
            {
                var match = await _names.MatchStoreAsync(kie.Store.Name, ct);
                if (match.HasValue) existingStoreId = match.Value.storeId;
            }

            var receipt = new WIB.Domain.Receipt
            {
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
            foreach (var l in kie.Lines)
            {
                if (LooksLikeTotalOrPayment(l.LabelRaw ?? string.Empty))
                    continue;

                var corrected = await _names.CorrectProductLabelAsync(l.LabelRaw ?? string.Empty, ct);
                var labelRaw = (corrected ?? l.LabelRaw) ?? string.Empty;
                var pred = await _classifier.PredictAsync(l.LabelRaw ?? string.Empty, ct);
                var typeId = pred.TypeId; 
                var categoryId = pred.CategoryId; 
                var confidence = pred.Confidence;

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

            if (existingStoreId.HasValue && receipt.StoreLocation != null)
            {
                receipt.StoreLocation.StoreId = existingStoreId.Value;
            }

            await _storage.SaveAsync(receipt, ct);
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




