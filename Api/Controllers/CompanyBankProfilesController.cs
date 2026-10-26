using AlfaHackaton.Controllers.Controllers;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[ApiController]
[InternalSecret]
[Route("internal/bank-profiles")]
public sealed class CompanyBankProfilesController(CompanyBankProfileService service) : BaseController
{
    private bool Query(params string[] allowed) => Request.Query.All(pair => allowed.Contains(pair.Key, StringComparer.Ordinal) && pair.Value.Count == 1);
    private IActionResult InvalidQuery() => BadRequest(new { message = "Only documented, nonrepeated query parameters are accepted." });

    /// <summary>Explicit immutable capture for a Company UUID; does not create registration or membership.</summary>
    [HttpPost("{companyId:guid}/capture")]
    [RequestSizeLimit(1048576)]
    public async Task<IActionResult> Capture(Guid companyId, [FromBody] CaptureCompanyBankProfileRequest request, CancellationToken ct) =>
        Query() ? OkOrProblem(await service.Capture(companyId, request, ct)) : InvalidQuery();

    [HttpGet("{companyId:guid}")]
    public async Task<IActionResult> Get(Guid companyId, [FromQuery, BindRequired] DateTimeOffset asOf) =>
        Query("asOf") ? OkOrProblem(await service.Get(companyId, asOf)) : InvalidQuery();

    /// <summary>Bounded append-sequence pagination of immutable canonical profile versions.</summary>
    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromQuery] string? after = null, [FromQuery] int limit = 100) =>
        Query("after", "limit") ? OkOrProblem(await service.Versions(after, limit)) : InvalidQuery();
}
