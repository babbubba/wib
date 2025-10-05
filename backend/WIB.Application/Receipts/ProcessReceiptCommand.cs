namespace WIB.Application.Receipts;

public record ProcessReceiptCommand(Stream Image, string? ObjectKey = null);
