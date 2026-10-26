using AlfaHackaton.Controllers.Controllers;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[InternalSecret]
[Route("internal/knowledge/companies/{companyId:guid}/capture-declared-profile")]
public sealed class DeclaredCompanyCaptureController(DeclaredCompanyCaptureService capture) : BaseController
{
    /// <summary>Explicitly capture this company's current operational declarations. Retry with the same captureId.</summary>
    [HttpPost]
    public async Task<IActionResult> Capture(Guid companyId, [FromBody] CaptureDeclaredProfileRequest request, CancellationToken ct) =>
        OkOrProblem(await capture.Capture(companyId, request, ct));
}
