using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class NameMatcher : INameMatcher
{
    private readonly WibDbContext _db;
    public NameMatcher(WibDbContext db) { _db = db; }

    public async Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct)
    {
        var label = (raw ?? string.Empty).Trim();
        if (label.Length < 3) return null;
        var canon = Normalize(PreNormalize(label));

        // Build candidate set from product names and aliases
        var names = await _db.Products.AsNoTracking()
            .Select(p => p.Name)
            .ToListAsync(ct);
        var aliases = await _db.ProductAliases.AsNoTracking()
            .Select(a => a.Alias)
            .ToListAsync(ct);
        var candidates = names.Concat(aliases).Distinct().ToList();
        if (candidates.Count == 0) return null;

        string? best = null;
        double bestScore = 0;
        foreach (var c in candidates)
        {
            var score = Similarity(canon, Normalize(PreNormalize(c)));
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        // Accept only high confidence corrections and when the suggestion is close in length
        if (best != null && bestScore >= 0.82 && Math.Abs(best.Length - label.Length) <= Math.Max(3, (int)(0.33 * label.Length)))
            return best;
        return null;
    }

    public async Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct)
    {
        var name = (rawName ?? string.Empty).Trim();
        if (name.Length < 3) return null;
        var canon = Normalize(name);

        var stores = await _db.Stores.AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        (Guid id, string nm)? best = null;
        double bestScore = 0;
        foreach (var s in stores)
        {
            var score = Similarity(canon, Normalize(s.Name));
            if (score > bestScore)
            {
                bestScore = score;
                best = (s.Id, s.Name);
            }
        }

        if (best.HasValue && bestScore >= 0.82)
            return best.Value;
        return null;
    }

        private static string PreNormalize(string s)
    {
        s = s.Replace('0', 'o')
             .Replace('1', 'l')
             .Replace('5', 's')
             .Replace('€', 'e');
        s = s.Replace("rn", "m");
        s = s.Replace('à', 'a').Replace('á', 'a').Replace('â', 'a').Replace('ä', 'a')
             .Replace('è', 'e').Replace('é', 'e').Replace('ê', 'e').Replace('ë', 'e')
             .Replace('ì', 'i').Replace('í', 'i').Replace('î', 'i').Replace('ï', 'i')
             .Replace('ò', 'o').Replace('ó', 'o').Replace('ô', 'o').Replace('ö', 'o')
             .Replace('ù', 'u').Replace('ú', 'u').Replace('û', 'u').Replace('ü', 'u')
             .Replace('ç', 'c');
        return s;
    }private static readonly Regex MultiWs = new Regex("\\s+", RegexOptions.Compiled);
    private static string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        s = s.Replace("Ã ", "a").Replace("Ã¨", "e").Replace("Ã©", "e").Replace("Ã¬", "i").Replace("Ã²", "o").Replace("Ã¹", "u");
        s = Regex.Replace(s, "[^a-z0-9 ]", " ");
        s = MultiWs.Replace(s, " ").Trim();
        return s;
    }

    // Simple normalized Levenshtein similarity in [0,1]
    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;
        int dist = Levenshtein(a, b);
        int maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)dist / maxLen;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }
        return d[n, m];
    }
}



