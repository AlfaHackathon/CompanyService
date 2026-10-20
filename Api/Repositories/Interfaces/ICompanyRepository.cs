using Api.Contracts.Companies;
using Api.Models;

namespace Api.Repositories.Interfaces;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<Company> CreateAsync(Company company, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default);
    Task SetIndustriesAsync(Guid companyId, IReadOnlyCollection<Guid> industryIds, CancellationToken ct = default);
    Task<List<Industry>> GetIndustriesAsync(Guid companyId, CancellationToken ct = default);
    Task<List<CompanyMember>> GetMembersAsync(Guid companyId, CancellationToken ct = default);
    Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);
    Task AddMemberAsync(Guid companyId, Guid userId, CancellationToken ct = default);
}
