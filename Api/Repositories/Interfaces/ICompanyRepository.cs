using Api.Contracts.Companies;
using Api.Models;

namespace Api.Repositories.Interfaces;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Create the company and creator membership atomically; null if already a member.</summary>
    Task<Company?> TryCreateAsync(Company company, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default);
    Task SetIndustriesAsync(Guid companyId, IReadOnlyCollection<Guid> industryIds, CancellationToken ct = default);
    Task<List<Industry>> GetIndustriesAsync(Guid companyId, CancellationToken ct = default);
    Task<List<CompanyMember>> GetMembersAsync(Guid companyId, CancellationToken ct = default);
    Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);
    /// <summary>Reserve membership before claiming an invite; roll back if the claim is rejected.</summary>
    Task<JoinCompanyOutcome> TryJoinAsync(Guid companyId, Guid userId, Func<Task<bool>> tryClaimInvite, CancellationToken ct = default);
}
