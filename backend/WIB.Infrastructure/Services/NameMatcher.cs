using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class NameMatcher : INameMatcher
{
    private readonly WibDbContext _db;
    private readonly ILogger<NameMatcher> _logger;

    public NameMatcher(WibDbContext db, ILogger<NameMatcher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Simple implementation - just return cleaned up version
        return raw.Trim();
    }

    public async Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct)
    {
        return await MatchStoreAsync(rawName, null, null, null, ct);
    }

    public async Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, string? address, string? city, string? vatNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return null;

        var stores = await _db.Set<WIB.Domain.Store>()
            .Include(s => s.Locations)
            .ToListAsync(ct);
        
        // Simple exact match first
        var exactMatch = stores.FirstOrDefault(s => 
            string.Equals(s.Name, rawName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
            return (exactMatch.Id, exactMatch.Name);

        // Simple contains match
        var containsMatch = stores.FirstOrDefault(s => 
            s.Name.Contains(rawName, StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
        if (containsMatch != null)
            return (containsMatch.Id, containsMatch.Name);

        // Levenshtein distance matching
        var bestMatch = stores
            .Select(s => new { Store = s, Distance = CalculateLevenshteinDistance(rawName.ToLowerInvariant(), s.Name.ToLowerInvariant()) })
            .Where(x => x.Distance <= 3) // Max distance of 3
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        return bestMatch != null ? (bestMatch.Store.Id, bestMatch.Store.Name) : null;
    }

    // Compute the Levenshtein distance between two strings
    private static int CalculateLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0)
            return m;

        if (m == 0)
            return n;

        for (int i = 0; i <= n; d[i, 0] = i++)
        {
        }

        for (int j = 0; j <= m; d[0, j] = j++)
        {
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
