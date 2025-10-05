namespace WIB.Application.Interfaces;

public interface IOcrClient
{
    Task<string> ExtractAsync(Stream image, CancellationToken ct);
}
