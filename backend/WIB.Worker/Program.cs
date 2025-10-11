using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Infrastructure.Clients;
using WIB.Worker;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Storage;
using WIB.Infrastructure.Queue;
using WIB.Infrastructure.Services;

var builder = Host.CreateApplicationBuilder(args);
// Endpoints (support both section keys and env fallbacks)
var ocrEndpoint = builder.Configuration["Ocr:Endpoint"]
                   ?? Environment.GetEnvironmentVariable("Ocr__Endpoint")
                   ?? "http://localhost:8081";
builder.Services.AddHttpClient<IOcrClient, OcrClient>(client => client.BaseAddress = new Uri(ocrEndpoint));
var kieEndpoint = builder.Configuration["Kie:Endpoint"]
                   ?? Environment.GetEnvironmentVariable("Kie__Endpoint")
                   ?? ocrEndpoint;
builder.Services.AddHttpClient<IKieClient, KieClient>(client => client.BaseAddress = new Uri(kieEndpoint));
var mlEndpoint = builder.Configuration["Ml:Endpoint"]
                  ?? Environment.GetEnvironmentVariable("Ml__Endpoint")
                  ?? "http://localhost:8082";
builder.Services.AddHttpClient<IProductClassifier, ProductClassifier>(client => client.BaseAddress = new Uri(mlEndpoint));
builder.Services.AddScoped<IReceiptStorage, ReceiptStorage>();
builder.Services.AddScoped<INameMatcher, NameMatcher>();
builder.Services.AddScoped<ProcessReceiptCommandHandler>();
builder.Services.AddScoped<ReceiptProcessor>();
builder.Services.AddHostedService<Worker>();

var conn = builder.Configuration.GetConnectionString("Default")
           ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
           ?? "Host=localhost;Database=wib;Username=wib;Password=wib";
builder.Services.AddDbContext<WibDbContext>(options => options.UseNpgsql(conn));

// MinIO options and storage
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("Minio"));
builder.Services.AddSingleton<IImageStorage, MinioImageStorage>();

// Redis queue (support section key and env fallback)
var redisConn = builder.Configuration["Redis:Connection"]
                 ?? builder.Configuration["Redis__Connection"]
                 ?? Environment.GetEnvironmentVariable("Redis__Connection")
                 // In Docker, prefer the service name by default
                 ?? "redis:6379";
builder.Services.AddSingleton<IReceiptQueue>(_ => new RedisReceiptQueue(redisConn));

var host = builder.Build();

// Auto-migrate DB on startup (dev/local) con retry per dipendenze lente (DB)
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WibDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    const int maxAttempts = 30;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            db.Database.Migrate();
            try
            {
                db.Database.ExecuteSqlRaw(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'WeightKg'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""WeightKg"" numeric(10,3) NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'PricePerKg'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""PricePerKg"" numeric(10,3) NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'PriceHistories' AND column_name = 'PricePerKg'
    ) THEN
        ALTER TABLE ""PriceHistories"" ADD COLUMN ""PricePerKg"" numeric(10,3) NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'SortIndex'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""SortIndex"" integer NOT NULL DEFAULT 0;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'OcrX'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""OcrX"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'OcrY'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""OcrY"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'OcrW'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""OcrW"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ReceiptLines' AND column_name = 'OcrH'
    ) THEN
        ALTER TABLE ""ReceiptLines"" ADD COLUMN ""OcrH"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Receipts' AND column_name = 'OcrStoreX'
    ) THEN
        ALTER TABLE ""Receipts"" ADD COLUMN ""OcrStoreX"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Receipts' AND column_name = 'OcrStoreY'
    ) THEN
        ALTER TABLE ""Receipts"" ADD COLUMN ""OcrStoreY"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Receipts' AND column_name = 'OcrStoreW'
    ) THEN
        ALTER TABLE ""Receipts"" ADD COLUMN ""OcrStoreW"" integer NULL;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Receipts' AND column_name = 'OcrStoreH'
    ) THEN
        ALTER TABLE ""Receipts"" ADD COLUMN ""OcrStoreH"" integer NULL;
    END IF;
END $$;");
            }
            catch { /* best-effort */ }
            logger.LogInformation("Database migration successful");
            break;
        }
        catch (Exception ex)
        {
            if (attempt == maxAttempts)
            {
                logger.LogError(ex, "Database migration failed after {Attempts} attempts", maxAttempts);
                throw;
            }
            logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}). Retrying...", attempt, maxAttempts);
            await Task.Delay(2000);
        }
    }
}
host.Run();
