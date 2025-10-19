namespace WIB.Application.Interfaces;

public interface IImageStorage
{
    Task<string> SaveAsync(Stream image, string? contentType, CancellationToken ct);
    Task<Stream> GetAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}
