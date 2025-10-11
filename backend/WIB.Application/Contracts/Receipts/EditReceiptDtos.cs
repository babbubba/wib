using System.ComponentModel.DataAnnotations;

namespace WIB.Application.Contracts.Receipts;

public class EditReceiptRequest
{
    public string? StoreName { get; set; }
    public string? StoreAddress { get; set; }
    public string? StoreCity { get; set; }
    public string? StorePostalCode { get; set; }
    public string? StoreVatNumber { get; set; }
    public DateTimeOffset? Datetime { get; set; }
    public string? Currency { get; set; }
    public List<EditReceiptLine> Lines { get; set; } = new();
    public List<NewReceiptLine> AddLines { get; set; } = new();
    // Optional: reordering of existing lines by their original indices (after any removals applied client-side)
    public List<int>? Order { get; set; }
}

public class EditReceiptLine
{
    [Required]
    public int Index { get; set; }
    public string? LabelRaw { get; set; }
    public decimal? Qty { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    public Guid? FinalCategoryId { get; set; }
    public string? FinalCategoryName { get; set; }
    public string? ProductName { get; set; }
    public bool? Remove { get; set; }
}

public class NewReceiptLine
{
    [Required]
    public string LabelRaw { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? VatRate { get; set; }
    public Guid? FinalCategoryId { get; set; }
    public string? FinalCategoryName { get; set; }
    public string? ProductName { get; set; }
}
