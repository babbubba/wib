using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

public record ProductTypeDto(Guid Id, string Name);
public record UpsertProductTypeRequest(string Name);

[ApiController]
[Authorize(Roles = "wmc")]
[Route("producttypes")]
public class ProductTypesController : ControllerBase
{
    private readonly WibDbContext _db;
    public ProductTypesController(WibDbContext db) { _db = db; }

    [HttpGet("search")]
    public async Task<ActionResult<List<ProductTypeDto>>> Search([FromQuery] string query, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(new List<ProductTypeDto>());
        var q = query.Trim().ToLowerInvariant();
        take = take <= 0 || take > 50 ? 10 : take;
        var list = await _db.ProductTypes.AsNoTracking()
            .Where(t => t.Name.ToLower().Contains(q))
            .OrderBy(t => t.Name)
            .Take(take)
            .Select(t => new ProductTypeDto(t.Id, t.Name))
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<ProductTypeDto>> Create([FromBody] UpsertProductTypeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest();
        var lower = req.Name.Trim().ToLowerInvariant();
        var existing = await _db.ProductTypes.FirstOrDefaultAsync(t => t.Name.ToLower() == lower, ct);
        if (existing != null) return Ok(new ProductTypeDto(existing.Id, existing.Name));
        var norm = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
        var pt = new WIB.Domain.ProductType { Name = norm };
        _db.ProductTypes.Add(pt);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Search), new { query = norm }, new ProductTypeDto(pt.Id, pt.Name));
    }
}

