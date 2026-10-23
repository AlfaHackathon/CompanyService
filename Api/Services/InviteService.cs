using System.Net;
using System.Security.Cryptography;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Invites;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Services;

public class InviteService(IInviteRepository invites, ICompanyRepository companies) : IInviteService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    public async Task<Result<InviteResponse>> CreateMineAsync(
        Guid userId,
        CreateInviteRequest? request,
        CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(Failure("Пользователь не состоит в компании", HttpStatusCode.NotFound, ErrorType.NotFound));
        }
        if (company.CreatorId != userId)
        {
            return new(Failure("Только создатель компании может приглашать участников", HttpStatusCode.Forbidden, ErrorType.NoRights));
        }
        var expiresInDays = request?.ExpiresInDays;
        var ttl = expiresInDays is > 0 and <= 30
            ? TimeSpan.FromDays(expiresInDays.Value)
            : DefaultTtl;
        var invite = await invites.CreateAsync(company.Id, userId, GenerateCode(), ttl);
        return new(ToResponse(invite), null);
    }

    public async Task<Result<List<InviteResponse>>> ListMineAsync(Guid userId, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(Failure("Пользователь не состоит в компании", HttpStatusCode.NotFound, ErrorType.NotFound));
        }
        return new((await invites.ListByCompanyAsync(company.Id)).Select(ToResponse).ToList(), null);
    }

    public async Task<Result<InviteInfoResponse>> ValidateAsync(string code, CancellationToken ct = default)
    {
        var invite = await invites.GetByCodeAsync(code);
        if (invite is null)
        {
            return new(new InviteInfoResponse { Valid = false, Reason = "Инвайт не найден", StatusCode = StatusCodes.Status404NotFound }, null);
        }
        if (invite.UsedAt is not null)
        {
            return new(new InviteInfoResponse { Valid = false, Reason = "Инвайт уже использован", StatusCode = StatusCodes.Status409Conflict }, null);
        }
        if (invite.ExpiresAt <= DateTime.UtcNow)
        {
            return new(new InviteInfoResponse { Valid = false, Reason = "Инвайт истёк", StatusCode = StatusCodes.Status410Gone }, null);
        }
        var company = await companies.GetByIdAsync(invite.CompanyId, ct);
        return new(new InviteInfoResponse { Valid = true, CompanyName = company?.Name }, null);
    }

    public async Task<Result<UsedInviteResponse>> UseInternalAsync(
        string code,
        Guid userId,
        CancellationToken ct = default)
    {
        var invite = await invites.GetByCodeAsync(code);
        if (invite is null)
        {
            return new(Failure("Инвайт не найден", HttpStatusCode.NotFound, ErrorType.NotFound));
        }
        if (invite.UsedAt is not null)
        {
            return new(Failure("Инвайт уже использован", HttpStatusCode.Conflict));
        }
        if (invite.ExpiresAt <= DateTime.UtcNow)
        {
            return new(Failure("Инвайт истёк", HttpStatusCode.Gone));
        }
        if (await companies.GetByUserAsync(userId, ct) is not null)
        {
            return new(Failure("Пользователь уже состоит в компании", HttpStatusCode.Conflict));
        }
        var used = await invites.TryUseAsync(code, userId);
        if (used is null)
        {
            return new(Failure("Инвайт уже использован", HttpStatusCode.Conflict));
        }
        await companies.AddMemberAsync(used.CompanyId, userId, ct);
        var company = await companies.GetByIdAsync(used.CompanyId, ct);
        await invites.DeleteAsync(code);
        return new(new UsedInviteResponse { CompanyId = used.CompanyId, CompanyName = company?.Name }, null);
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(RandomNumberGenerator.GetItems<char>(alphabet, 10));
    }

    private static InviteResponse ToResponse(InviteDocument invite)
    {
        return new InviteResponse
        {
            Id = invite.Id,
            Code = invite.Code,
            CompanyId = invite.CompanyId,
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt,
            UsedBy = invite.UsedBy,
            UsedAt = invite.UsedAt,
            Status = invite.UsedAt is not null ? "used" : invite.ExpiresAt <= DateTime.UtcNow ? "expired" : "active"
        };
    }

    private static Error Failure(
        string message,
        HttpStatusCode status,
        ErrorType type = ErrorType.CorruptedInfo)
    {
        return new(message, ErrorSource.Client, type) { HttpStatusCode = status };
    }
}
