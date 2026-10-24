using AlfaHackaton.Controllers.Controllers;
using Api.Contracts.Invites;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
public class InvitesController(IInviteService invites) : BaseAuthController
{
    [HttpPost("companies/me/invites")]
    public async Task<IActionResult> CreateAsync([FromBody] CreateInviteRequest? request, CancellationToken ct = default)
    {
        return OkOrProblem(await invites.CreateMineAsync(UserId, request, ct));
    }

    [HttpGet("companies/me/invites")]
    public async Task<IActionResult> ListMineAsync(CancellationToken ct = default)
    {
        return OkOrProblem(await invites.ListMineAsync(UserId, ct));
    }

    [HttpGet("invites/{code}")]
    public async Task<IActionResult> ValidateAsync(string code, CancellationToken ct = default)
    {
        return OkOrProblem(await invites.ValidateAsync(code, ct));
    }
}
