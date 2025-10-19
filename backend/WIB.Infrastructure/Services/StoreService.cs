using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WIB.Application.Interfaces;
using WIB.Domain;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class StoreService : IStoreService
{
    private readonly WibDbContext _db;

    public StoreService(WibDbContext db)
    {
        _db = db;
    }

    public async Task<Store> RenameOrMergeAsync(Guid currentStoreId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName is required", nameof(newName));

        var newNorm = Normalize(newName);

        var current = await _db.Stores.FirstAsync(s => s.Id == currentStoreId, ct);
        if (string.Equals(current.NameNormalized ?? string.Empty, newNorm, StringComparison.Ordinal))
        {
            return current; // no-op
        }

        var target = await _db.Stores.FirstOrDefaultAsync(s => s.NameNormalized == newNorm, ct);

        if (target != null && target.Id != current.Id)
        {
            // MERGE: reassign receipts
            var receipts = _db.Receipts.Where(r => r.StoreId == current.Id);
            await receipts.ForEachAsync(r => r.StoreId = target.Id, ct);

            // Metadata merge: fill missing chain
            if (string.IsNullOrWhiteSpace(target.Chain) && !string.IsNullOrWhiteSpace(current.Chain))
            {
                target.Chain = current.Chain;
            }

            // Create aliases for traceability
            if (!string.IsNullOrWhiteSpace(current.NameNormalized))
            {
                _db.StoreAliases.Add(new StoreAlias { StoreId = target.Id, AliasNormalized = current.NameNormalized! });
            }
            _db.StoreAliases.Add(new StoreAlias { StoreId = target.Id, AliasNormalized = newNorm });

            _db.Stores.Remove(current); // hard delete; alternatively soft-delete pattern

            await _db.SaveChangesAsync(ct);
            return target;
        }
        else
        {
            // RENAME IN-PLACE
            if (!string.IsNullOrWhiteSpace(current.NameNormalized))
            {
                _db.StoreAliases.Add(new StoreAlias { StoreId = current.Id, AliasNormalized = current.NameNormalized! });
            }
            current.Name = ToTitleCase(newName.Trim());
            current.NameNormalized = newNorm;
            await _db.SaveChangesAsync(ct);
            return current;
        }
    }

    private static string ToTitleCase(string input)
        => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLowerInvariant());

    // Simple, deterministic normalization: lowercase, remove diacritics, keep alnum+spaces, collapse spaces
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
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}

