using AlfaHackaton.Controllers.Controllers;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("industries")]
public class IndustriesController(IIndustryService industries) : BaseAuthController
{
    [HttpGet]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct = default)
    {
        return OkOrProblem(await industries.GetAllAsync(ct));
    }
}
