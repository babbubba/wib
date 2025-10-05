using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WIB.Application.Contracts.Ml;
using WIB.Application.Interfaces;

namespace WIB.API.Controllers;

[ApiController]
[Authorize(Roles = "wmc")]
[Route("ml")]
public class MlController : ControllerBase
{
    private readonly IProductClassifier _cls;

    public MlController(IProductClassifier cls)
    {
        _cls = cls;
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<MlSuggestionsResponse>> Suggestions([FromQuery] string labelRaw, CancellationToken ct)
    {
        var (typeId, catId, conf) = await _cls.PredictAsync(labelRaw, ct);
        var res = new MlSuggestionsResponse();
        if (typeId.HasValue)
            res.TypeCandidates.Add(new MlCandidateDto { Id = typeId.Value, Name = string.Empty, Conf = conf });
        if (catId.HasValue)
            res.CategoryCandidates.Add(new MlCandidateDto { Id = catId.Value, Name = string.Empty, Conf = conf });
        return Ok(res);
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] MlFeedbackRequest req, CancellationToken ct)
    {
        await _cls.FeedbackAsync(req.LabelRaw, req.Brand, req.FinalTypeId, req.FinalCategoryId, ct);
        return Ok(new { status = "ok" });
    }
}
