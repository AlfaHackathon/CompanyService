using AlfaHackaton.Controllers.Controllers;
using Api.Contracts.Companies;
using Api.Contracts.Industries;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("companies")]
public class CompaniesController(ICompanyService companies) : BaseAuthController
{
    [HttpPost]
    public async Task<IActionResult> CreateMyAsync([FromBody] CreateCompanyRequest request, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.CreateMyAsync(UserId, request, ct));
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyAsync(CancellationToken ct = default)
    {
        return OkOrProblem(await companies.GetMyAsync(UserId, ct));
    }

    [HttpPatch("me")]
    public async Task<IActionResult> PatchMyAsync([FromBody] UpdateCompanyRequest patch, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.PatchMyAsync(UserId, patch, ct));
    }

    [HttpPut("me/industries")]
    public async Task<IActionResult> PutIndustriesAsync([FromBody] SetIndustriesRequest request, CancellationToken ct = default)
    {
        return OkOrProblem(await companies.SetMyIndustriesAsync(UserId, request, ct));
    }

    [HttpGet("me/members")]
    public async Task<IActionResult> GetMembersAsync(CancellationToken ct = default)
    {
        return OkOrProblem(await companies.GetMyMembersAsync(UserId, ct));
    }

    [HttpGet("me/group")]
    public async Task<IActionResult> GetGroupAsync(CancellationToken ct = default)
    {
        return OkOrProblem(await companies.GetMyGroupAsync(UserId, ct));
    }
}
