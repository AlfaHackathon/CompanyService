using AlfaHackaton.Result;
using Api.Contracts.Invites;

namespace Api.Services.Interfaces;

public interface IInviteService
{
    Task<Result<InviteResponse>> CreateMineAsync(Guid userId, CreateInviteRequest? request, CancellationToken ct = default);
    Task<Result<List<InviteResponse>>> ListMineAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InviteInfoResponse>> ValidateAsync(string code, CancellationToken ct = default);
    Task<Result<UsedInviteResponse>> UseInternalAsync(string code, Guid userId, CancellationToken ct = default);
}
