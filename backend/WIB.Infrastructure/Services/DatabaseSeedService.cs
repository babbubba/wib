using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
            await NormalizeAndDedupStoresAsync();
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

        // Normalize names for newly seeded stores
        foreach (var s in stores)
        {
            s.NameNormalized = Normalize(s.Name);
        }

        _context.Stores.AddRange(stores);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created {StoreCount} default stores", stores.Length);
    }

    private async Task NormalizeAndDedupStoresAsync()
    {
        // Fill NameNormalized for any existing store missing it or blank
        var toFill = await _context.Stores.Where(s => s.NameNormalized == null || s.NameNormalized == "").ToListAsync();
        foreach (var s in toFill)
        {
            s.NameNormalized = Normalize(s.Name);
        }
        if (toFill.Count > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("[StoreSeed] Filled NameNormalized for {Count} stores", toFill.Count);
        }

        // Deduplicate by NameNormalized
        var dupGroups = await _context.Stores
            .GroupBy(s => s.NameNormalized!)
            .Where(g => g.Key != null && g.Count() > 1)
            .Select(g => new { g.Key, Ids = g.Select(s => s.Id).ToList() })
            .ToListAsync();

        foreach (var grp in dupGroups)
        {
            var targetId = grp.Ids.OrderBy(x => x).First();
            var others = grp.Ids.Where(id => id != targetId).ToList();
            var target = await _context.Stores.FirstAsync(s => s.Id == targetId);
            foreach (var otherId in others)
            {
                var current = await _context.Stores.FirstAsync(s => s.Id == otherId);
                await _context.Receipts.Where(r => r.StoreId == current.Id).ForEachAsync(r => r.StoreId = target.Id);

                if (string.IsNullOrWhiteSpace(target.Chain) && !string.IsNullOrWhiteSpace(current.Chain))
                    target.Chain = current.Chain;

                if (!string.IsNullOrWhiteSpace(current.NameNormalized))
                    _context.StoreAliases.Add(new StoreAlias { StoreId = target.Id, AliasNormalized = current.NameNormalized! });

                _context.Stores.Remove(current);
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("[StoreSeed] Merged {Count} duplicates into {Target}", others.Count, targetId);
        }
    }

    private static string Normalize(string input)
    {
        input = (input ?? string.Empty).Trim().ToLowerInvariant();
        input = RemoveDiacritics(input);
        input = Regex.Replace(input, "[^a-z0-9 ]", " ");
        input = Regex.Replace(input, "\\s+", " ").Trim();
        return input;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();
        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
