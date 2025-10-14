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
            LastName = "Admin",
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

        // Create some default stores 
        var stores = new[]
        {
            new Store { Id = Guid.NewGuid(), Name = "Coop" },
            new Store { Id = Guid.NewGuid(), Name = "Esselunga" },
            new Store { Id = Guid.NewGuid(), Name = "Carrefour Express" },
            new Store { Id = Guid.NewGuid(), Name = "Carrefour" },
            new Store { Id = Guid.NewGuid(), Name = "Conad" },
            new Store { Id = Guid.NewGuid(), Name = "Bennet" },
            new Store { Id = Guid.NewGuid(), Name = "Iper" },
            new Store { Id = Guid.NewGuid(), Name = "Dimar" },
            new Store { Id = Guid.NewGuid(), Name = "Selex" },
            new Store { Id = Guid.NewGuid(), Name = "Mercatò" },
            new Store { Id = Guid.NewGuid(), Name = "Eurospin" },
            new Store { Id = Guid.NewGuid(), Name = "Famila / Maxi Di" },
            new Store { Id = Guid.NewGuid(), Name = "Lidl" },
            new Store { Id = Guid.NewGuid(), Name = "Galassia" },
            new Store { Id = Guid.NewGuid(), Name = "Gulliver" },
            new Store { Id = Guid.NewGuid(), Name = "Ekom" },
            new Store { Id = Guid.NewGuid(), Name = "DPiù" },
            new Store { Id = Guid.NewGuid(), Name = "Pam" },
            new Store { Id = Guid.NewGuid(), Name = "Pam Local" },
            new Store { Id = Guid.NewGuid(), Name = "iN's Mercato" },
            new Store { Id = Guid.NewGuid(), Name = "Il Gigante" },
            new Store { Id = Guid.NewGuid(), Name = "Basko" },
            new Store { Id = Guid.NewGuid(), Name = "Despar" },
            new Store { Id = Guid.NewGuid(), Name = "Unes*" },
            new Store { Id = Guid.NewGuid(), Name = "PENNY Market" }
        };

        _context.Stores.AddRange(stores);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created {StoreCount} default stores", stores.Length);
    }
}