using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WIB.Domain;
using WIB.Infrastructure.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WIB.API.Services;

public class StoreBackfillHostedService : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<StoreBackfillHostedService> _logger;
    private readonly bool _enabled;

    public StoreBackfillHostedService(IServiceProvider sp, ILogger<StoreBackfillHostedService> logger, IConfiguration cfg)
    {
        _sp = sp;
        _logger = logger;
        _enabled = string.Equals(
            cfg["Migrations:RunStoreBackfill"] ?? Environment.GetEnvironmentVariable("Migrations__RunStoreBackfill"),
            "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled) return;
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WibDbContext>();

        _logger.LogInformation("[StoreBackfill] Starting backfill of NameNormalized and dedup... ");

        var toFill = await db.Stores.Where(s => s.NameNormalized == null).ToListAsync(cancellationToken);
        foreach (var s in toFill)
        {
            s.NameNormalized = Normalize(s.Name);
        }
        if (toFill.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[StoreBackfill] Filled NameNormalized for {Count} stores", toFill.Count);
        }

        var dupGroups = await db.Stores
            .GroupBy(s => s.NameNormalized!)
            .Where(g => g.Key != null && g.Count() > 1)
            .Select(g => new { g.Key, Ids = g.Select(s => s.Id).ToList() })
            .ToListAsync(cancellationToken);

        foreach (var grp in dupGroups)
        {
            var targetId = grp.Ids.OrderBy(x => x).First();
            var others = grp.Ids.Where(id => id != targetId).ToList();

            var target = await db.Stores.FirstAsync(s => s.Id == targetId, cancellationToken);
            foreach (var otherId in others)
            {
                var current = await db.Stores.FirstAsync(s => s.Id == otherId, cancellationToken);
                await db.Receipts.Where(r => r.StoreId == current.Id)
                    .ForEachAsync(r => r.StoreId = target.Id, cancellationToken);

                if (string.IsNullOrWhiteSpace(target.Chain) && !string.IsNullOrWhiteSpace(current.Chain))
                    target.Chain = current.Chain;

                if (!string.IsNullOrWhiteSpace(current.NameNormalized))
                {
                    db.StoreAliases.Add(new StoreAlias { StoreId = target.Id, AliasNormalized = current.NameNormalized! });
                }
                db.Stores.Remove(current);
            }
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[StoreBackfill] Merged {Count} duplicates into {Target}", others.Count, targetId);
        }

        _logger.LogInformation("[StoreBackfill] Completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

