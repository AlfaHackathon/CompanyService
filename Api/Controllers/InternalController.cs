using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// internal-ручки для других сервисов кластера (auth-service: регистрация с компанией/инвайтом).
/// Защищены заголовком X-Internal-Secret.
/// </summary>
[ApiController]
[Route("internal")]
public class InternalController(
    ICompanyService companies,
    IInviteService invites) : ControllerBase
{
    private const string InternalSecretHeader = "X-Internal-Secret";

    /// <summary>
    /// Создать компанию при регистрации пользователя без инвайта (вызывает auth-service).
    /// </summary>
    [HttpPost("companies")]
    public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyInternalRequest request, CancellationToken ct = default)
    {
        if (!IsInternalRequest()) return Unauthorized(new { error = "Только для внутренних вызовов" });

        var created = await companies.CreateInternalAsync(request, ct);
        return Ok(new { id = created.Id, name = created.Name });
    }

    /// <summary>Компания пользователя (для других сервисов).</summary>
    [HttpGet("companies/by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(Guid userId, CancellationToken ct = default)
    {
        if (!IsInternalRequest()) return Unauthorized(new { error = "Только для внутренних вызовов" });

        var company = await companies.GetByUserAsync(userId, ct);
        return company is null
            ? NotFound(new { error = "Пользователь не состоит в компании" })
            : Ok(new { id = company.Id, name = company.Name, bankProfileId = company.BankProfileId });
    }

    /// <summary>
    /// Использовать инвайт: пометить использованным и добавить пользователя в компанию.
    /// Вызывает auth-service при регистрации по инвайт-ссылке.
    /// </summary>
    [HttpPost("invites/{code}/use")]
    public async Task<IActionResult> UseInvite(string code, [FromBody] UseInviteInternalRequest request, CancellationToken ct = default)
    {
        if (!IsInternalRequest()) return Unauthorized(new { error = "Только для внутренних вызовов" });

        var result = await invites.UseInternalAsync(code, request.UserId, ct);
        if (result.Error is not null)
        {
            return result.ErrorStatus switch
            {
                StatusCodes.Status404NotFound => NotFound(new { error = result.Error }),
                StatusCodes.Status410Gone => StatusCode(StatusCodes.Status410Gone, new { error = result.Error }),
                _ => Conflict(new { error = result.Error })
            };
        }

        return Ok(new { companyId = result.CompanyId, companyName = result.CompanyName });
    }

    private bool IsInternalRequest()
    {
        var expected = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Internal:Secret"];
        var provided = Request.Headers[InternalSecretHeader].ToString();
        return !string.IsNullOrEmpty(expected) && expected == provided;
    }
}
