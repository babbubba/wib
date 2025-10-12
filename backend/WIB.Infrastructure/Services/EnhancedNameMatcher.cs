using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Services;

public class EnhancedNameMatcher : INameMatcher
{
    private readonly WibDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EnhancedNameMatcher> _logger;

    // Cache keys
    private const string CACHE_PRODUCTS = "products_names_and_aliases";
    private const string CACHE_STORES = "stores_data";
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);

    // Brand normalization dictionary for ALL commercial activities
    private static readonly Dictionary<string, string> BrandNormalizations = new()
    {
        // === SUPERMARKET & FOOD CHAINS ===
        // Cooperative chains
        {"coop", "coop"}, {"co-op", "coop"}, {"cooperativa", "coop"}, 
        {"conad", "conad"}, {"con.ad", "conad"}, {"conad city", "conad"},
        
        // Major supermarkets
        {"esselunga", "esselunga"}, {"esse lunga", "esselunga"},
        {"carrefour", "carrefour"}, {"carrefur", "carrefour"}, {"carrefour express", "carrefour"},
        {"lidl", "lidl"}, {"lidel", "lidl"},
        {"eurospin", "eurospin"}, {"euro spin", "eurospin"},
        {"pam", "pam"}, {"panorama", "pam"}, {"pam local", "pam"},
        {"auchan", "auchan"}, {"simply", "auchan"},
        {"ipercoop", "ipercoop"}, {"iper coop", "ipercoop"},
        {"sigma", "sigma"}, {"tigre", "tigre"}, {"tigre amico", "tigre"},
        {"bennet", "bennet"}, {"iperbenne", "bennet"},
        {"despar", "despar"}, {"interspar", "despar"}, {"eurospar", "despar"},
        {"il gigante", "gigante"}, {"gigante", "gigante"},
        
        // Discount chains
        {"md", "md"}, {"m.d.", "md"}, {"m d", "md"}, {"md discount", "md"},
        {"in's", "ins"}, {"ins mercato", "ins"}, {"ins", "ins"},
        {"penny", "penny"}, {"penny market", "penny"},
        {"tuodi", "tuodi"}, {"tuodì", "tuodi"},
        {"todis", "todis"}, {"ard", "ard"}, {"aldi", "aldi"},
        
        // === RESTAURANTS & FOOD SERVICE ===
        // Fast food
        {"mcdonald's", "mcdonalds"}, {"mcdonalds", "mcdonalds"}, {"mc donald's", "mcdonalds"},
        {"burger king", "burger king"}, {"kfc", "kfc"}, {"kentucky", "kfc"},
        {"domino's", "dominos"}, {"dominos", "dominos"},
        {"pizza hut", "pizza hut"}, {"pizzahut", "pizza hut"},
        {"subway", "subway"}, {"autogrill", "autogrill"},
        
        // Coffee & bars
        {"starbucks", "starbucks"}, {"costa coffee", "costa"},
        {"bar", "bar"}, {"caffè", "bar"}, {"caffe", "bar"},
        {"pasticceria", "pasticceria"}, {"gelateria", "gelateria"},
        
        // === PHARMACIES & HEALTH ===
        {"farmacia", "farmacia"}, {"farm.", "farmacia"}, {"farmacia comunale", "farmacia"},
        {"lloyds", "lloyds"}, {"lloyd's", "lloyds"},
        {"boots", "boots"}, {"cisalfa", "cisalfa"},
        
        // === GAS STATIONS ===
        {"eni", "eni"}, {"agip", "eni"}, {"eni station", "eni"},
        {"shell", "shell"}, {"shell select", "shell"},
        {"q8", "q8"}, {"kuwait", "q8"},
        {"esso", "esso"}, {"exxon", "esso"},
        {"ip", "ip"}, {"italiana petroli", "ip"},
        {"tamoil", "tamoil"}, {"total", "total"}, {"totalerg", "total"},
        {"api", "api"}, {"repsol", "repsol"},
        
        // === RETAIL & SPECIALTY STORES ===
        // Electronics & tech
        {"mediaworld", "mediaworld"}, {"media world", "mediaworld"},
        {"unieuro", "unieuro"}, {"euronics", "euronics"},
        {"expert", "expert"}, {"trony", "trony"},
        
        // Fashion & clothing
        {"zara", "zara"}, {"h&m", "hm"}, {"hm", "hm"}, {"h & m", "hm"},
        {"uniqlo", "uniqlo"}, {"bershka", "bershka"},
        {"pull&bear", "pull and bear"}, {"stradivarius", "stradivarius"},
        {"ovs", "ovs"}, {"coin", "coin"},
        
        // Home & garden
        {"ikea", "ikea"}, {"leroy merlin", "leroy merlin"},
        {"bricocenter", "bricocenter"}, {"brico", "bricocenter"},
        {"obi", "obi"}, {"castorama", "castorama"},
        
        // === TABACCHI & SERVICES ===
        {"tabacchi", "tabacchi"}, {"tabaccheria", "tabacchi"}, {"ricevitoria", "tabacchi"},
        {"sisal", "sisal"}, {"lottomatica", "lottomatica"},
        
        // === DEPARTMENT STORES ===
        {"coop.fi", "coop"}, {"ipercoop", "ipercoop"},
        {"centro commerciale", "centro commerciale"}, {"mall", "centro commerciale"},
        
        // === FOOD SPECIALTIES ===
        {"pizzeria", "pizzeria"}, {"ristorante", "ristorante"},
        {"trattoria", "trattoria"}, {"osteria", "osteria"},
        {"alimentari", "alimentari"}, {"salumeria", "alimentari"},
        {"panetteria", "panetteria"}, {"panificio", "panetteria"},
        {"macelleria", "macelleria"}, {"pescheria", "pescheria"},
        {"fruttivendolo", "fruttivendolo"}, {"ortofrutta", "fruttivendolo"}
    };

    public EnhancedNameMatcher(WibDbContext db, IMemoryCache cache, ILogger<EnhancedNameMatcher> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> CorrectProductLabelAsync(string raw, CancellationToken ct)
    {
        var label = (raw ?? string.Empty).Trim();
        if (label.Length < 3) return null;
        var canon = Normalize(PreNormalize(label));

        // Get candidates from cache or DB
        var candidates = await GetProductCandidatesAsync(ct);
        if (candidates.Count == 0) return null;

        var (bestMatch, bestScore) = FindBestMatch(canon, candidates, 0.82);
        
        // Accept only high confidence corrections with reasonable length difference
        if (bestMatch != null && Math.Abs(bestMatch.Length - label.Length) <= Math.Max(3, (int)(0.33 * label.Length)))
        {
            _logger.LogDebug("Product label corrected: '{RawLabel}' -> '{CorrectedLabel}' (score: {Score:F3})", 
                label, bestMatch, bestScore);
            return bestMatch;
        }

        return null;
    }

    public async Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, CancellationToken ct)
    {
        return await MatchStoreAsync(rawName, null, null, null, ct);
    }

    // Enhanced version with location data for better matching
    public async Task<(Guid storeId, string name)?> MatchStoreAsync(string rawName, string? address, string? city, string? vatNumber, CancellationToken ct)
    {
        var name = (rawName ?? string.Empty).Trim();
        if (name.Length < 3) return null;
        
        // Apply brand normalization first
        var normalizedName = NormalizeBrandName(name);
        var canon = Normalize(PreNormalize(normalizedName));

        // Get stores from cache or DB
        var stores = await GetStoresAsync(ct);
        if (stores.Count == 0) return null;

        (Guid id, string nm, double totalScore)? bestMatch = null;
        double bestScore = 0;

        foreach (var store in stores)
        {
            var storeNormalized = NormalizeBrandName(store.name);
            var storeCanon = Normalize(PreNormalize(storeNormalized));
            
            // Base name similarity (70% weight)
            var nameScore = CombinedSimilarity(canon, storeCanon) * 0.7;
            
            // Location matching bonus (30% weight)
            var locationScore = CalculateLocationScore(address, city, vatNumber, store.location) * 0.3;
            
            // Chain matching bonus
            var chainBonus = 0.0;
            if (!string.IsNullOrEmpty(store.chain))
            {
                var chainNormalized = NormalizeBrandName(store.chain);
                var chainCanon = Normalize(PreNormalize(chainNormalized));
                var chainSim = CombinedSimilarity(canon, chainCanon);
                if (chainSim > 0.8) chainBonus = 0.1; // 10% bonus for chain match
            }
            
            var totalScore = nameScore + locationScore + chainBonus;
            
            if (totalScore > bestScore)
            {
                bestScore = totalScore;
                bestMatch = (store.id, store.name, totalScore);
            }
        }

        var threshold = HasLocationData(address, city, vatNumber) ? 0.65 : 0.78; // Lower threshold when we have location data
        
        if (bestMatch.HasValue && bestScore >= threshold)
        {
            _logger.LogDebug("Store matched: '{RawName}' -> '{StoreName}' (score: {Score:F3}, threshold: {Threshold:F2})", 
                rawName, bestMatch.Value.nm, bestScore, threshold);
            return (bestMatch.Value.id, bestMatch.Value.nm);
        }

        _logger.LogDebug("No store match found for: '{RawName}' (best score: {Score:F3}, threshold: {Threshold:F2})", rawName, bestScore, threshold);
        return null;
    }

    private static bool HasLocationData(string? address, string? city, string? vatNumber)
    {
        return !string.IsNullOrEmpty(address) || !string.IsNullOrEmpty(city) || !string.IsNullOrEmpty(vatNumber);
    }

    private static double CalculateLocationScore(string? inputAddress, string? inputCity, string? inputVatNumber, StoreLocationInfo? storeLocation)
    {
        if (storeLocation == null) return 0.0;
        
        var scores = new List<double>();
        
        // VAT Number exact match (highest priority - 40% of location score)
        if (!string.IsNullOrEmpty(inputVatNumber) && !string.IsNullOrEmpty(storeLocation.VatNumber))
        {
            var vatMatch = string.Equals(CleanVatNumber(inputVatNumber), CleanVatNumber(storeLocation.VatNumber), StringComparison.OrdinalIgnoreCase);
            scores.Add(vatMatch ? 1.0 * 0.4 : 0.0);
        }
        
        // City match (30% of location score)
        if (!string.IsNullOrEmpty(inputCity) && !string.IsNullOrEmpty(storeLocation.City))
        {
            var cityCanon1 = Normalize(PreNormalize(inputCity));
            var cityCanon2 = Normalize(PreNormalize(storeLocation.City));
            var citySim = LevenshteinSimilarity(cityCanon1, cityCanon2);
            if (citySim > 0.8) scores.Add(citySim * 0.3);
        }
        
        // Address similarity (30% of location score)
        if (!string.IsNullOrEmpty(inputAddress) && !string.IsNullOrEmpty(storeLocation.Address))
        {
            var addrCanon1 = Normalize(PreNormalize(inputAddress));
            var addrCanon2 = Normalize(PreNormalize(storeLocation.Address));
            var addrSim = LevenshteinSimilarity(addrCanon1, addrCanon2);
            if (addrSim > 0.6) scores.Add(addrSim * 0.3);
        }
        
        return scores.Count > 0 ? scores.Sum() : 0.0;
    }

    private static string CleanVatNumber(string vatNumber)
    {
        // Remove common VAT prefixes and formatting
        return Regex.Replace(vatNumber.ToUpperInvariant(), @"[^A-Z0-9]", "");
    }

    private async Task<List<string>> GetProductCandidatesAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(CACHE_PRODUCTS, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiry;
            
            var names = await _db.Products.AsNoTracking()
                .Select(p => p.Name)
                .ToListAsync(ct);
            
            var aliases = await _db.ProductAliases.AsNoTracking()
                .Select(a => a.Alias)
                .ToListAsync(ct);
            
            var candidates = names.Concat(aliases).Distinct().ToList();
            _logger.LogDebug("Cached {Count} product candidates", candidates.Count);
            return candidates;
        }) ?? new List<string>();
    }

    private async Task<List<(Guid id, string name, string? chain, StoreLocationInfo? location)>> GetStoresAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(CACHE_STORES, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiry;
            
            var stores = await _db.Stores.AsNoTracking()
                .Include(s => s.Locations)
                .Select(s => new { 
                    s.Id, 
                    s.Name, 
                    s.Chain,
                    Location = s.Locations.FirstOrDefault()
                })
                .ToListAsync(ct);
            
            var result = stores.Select(s => (
                s.Id, 
                s.Name, 
                s.Chain,
                s.Location != null ? new StoreLocationInfo
                {
                    Address = s.Location.Address,
                    City = s.Location.City,
                    PostalCode = s.Location.PostalCode,
                    VatNumber = s.Location.VatNumber
                } : null
            )).ToList();
            
            _logger.LogDebug("Cached {Count} stores with location data", result.Count);
            return result;
        }) ?? new List<(Guid, string, string?, StoreLocationInfo?)>();
    }

    // DTO for store location caching
    private class StoreLocationInfo
    {
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? VatNumber { get; set; }
    }

    private static string NormalizeBrandName(string name)
    {
        var normalized = name.ToLowerInvariant().Trim();
        
        // Remove common suffixes/prefixes
        normalized = Regex.Replace(normalized, @"\b(supermercato|ipermercato|centro|market|punto|vendita|il|la|lo|del|della)\b", "", RegexOptions.IgnoreCase);
        
        // Check brand normalizations
        foreach (var (key, value) in BrandNormalizations)
        {
            if (normalized.Contains(key))
            {
                return value;
            }
        }
        
        return normalized.Trim();
    }

    private static (string? match, double score) FindBestMatch(string target, List<string> candidates, double threshold)
    {
        string? bestMatch = null;
        double bestScore = 0;

        foreach (var candidate in candidates)
        {
            var candidateNorm = Normalize(PreNormalize(candidate));
            var score = CombinedSimilarity(target, candidateNorm);
            
            if (score > bestScore && score >= threshold)
            {
                bestScore = score;
                bestMatch = candidate;
            }
        }

        return (bestMatch, bestScore);
    }

    private static double CombinedSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
        
        // Weight combination of different algorithms
        var levenshtein = LevenshteinSimilarity(a, b);
        var jaroWinkler = JaroWinklerSimilarity(a, b);
        var jaccard = JaccardSimilarity(a, b);
        
        // Weighted average: Jaro-Winkler is better for names, Levenshtein for typos
        return (0.4 * jaroWinkler) + (0.4 * levenshtein) + (0.2 * jaccard);
    }

    // Enhanced OCR error pre-normalization
    private static string PreNormalize(string s)
    {
        // Common OCR errors
        s = s.Replace('0', 'o').Replace('1', 'l').Replace('5', 's').Replace('€', 'e')
             .Replace('8', 'b').Replace('6', 'g').Replace('2', 'z');
        
        // Pattern replacements
        s = s.Replace("rn", "m").Replace("cl", "d").Replace("ri", "n");
        
        // Accented characters
        s = s.Replace('à', 'a').Replace('á', 'a').Replace('â', 'a').Replace('ä', 'a')
             .Replace('è', 'e').Replace('é', 'e').Replace('ê', 'e').Replace('ë', 'e')
             .Replace('ì', 'i').Replace('í', 'i').Replace('î', 'i').Replace('ï', 'i')
             .Replace('ò', 'o').Replace('ó', 'o').Replace('ô', 'o').Replace('ö', 'o')
             .Replace('ù', 'u').Replace('ú', 'u').Replace('û', 'u').Replace('ü', 'u')
             .Replace('ç', 'c').Replace('ñ', 'n');
        
        return s;
    }

    private static readonly Regex MultiWs = new Regex("\\s+", RegexOptions.Compiled);
    private static string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        
        // Handle UTF-8 encoding issues
        s = s.Replace("à ", "a").Replace("è", "e").Replace("é", "e")
             .Replace("ì", "i").Replace("ò", "o").Replace("ù", "u");
        
        // Keep only alphanumeric and spaces
        s = Regex.Replace(s, "[^a-z0-9 ]", " ");
        s = MultiWs.Replace(s, " ").Trim();
        
        return s;
    }

    // Existing Levenshtein implementation
    private static double LevenshteinSimilarity(string a, string b)
    {
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

    // Jaro-Winkler similarity implementation
    private static double JaroWinklerSimilarity(string s1, string s2)
    {
        var jaro = JaroSimilarity(s1, s2);
        if (jaro < 0.7) return jaro;
        
        // Calculate common prefix length (up to 4 chars)
        int prefix = 0;
        int maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));
        for (int i = 0; i < maxPrefix && s1[i] == s2[i]; i++)
            prefix++;
        
        return jaro + (0.1 * prefix * (1 - jaro));
    }

    private static double JaroSimilarity(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;
        
        int matchWindow = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;
        
        bool[] s1Matches = new bool[s1.Length];
        bool[] s2Matches = new bool[s2.Length];
        
        int matches = 0;
        int transpositions = 0;
        
        // Identify matches
        for (int i = 0; i < s1.Length; i++)
        {
            int start = Math.Max(0, i - matchWindow);
            int end = Math.Min(i + matchWindow + 1, s2.Length);
            
            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j]) continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }
        
        if (matches == 0) return 0.0;
        
        // Count transpositions
        int k = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }
        
        return (matches / (double)s1.Length + matches / (double)s2.Length + 
                (matches - transpositions / 2.0) / matches) / 3.0;
    }

    // Jaccard similarity for set-based comparison
    private static double JaccardSimilarity(string s1, string s2)
    {
        var set1 = new HashSet<string>(GetNGrams(s1, 2));
        var set2 = new HashSet<string>(GetNGrams(s2, 2));
        
        if (set1.Count == 0 && set2.Count == 0) return 1.0;
        if (set1.Count == 0 || set2.Count == 0) return 0.0;
        
        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        
        return (double)intersection / union;
    }

    private static IEnumerable<string> GetNGrams(string s, int n)
    {
        if (s.Length < n) yield break;
        for (int i = 0; i <= s.Length - n; i++)
            yield return s.Substring(i, n);
    }
}