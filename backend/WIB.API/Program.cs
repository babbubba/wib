using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using WIB.Application.Interfaces;
using WIB.Application.Receipts;
using WIB.Infrastructure.Clients;
using WIB.Infrastructure.Data;
using WIB.Infrastructure.Storage;
using WIB.Infrastructure.Queue;
using WIB.Infrastructure.Services;
using WIB.Infrastructure.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add memory cache for EnhancedNameMatcher
builder.Services.AddMemoryCache();

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
builder.Services.AddScoped<INameMatcher, WIB.Infrastructure.Services.EnhancedNameMatcher>();
builder.Services.AddScoped<IProductMatcher, WIB.Infrastructure.Services.ProductMatcher>();
builder.Services.AddScoped<ProcessReceiptCommandHandler>();

// Authentication services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<DatabaseSeedService>();

// Database configuration
var connectionString = builder.Configuration.GetConnectionString("Default")
                      ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                      ?? "Host=localhost;Database=wib;Username=wib;Password=wib";

builder.Services.AddDbContext<WibDbContext>(options => options.UseNpgsql(connectionString));

// MinIO options and storage
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("Minio"));
builder.Services.AddSingleton<IImageStorage, MinioImageStorage>();

// Redis queue
var redisConn = builder.Configuration["Redis:Connection"]
                ?? builder.Configuration["Redis__Connection"]
                ?? Environment.GetEnvironmentVariable("Redis__Connection")
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
    new RedisLogger(redisConn, "api", logStreamKey, maxStreamLength: 10000, minLogLevel: logLevel));

// Redis log consumer for monitoring endpoints
builder.Services.AddSingleton(sp => new RedisLogConsumer(redisConn, logStreamKey));

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

// JWT Authentication Configuration
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"] ?? 
    Environment.GetEnvironmentVariable("JWT_SECRET") ?? 
    "your-super-secret-jwt-signing-key-that-should-be-at-least-32-characters-long";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "WIB.API",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "WIB.Client",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    var path = context.HttpContext.Request.Path;
                    if (path.StartsWithSegments("/monitoring/logs/stream") &&
                        context.Request.Cookies.TryGetValue("wib_access_token", out var cookieToken) &&
                        !string.IsNullOrWhiteSpace(cookieToken))
                    {
                        context.Token = cookieToken;
                    }
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
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

// Auto-migrate DB on startup with retry logic
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<WibDbContext>();
    
    var attempts = 0;
    var maxAttempts = 10;
    
    while (attempts < maxAttempts)
    {
        try
        {
            logger.LogInformation("Attempting database migration (attempt {Attempt}/{MaxAttempts})", attempts + 1, maxAttempts);
            db.Database.Migrate();
            logger.LogInformation("Database migration successful");
            
            // Seed default data
            var seedService = scope.ServiceProvider.GetRequiredService<DatabaseSeedService>();
            await seedService.SeedDefaultDataAsync();
            logger.LogInformation("Database seeding completed");
            
            break;
        }
        catch (Exception ex)
        {
            attempts++;
            if (attempts >= maxAttempts)
            {
                logger.LogError(ex, "Database migration failed after {MaxAttempts} attempts", maxAttempts);
                throw;
            }
            
            logger.LogWarning(ex, "Database migration attempt {Attempt} failed, retrying in 2 seconds", attempts);
            await Task.Delay(2000);
        }
    }
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapControllers();

app.Run();



