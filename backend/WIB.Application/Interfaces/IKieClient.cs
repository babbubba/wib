using WIB.Application.Contracts.Kie;

namespace WIB.Application.Interfaces;

public interface IKieClient
{
    Task<ReceiptDraft> ExtractFieldsAsync(string ocrResult, CancellationToken ct);
}
