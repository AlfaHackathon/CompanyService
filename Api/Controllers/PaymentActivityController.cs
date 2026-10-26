using AlfaHackaton.Controllers.Controllers;
using Api.Common;
using Api.Contracts.Payments;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[ApiController]
[InternalSecret]
[Route("internal")]
public sealed class PaymentActivityController(PaymentActivityService service) : BaseController
{
    private bool Query(params string[] allowed) => Request.Query.All(pair => allowed.Contains(pair.Key, StringComparer.Ordinal) && pair.Value.Count == 1);
    private IActionResult InvalidQuery() => BadRequest(new { message = "Only documented, nonrepeated payment query parameters are accepted." });
    /// <summary>Store one exact source row. Retry identical content with the same logical source/version/row identity.</summary>
    [HttpPost("payment-rows")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> Save([FromBody] PaymentRowInput input) => Query() ? OkOrProblem(await service.Save(input)) : InvalidQuery();
    /// <summary>Observed incoming/outgoing/self-transfer activity for one exact logical source version; no-row is unknown.</summary>
    [HttpGet("payment-activity/{companyId:guid}")]
    public async Task<IActionResult> Activity(Guid companyId, [FromQuery, BindRequired] string sourceId,
        [FromQuery, BindRequired] string sourceVersionId, [FromQuery, BindRequired] DateTimeOffset asOf) =>
        Query("sourceId", "sourceVersionId", "asOf") ? OkOrProblem(await service.Activity(companyId, sourceId, sourceVersionId, asOf)) : InvalidQuery();
}
