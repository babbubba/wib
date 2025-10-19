using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

public record StoreDto(Guid Id, string Name, string? Address, string? City, string? PostalCode, string? VatNumber, string? Chain);
public record UpsertStoreRequest(string Name, string? Address, string? City, string? PostalCode, string? VatNumber, string? Chain);

[ApiController]
[Authorize(Roles = "wmc")]
[Route("stores")]
public class StoresController : ControllerBase
{
    private readonly WibDbContext _db;
    public StoresController(WibDbContext db) { _db = db; }

    [HttpGet("search")]
    public async Task<ActionResult<List<StoreDto>>> Search([FromQuery] string query, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(new List<StoreDto>());
        var q = query.Trim().ToLowerInvariant();
        take = take <= 0 || take > 50 ? 10 : take;
        var list = await _db.Stores.AsNoTracking()
            .Where(s => s.Name.ToLower().Contains(q))
            .OrderBy(s => s.Name)
            .Take(take)
            .Select(s => new {
                s.Id,
                s.Name,
                s.Chain,
                FirstLoc = _db.StoreLocations.AsNoTracking().Where(sl => sl.StoreId == s.Id).OrderBy(sl => sl.Address).FirstOrDefault()
            })
            .Select(x => new StoreDto(x.Id, x.Name, x.FirstLoc!.Address, x.FirstLoc!.City, x.FirstLoc!.PostalCode, x.FirstLoc!.VatNumber, x.Chain))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<StoreDto>> Create([FromBody] UpsertStoreRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest();
        var lower = req.Name.Trim().ToLowerInvariant();
        var existing = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Name.ToLower() == lower, ct);
        if (existing != null)
        {
            var firstLoc = await _db.StoreLocations.AsNoTracking().Where(sl => sl.StoreId == existing.Id).OrderBy(sl => sl.Address).FirstOrDefaultAsync(ct);
            return Ok(new StoreDto(existing.Id, existing.Name, firstLoc?.Address, firstLoc?.City, firstLoc?.PostalCode, firstLoc?.VatNumber, existing.Chain));
        }
        var norm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
        var store = new WIB.Domain.Store
        {
            Name = norm,
            NameNormalized = NormalizeName(req.Name),
            Chain = req.Chain?.Trim()
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync(ct);
        // Optionally create first location if details provided
        if (!string.IsNullOrWhiteSpace(req.Address) || !string.IsNullOrWhiteSpace(req.City) || !string.IsNullOrWhiteSpace(req.PostalCode) || !string.IsNullOrWhiteSpace(req.VatNumber))
        {
            var loc = new WIB.Domain.StoreLocation
            {
                StoreId = store.Id,
                Address = req.Address?.Trim(),
                City = req.City?.Trim(),
                PostalCode = req.PostalCode?.Trim(),
                VatNumber = req.VatNumber?.Trim()
            };
            _db.StoreLocations.Add(loc);
            await _db.SaveChangesAsync(ct);
            return CreatedAtAction(nameof(Search), new { query = norm }, new StoreDto(store.Id, store.Name, loc.Address, loc.City, loc.PostalCode, loc.VatNumber, store.Chain));
        }
        return CreatedAtAction(nameof(Search), new { query = norm }, new StoreDto(store.Id, store.Name, null, null, null, null, store.Chain));
    }
    private static string NormalizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim().ToLowerInvariant();
        s = RemoveDiacritics(s);
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9 ]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        return s;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();
        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }
        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
