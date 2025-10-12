using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WIB.Domain;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class DatabaseSeedService
{
    private readonly WibDbContext _context;
    private readonly ILogger<DatabaseSeedService> _logger;

    public DatabaseSeedService(WibDbContext context, ILogger<DatabaseSeedService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedDefaultDataAsync()
    {
        try
        {
            await SeedDefaultUserAsync();
            await SeedDefaultStoresAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during database seeding");
            throw;
        }
    }

    private async Task SeedDefaultUserAsync()
    {
        // Check if admin user already exists
        var existingAdmin = await _context.Users
            .Where(u => u.Email == "admin@wib.local")
            .FirstOrDefaultAsync();

        if (existingAdmin != null)
        {
            _logger.LogInformation("Admin user already exists, skipping creation");
            return;
        }

        // Create default admin user
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@wib.local",
            FirstName = "Admin",
            LastName = "User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"), // Default password
            IsActive = true,
            EmailVerified = true, // For demo purposes
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(adminUser);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created default admin user: admin@wib.local / admin123");
    }

    private async Task SeedDefaultStoresAsync()
    {
        // Check if stores already exist
        if (await _context.Stores.AnyAsync())
        {
            _logger.LogInformation("Stores already exist, skipping store seeding");
            return;
        }

        // Create some default stores for demo purposes
        var stores = new[]
        {
            new Store { Id = Guid.NewGuid(), Name = "Coop Centro" },
            new Store { Id = Guid.NewGuid(), Name = "Esselunga" },
            new Store { Id = Guid.NewGuid(), Name = "Carrefour Express" },
            new Store { Id = Guid.NewGuid(), Name = "LIDL" },
            new Store { Id = Guid.NewGuid(), Name = "Conad City" }
        };

        _context.Stores.AddRange(stores);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created {StoreCount} default stores", stores.Length);
    }
}