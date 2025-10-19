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
            await SeedRolesAsync();
            await SeedDefaultUsersAsync();
            await SeedDefaultStoresAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during database seeding");
            throw;
        }
    }

    private async Task SeedRolesAsync()
    {
        // Check if roles exist
        if (await _context.Roles.AnyAsync())
        {
            _logger.LogInformation("Roles already exist, skipping role seeding");
            return;
        }

        var roles = new[]
        {
            new Role { Name = "wmc", Description = "Web Management Console access" },
            new Role { Name = "device", Description = "Device upload access" }
        };

        _context.Roles.AddRange(roles);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created default roles: wmc, device");
    }

    private async Task SeedDefaultUsersAsync()
    {
        // Check if users already exist
        if (await _context.Users.AnyAsync())
        {
            _logger.LogInformation("Users already exist, skipping user seeding");
            return;
        }

        // Get roles
        var roleWmc = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "wmc");
        var roleDevice = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "device");

        if (roleWmc == null || roleDevice == null)
        {
            _logger.LogWarning("Roles not found, cannot create users");
            return;
        }

        // Create admin user with both roles
        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@wib.local",
            FirstName = "Admin",
            LastName = "User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            IsActive = true,
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create device user with only device role
        var deviceUser = new User
        {
            Username = "user",
            Email = "user@wib.local",
            FirstName = "Device",
            LastName = "User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("user"),
            IsActive = true,
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.AddRange(adminUser, deviceUser);
        await _context.SaveChangesAsync();

        // Assign roles
        var userRoles = new[]
        {
            new UserRole { UserId = adminUser.Id, RoleId = roleWmc.Id },
            new UserRole { UserId = adminUser.Id, RoleId = roleDevice.Id },
            new UserRole { UserId = deviceUser.Id, RoleId = roleDevice.Id }
        };

        _context.UserRoles.AddRange(userRoles);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created default users: admin/admin (wmc,device), user/user (device)");
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
