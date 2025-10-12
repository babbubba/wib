using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Infrastructure.Clients;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Storage;
using WIB.Infrastructure.Queue;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using WIB.API.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
builder.Services.AddScoped<INameMatcher, WIB.Infrastructure.Services.NameMatcher>();
builder.Services.AddScoped<ProcessReceiptCommandHandler>();

var conn = builder.Configuration.GetConnectionString("Default") ??
           Environment.GetEnvironmentVariable("ConnectionStrings__Default") ??
           "Host=localhost;Database=wib;Username=wib;Password=wib";
builder.Services.AddDbContext<WibDbContext>(options => options.UseNpgsql(conn));

// MinIO options and storage
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("Minio"));
builder.Services.AddSingleton<IImageStorage, MinioImageStorage>();

// Redis queue
var redisConn = builder.Configuration["Redis:Connection"]
                ?? builder.Configuration["Redis__Connection"]
                ?? Environment.GetEnvironmentVariable("Redis__Connection")
                ?? "redis:6379";
builder.Services.AddSingleton<IReceiptQueue>(_ => new RedisReceiptQueue(redisConn));

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WibDbContext>(name: "db");

// Form limits (max 10 MB upload)
builder.Services.Configure<FormOptions>(o =>
{
    // Consenti upload fino a 20 MB
    o.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

// Rate limiting (fixed window, 30 req / 10s per IP)
builder.Services.AddRateLimiter(_ =>
{
    _.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    _.RejectionStatusCode = 429;
});

// Auth (JWT)
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
var authOpts = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
// Ensure defaults are available to both JWT setup and injected options
if (authOpts.Users == null || authOpts.Users.Count == 0)
{
    authOpts.Users = new List<AuthUser> {
        new() { Username = "admin", Password = "admin", Role = "wmc" },
        new() { Username = "device", Password = "device", Role = "devices" }
    };
}
if (string.IsNullOrWhiteSpace(authOpts.Key)) authOpts.Key = new AuthOptions().Key;
if (string.IsNullOrWhiteSpace(authOpts.Issuer)) authOpts.Issuer = new AuthOptions().Issuer;
if (string.IsNullOrWhiteSpace(authOpts.Audience)) authOpts.Audience = new AuthOptions().Audience;
builder.Services.PostConfigure<AuthOptions>(opts =>
{
    if (opts.Users == null || opts.Users.Count == 0)
    {
        opts.Users = new List<AuthUser> {
            new() { Username = "admin", Password = "admin", Role = "wmc" },
            new() { Username = "device", Password = "device", Role = "devices" }
        };
    }
    if (string.IsNullOrWhiteSpace(opts.Key)) opts.Key = authOpts.Key;
    if (string.IsNullOrWhiteSpace(opts.Issuer)) opts.Issuer = authOpts.Issuer;
    if (string.IsNullOrWhiteSpace(opts.Audience)) opts.Audience = authOpts.Audience;
});
var keyBytes = Encoding.UTF8.GetBytes(authOpts.Key);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer = true,
            ValidIssuer = authOpts.Issuer,
            ValidateAudience = true,
            ValidAudience = authOpts.Audience,
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("wmc", p => p.RequireRole("wmc"));
    options.AddPolicy("devices", p => p.RequireRole("devices"));
});
var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment()
                     || string.Equals(
                         builder.Configuration["Swagger:Enabled"]
                         ?? Environment.GetEnvironmentVariable("SWAGGER__ENABLED"),
                         "true",
                         StringComparison.OrdinalIgnoreCase);
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Respect reverse proxy headers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Auto-migrate DB on startup (dev/local)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WibDbContext>();
    var attempts = 0;
    var maxAttempts = 10;
    while (true)
    {
        try
        {
            db.Database.Migrate();
            // Ensure new columns exist when migrations assembly is out of sync
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
            break;
        }
        catch
        {
            if (++attempts >= maxAttempts) throw;
            System.Threading.Thread.Sleep(2000);
        }
    }
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapControllers();

app.Run();



