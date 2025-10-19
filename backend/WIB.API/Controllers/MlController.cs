using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WIB.Application.Contracts.Ml;
using WIB.Application.Interfaces;
using WIB.Infrastructure.Data;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("ml")]
public class MlController : ControllerBase
{
    private readonly IProductClassifier _cls;
    private readonly WibDbContext _db;

    public MlController(IProductClassifier cls, WibDbContext db)
    {
        _cls = cls;
        _db = db;
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<MlSuggestionsResponse>> Suggestions([FromQuery] string labelRaw, CancellationToken ct)
    {
        var pred = await _cls.PredictAsync(labelRaw, ct);
        var res = new MlSuggestionsResponse();
        
        // Populate type candidate with name from database
        if (pred.TypeId.HasValue)
        {
            var productType = await _db.ProductTypes.FirstOrDefaultAsync(pt => pt.Id == pred.TypeId.Value, ct);
            res.TypeCandidates.Add(new MlCandidateDto 
            { 
                Id = pred.TypeId.Value, 
                Name = productType?.Name ?? string.Empty, 
                Conf = pred.Confidence 
            });
        }
        
        // Populate category candidate with name from database
        if (pred.CategoryId.HasValue)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == pred.CategoryId.Value, ct);
            res.CategoryCandidates.Add(new MlCandidateDto 
            { 
                Id = pred.CategoryId.Value, 
                Name = category?.Name ?? string.Empty, 
                Conf = pred.Confidence 
            });
        }
        
        return Ok(res);
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] MlFeedbackRequest req, CancellationToken ct)
    {
        await _cls.FeedbackAsync(req.LabelRaw, req.Brand, req.FinalTypeId, req.FinalCategoryId, ct);
        return Ok(new { status = "ok" });
    }
}
