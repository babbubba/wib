using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

public record CategoryLookupResponse(Guid? Id, string Name, bool Exists);
public record CategoryDto(Guid Id, string Name);
public record UpsertCategoryRequest(string Name);

[ApiController]
[Authorize(Roles = "wmc")]
[Route("categories")]
public class CategoriesController : ControllerBase
{
    private readonly WibDbContext _db;
    public CategoriesController(WibDbContext db) { _db = db; }

    [HttpGet("lookup")]
    public async Task<ActionResult<CategoryLookupResponse>> Lookup([FromQuery] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        var trimmed = name.Trim();
        var lower = trimmed.ToLowerInvariant();
        var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Name.ToLower() == lower, ct);
        if (cat != null)
        {
            return Ok(new CategoryLookupResponse(cat.Id, cat.Name, true));
        }
        var norm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
        return Ok(new CategoryLookupResponse(null, norm, false));
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<CategoryDto>>> Search([FromQuery] string query, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(new List<CategoryDto>());
        var q = query.Trim().ToLowerInvariant();
        take = take <= 0 || take > 50 ? 10 : take;
        var list = await _db.Categories.AsNoTracking()
            .Where(c => c.Name.ToLower().Contains(q))
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] UpsertCategoryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest();
        var lower = req.Name.Trim().ToLowerInvariant();
        var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == lower, ct);
        if (existing != null) return Ok(new CategoryDto(existing.Id, existing.Name));
        var norm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
        var cat = new WIB.Domain.Category { Name = norm };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Lookup), new { name = norm }, new CategoryDto(cat.Id, cat.Name));
    }
}
