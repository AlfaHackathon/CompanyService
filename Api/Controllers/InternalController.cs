using AlfaHackaton.Controllers.Controllers;
using Api.Common;
using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[InternalSecret]
[Route("internal")]
public class InternalController(ICompanyService companies, IInviteService invites) : BaseController
{
    [HttpPost("companies")]
    public async Task<IActionResult> CreateCompanyAsync([FromBody] CreateCompanyInternalRequest request, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.CreateInternalAsync(request, ct));
    }

    [HttpGet("companies/by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.GetByUserInternalAsync(userId, ct));
    }

    [HttpGet("companies/{companyId:guid}/member-ids")]
    public async Task<IActionResult> GetMemberIdsAsync(Guid companyId, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.GetMemberIdsAsync(companyId, ct));
    }

    [HttpGet("invites/{code}")]
    public async Task<IActionResult> ValidateInviteAsync(string code, CancellationToken ct = default)
    {
        return OkOrProblem(await invites.ValidateAsync(code, ct));
    }

    [HttpPost("invites/{code}/use")]
    public async Task<IActionResult> UseInviteAsync(
        string code,
        [FromBody] UseInviteInternalRequest request,
        CancellationToken ct = default)
    {
        return OkOrProblem(await invites.UseInternalAsync(code, request.UserId, ct));
    }
}
