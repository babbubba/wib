namespace WIB.Application.Contracts.Receipts;

public class LabelingItemDto
{
    public Guid ReceiptId { get; set; }
    public Guid ReceiptLineId { get; set; }
    public DateTimeOffset Datetime { get; set; }
    public string? StoreName { get; set; }
    public string LabelRaw { get; set; } = string.Empty;
    public Guid? PredictedTypeId { get; set; }
    public Guid? PredictedCategoryId { get; set; }
    public decimal? Confidence { get; set; }
}

