using AlfaHackaton.Auth.Exceptions;
using AlfaHackaton.Controllers.Controllers;
using AlfaHackaton.Controllers.Extensions;
using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Инвайты: создание (только создатель компании), список, публичная валидация кода.
/// Хранение — MongoDB с TTL: протухшие инвайты удаляются сами.
/// Префикс company-service добавляется глобально через RoutePrefixConvention.
/// </summary>
[ApiController]
public class InvitesController(IInviteService invites, ICompanyService companies) : BaseAuthController
{
    /// <summary>Создать инвайт-код (только создатель компании). TTL по умолчанию 7 дней.</summary>
    [HttpPost("companies/me/invites")]
    public async Task<IActionResult> Create([FromBody] CreateInviteRequest? request, CancellationToken ct = default)
    {
        var userId = TryGetUserId();
        if (userId is null) return Unauthorized(new { error = "Невалидный или отсутствующий токен" });

        var company = await companies.GetByUserAsync(userId.Value, ct);
        if (company is null) return NotFound(new { error = "Пользователь не состоит в компании" });
        if (company.CreatorId != userId.Value)
            return Forbid();

        var invite = await invites.CreateAsync(company.Id, userId.Value, request?.ExpiresInDays, ct);
        return Ok(invite);
    }

    /// <summary>Инвайты своей компании со статусами (active/used/expired).</summary>
    [HttpGet("companies/me/invites")]
    public async Task<IActionResult> ListMine(CancellationToken ct = default)
    {
        var userId = TryGetUserId();
        if (userId is null) return Unauthorized(new { error = "Невалидный или отсутствующий токен" });

        var company = await companies.GetByUserAsync(userId.Value, ct);
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

    /// <summary>
    /// UserId из JWT Authorization-заголовка через пакет AlfaHackaton.Controllers (Extensions.Jwt).
    /// null — токена нет или он битый.
    /// </summary>
    private Guid? TryGetUserId()
    {
        try
        {
            var token = ExtractToken(Request.Headers.Authorization.ToString());
            if (token is null) return null;
            return Jwt.GetPayload(token).UserId;
        }
        catch (Exception exception)
            when (exception is TokenInvalidException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? ExtractToken(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return null;

        const string bearerPrefix = "Bearer ";
        var token = authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[bearerPrefix.Length..]
            : authorizationHeader;

        token = token.Trim();
        return token.Length == 0 ? null : token;
    }
}
