namespace WIB.Application.Interfaces;

using WIB.Domain;

public interface IStoreService
{
    Task<Store> RenameOrMergeAsync(Guid currentStoreId, string newName, CancellationToken ct = default);
}

