using AlfaHackaton.Controllers.Controllers;
using Api.Contracts.Industries;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Справочник отраслей (разделы ОКВЭД A–U).
/// </summary>
[ApiController]
[Route("industries")]
public class IndustriesController(IIndustryService industries) : BaseAuthController
{
    /// <summary>Все отрасли (сид: 21 раздел ОКВЭД).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default) =>
        Ok((await industries.GetAllAsync(ct))
            .Select(i => new IndustryResponse(i.Id, i.Code, i.Name)));
}
