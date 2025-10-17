using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Infrastructure.Clients;
using WIB.Worker;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Storage;
using WIB.Infrastructure.Queue;
using WIB.Infrastructure.Services;
using WIB.Infrastructure.Logging;

var builder = Host.CreateApplicationBuilder(args);
// Add memory cache for EnhancedNameMatcher
builder.Services.AddMemoryCache();
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
builder.Services.AddScoped<INameMatcher, EnhancedNameMatcher>();
builder.Services.AddScoped<IProductMatcher, WIB.Infrastructure.Services.ProductMatcher>();
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

// Redis logger for centralized monitoring
var logStreamKey = builder.Configuration["Logging:StreamKey"]
                    ?? Environment.GetEnvironmentVariable("Logging__StreamKey")
                    ?? "app_logs";
var logLevel = Enum.TryParse<LogSeverity>(
    builder.Configuration["Logging:MinLevel"] ?? Environment.GetEnvironmentVariable("Logging__MinLevel"),
    ignoreCase: true,
    out var parsedLevel) ? parsedLevel : LogSeverity.Info;
builder.Services.AddSingleton<IRedisLogger>(sp =>
    new RedisLogger(redisConn, "worker", logStreamKey, maxStreamLength: 10000, minLogLevel: logLevel));

var host = builder.Build();

// Verify DB connection is available (API handles migrations)
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WibDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    
    const int maxAttempts = 10;
    
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            logger.LogInformation("Verifying database connection (attempt {Attempt}/{MaxAttempts})", attempt, maxAttempts);
            await db.Database.CanConnectAsync();
            logger.LogInformation("Database connection verified successfully");
            break;
        }
        catch (Exception ex)
        {
            if (attempt == maxAttempts)
            {
                logger.LogError(ex, "Database connection verification failed after {MaxAttempts} attempts", maxAttempts);
                throw;
            }
            
            logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{MaxAttempts}). Retrying in 2 seconds...", attempt, maxAttempts);
            await Task.Delay(2000);
        }
    }
}
host.Run();
