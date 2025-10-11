using System.ComponentModel.DataAnnotations;

namespace WIB.Application.Contracts.Kie;

public class ReceiptDraft
{
    [Required]
    public ReceiptDraftStore Store { get; set; } = new();
    public DateTimeOffset Datetime { get; set; }
    public string Currency { get; set; } = "EUR";
    public List<ReceiptDraftLine> Lines { get; set; } = new();
    public ReceiptDraftTotals Totals { get; set; } = new();
}

public class ReceiptDraftStore
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Chain { get; set; }
    public string? PostalCode { get; set; }
    public string? VatNumber { get; set; }
}

public class ReceiptDraftLine
{
    [Required]
    public string LabelRaw { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    // Opzionali: solo per prodotti a peso
    public decimal? WeightKg { get; set; }
    public decimal? PricePerKg { get; set; }
}

public class ReceiptDraftTotals
{
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
}
