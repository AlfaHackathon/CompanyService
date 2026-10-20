using System.Security.Cryptography;
using Api.Contracts.Invites;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Services;

/// <summary>Результат internal-использования инвайта.</summary>
/// <param name="Error">null — успех.</param>
/// <param name="ErrorStatus">HTTP-код ошибки.</param>
/// <param name="CompanyId">id компании инвайта.</param>
/// <param name="CompanyName">название компании.</param>
public record UseInviteResult(string? Error, int ErrorStatus, Guid CompanyId, string? CompanyName);

/// <summary>Бизнес-операции над инвайтами.</summary>
public class InviteService(
    IInviteRepository invites,
    ICompanyRepository companies) : IInviteService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    public async Task<InviteResponse> CreateAsync(Guid companyId, Guid userId, int? expiresInDays, CancellationToken ct = default)
    {
        var ttl = expiresInDays is > 0 and <= 30
            ? TimeSpan.FromDays(expiresInDays.Value)
            : DefaultTtl;

        var invite = await invites.CreateAsync(companyId, userId, GenerateCode(), ttl);
        return ToResponse(invite);
    }

    public async Task<List<InviteResponse>> ListByCompanyAsync(Guid companyId)
    {
        var list = await invites.ListByCompanyAsync(companyId);
        return list.Select(ToResponse).ToList();
    }

    public Task<InviteDocument?> GetByCodeAsync(string code)
        => invites.GetByCodeAsync(code);

    public async Task<InviteInfoResponse> ValidateAsync(string code, CancellationToken ct = default)
    {
        var invite = await invites.GetByCodeAsync(code);
        if (invite is null)
            return new InviteInfoResponse(false, null, "Инвайт не найден");

        if (invite.UsedAt is not null)
            return new InviteInfoResponse(false, null, "Инвайт уже использован");

        if (invite.ExpiresAt <= DateTime.UtcNow)
            return new InviteInfoResponse(false, null, "Инвайт истёк");

        var company = await companies.GetByIdAsync(invite.CompanyId, ct);
        return new InviteInfoResponse(true, company?.Name, null);
    }

    public async Task<UseInviteResult> UseInternalAsync(string code, Guid userId, CancellationToken ct = default)
    {
        var invite = await invites.GetByCodeAsync(code);
        if (invite is null) return new UseInviteResult("Инвайт не найден", StatusCodes.Status404NotFound, Guid.Empty, null);
        if (invite.UsedAt is not null) return new UseInviteResult("Инвайт уже использован", StatusCodes.Status409Conflict, Guid.Empty, null);
        if (invite.ExpiresAt <= DateTime.UtcNow) return new UseInviteResult("Инвайт истёк", StatusCodes.Status410Gone, Guid.Empty, null);

        var used = await invites.TryUseAsync(code, userId);
        if (used is null) return new UseInviteResult("Инвайт уже использован", StatusCodes.Status409Conflict, Guid.Empty, null);

        await companies.AddMemberAsync(used.CompanyId, userId, ct);
        var company = await companies.GetByIdAsync(used.CompanyId, ct);
        return new UseInviteResult(null, 0, used.CompanyId, company?.Name);
    }

    public Task InsertExpiredForTestAsync(string code, Guid companyId)
        => invites.InsertExpiredForTestAsync(code, companyId);

    private static string GenerateCode()
    {
        // 10 символов url-safe — коротко для ссылки и без неоднозначных символов
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(RandomNumberGenerator.GetItems<char>(alphabet, 10));
    }

    private static InviteResponse ToResponse(InviteDocument invite) => new(
        invite.Id, invite.Code, invite.CompanyId, invite.CreatedAt, invite.ExpiresAt,
        invite.UsedBy, invite.UsedAt,
        Status: invite.UsedAt is not null ? "used"
            : invite.ExpiresAt <= DateTime.UtcNow ? "expired"
            : "active");
}
