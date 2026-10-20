using AlfaHackaton.Controllers.Controllers;
using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Инвайты: создание (только создатель компании), список, публичная валидация кода.
/// Хранение — MongoDB с TTL: протухшие инвайты удаляются сами.
/// Юзер берётся из BaseAuthController (пакет AlfaHackaton.Controllers).
/// Префикс company-service добавляется глобально через RoutePrefixConvention.
/// </summary>
[ApiController]
public class InvitesController(IInviteService invites, ICompanyService companies) : BaseAuthController
{
    /// <summary>Создать инвайт-код (только создатель компании). TTL по умолчанию 7 дней.</summary>
    [HttpPost("companies/me/invites")]
    public async Task<IActionResult> Create([FromBody] CreateInviteRequest? request, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });
        if (company.CreatorId != UserId)
            return Forbid();

        var invite = await invites.CreateAsync(company.Id, UserId, request?.ExpiresInDays, ct);
        return Ok(invite);
    }

    /// <summary>Инвайты своей компании со статусами (active/used/expired).</summary>
    [HttpGet("companies/me/invites")]
    public async Task<IActionResult> ListMine(CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(UserId, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });

        return Ok(await invites.ListByCompanyAsync(company.Id));
    }

    /// <summary>
    /// Публичная проверка кода (страница инвайта перед регистрацией): валиден ли и куда приглашает.
    /// </summary>
    [HttpGet("invites/{code}")]
    public async Task<IActionResult> Validate(string code, CancellationToken ct = default)
    {
        return Ok(await invites.ValidateAsync(code, ct));
    }
}
