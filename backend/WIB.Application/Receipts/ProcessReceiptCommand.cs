namespace WIB.Application.Receipts;

public record ProcessReceiptCommand(Stream Image, Guid UserId, string? ObjectKey = null);
