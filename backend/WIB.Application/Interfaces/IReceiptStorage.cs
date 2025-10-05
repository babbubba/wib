using WIB.Domain;

namespace WIB.Application.Interfaces;

public interface IReceiptStorage
{
    Task<Receipt> SaveAsync(Receipt receipt, CancellationToken ct);
}
