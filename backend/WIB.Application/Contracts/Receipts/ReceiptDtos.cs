using System.ComponentModel.DataAnnotations;

namespace WIB.Application.Contracts.Receipts;

public class ReceiptDto
{
    public Guid Id { get; set; }
    public ReceiptStoreDto Store { get; set; } = new();
    public DateTimeOffset Datetime { get; set; }
    public string Currency { get; set; } = "EUR";
    public List<ReceiptLineDto> Lines { get; set; } = new();
    public ReceiptTotalsDto Totals { get; set; } = new();
}

public class ReceiptStoreDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Chain { get; set; }
    public string? PostalCode { get; set; }
    public string? VatNumber { get; set; }
    public int? OcrX { get; set; }
    public int? OcrY { get; set; }
    public int? OcrW { get; set; }
    public int? OcrH { get; set; }
}

public class ReceiptLineDto
{
    [Required]
    public string LabelRaw { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    public int? OcrX { get; set; }
    public int? OcrY { get; set; }
    public int? OcrW { get; set; }
    public int? OcrH { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}

public class ReceiptTotalsDto
{
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
}

public class UploadReceiptResponse
{
    public Guid Id { get; set; }
}

public class ReceiptListItemDto
{
    public Guid Id { get; set; }
    public DateTimeOffset Datetime { get; set; }
    public string? StoreName { get; set; }
    public decimal Total { get; set; }
}
