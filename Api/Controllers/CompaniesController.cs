using AlfaHackaton.Controllers.Controllers;
using AlfaHackaton.Controllers.Extensions;
using Api.Contracts.Companies;
using Api.Contracts.Industries;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Ручки компаний для авторизованных пользователей (JWT-клейм UserId → компания через участников).
/// Юзер берётся из BaseAuthController (пакет AlfaHackaton.Controllers).
/// Префикс company-service добавляется глобально через RoutePrefixConvention.
/// </summary>
[ApiController]
[Route("companies")]
public class CompaniesController(ICompanyService companies) : BaseAuthController
{
    /// <summary>Своя компания с отраслями. 404 — если пользователь ещё не состоит в компании.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMy(CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        return Ok(await companies.ToResponseAsync(company, UserId, ct));
    }

    /// <summary>Частичное обновление компании (регион, выручка, штат, ИНН, bank_profile_id).</summary>
    [HttpPatch("me")]
    public async Task<IActionResult> PatchMy([FromBody] UpdateCompanyRequest patch, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        if (!await companies.UpdateAsync(company.Id, patch, ct))
            return NotFound(new { error = "Компания не найдена" });

        return Ok(await companies.ToResponseAsync(company, UserId, ct));
    }

    /// <summary>Заменить отрасли компании кодами ОКВЭД: {"industryCodes": ["G", "C"]}.</summary>
    [HttpPut("me/industries")]
    public async Task<IActionResult> PutIndustries([FromBody] SetIndustriesRequest request, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        var (ok, validCodes) = await companies.SetIndustriesByCodesAsync(company.Id, request.IndustryCodes, ct);
        if (!ok)
        {
            return BadRequest(new { error = "Есть неизвестные коды отраслей", valid = validCodes });
        }

        return Ok(await companies.ToResponseAsync(company, UserId, ct));
    }

    /// <summary>Участники своей компании.</summary>
    [HttpGet("me/members")]
    public async Task<IActionResult> GetMembers(CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        return Ok(await companies.GetMembersAsync(company.Id, ct));
    }

    /// <summary>Группа компаний: родитель + дочерние (если компания в группе).</summary>
    [HttpGet("me/group")]
    public async Task<IActionResult> GetGroup(CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        var parent = company.ParentCompanyId is null
            ? null
            : await companies.GetByIdAsync(company.ParentCompanyId.Value, ct);
        var children = await companies.GetChildrenAsync(company.Id, ct);

        return Ok(new
        {
            current = await companies.ToResponseAsync(company, UserId, ct),
            parent = parent is null ? null : await companies.ToResponseAsync(parent, UserId, ct),
            children = await Task.WhenAll(children.Select(c => companies.ToResponseAsync(c, UserId, ct)))
        });
    }
}
