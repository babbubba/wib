using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using WIB.Application.Interfaces;

namespace WIB.Infrastructure.Storage;

public class MinioOptions
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public bool WithSSL { get; set; } = false;
    public string Bucket { get; set; } = "receipts";
}

public class MinioImageStorage : IImageStorage
{
    private readonly IMinioClient _client;
    private readonly MinioOptions _options;

    public MinioImageStorage(IOptions<MinioOptions> options)
    {
        _options = options.Value;
        _client = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.WithSSL)
            .Build();
    }

    public async Task<string> SaveAsync(Stream image, string? contentType, CancellationToken ct)
    {
        // Ensure bucket exists
        var bucketExists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.Bucket), ct);
        if (!bucketExists)
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.Bucket), ct);

        var objectName = $"{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}.jpg";
        image.Position = 0;
        var putArgs = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectName)
            .WithStreamData(image)
            .WithObjectSize(image.Length)
            .WithContentType(contentType ?? "image/jpeg");
        await _client.PutObjectAsync(putArgs, ct);
        return objectName;
    }

    public async Task<Stream> GetAsync(string objectKey, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithCallbackStream(stream => stream.CopyTo(ms));
        await _client.GetObjectAsync(args, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey);
        await _client.RemoveObjectAsync(args, ct);
    }
}
