using AlfaHackaton.Result;
using Api.Contracts.Companies;
using Api.Contracts.Industries;

namespace Api.Services.Interfaces;

public interface ICompanyService
{
    Task<Result<CompanyResponse>> CreateMyAsync(Guid userId, CreateCompanyRequest request, CancellationToken ct = default);
    Task<Result<CompanyResponse>> GetMyAsync(Guid userId, CancellationToken ct = default);
    Task<Result<CompanyResponse>> PatchMyAsync(Guid userId, UpdateCompanyRequest patch, CancellationToken ct = default);
    Task<Result<CompanyResponse>> SetMyIndustriesAsync(Guid userId, SetIndustriesRequest request, CancellationToken ct = default);
    Task<Result<List<MemberResponse>>> GetMyMembersAsync(Guid userId, CancellationToken ct = default);
    Task<Result<CompanyGroupResponse>> GetMyGroupAsync(Guid userId, CancellationToken ct = default);
    Task<Result<CreatedCompanyResponse>> CreateInternalAsync(CreateCompanyInternalRequest request, CancellationToken ct = default);
    Task<Result<CompanyMembershipResponse>> GetByUserInternalAsync(Guid userId, CancellationToken ct = default);
    Task<Result<List<Guid>>> GetMemberIdsAsync(Guid companyId, CancellationToken ct = default);
}
