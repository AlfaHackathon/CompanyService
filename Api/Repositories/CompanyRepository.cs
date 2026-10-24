using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Contracts.Companies;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Resources;

namespace Api.Repositories;

public class CompanyRepository(IDapperContext context) : ICompanyRepository
{
    public async Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.FirstOrDefault<Company>(new QueryObject(CompanySql.GetById, new { id }));
    }

    public async Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await context.FirstOrDefault<Company>(new QueryObject(CompanySql.GetByUser, new { userId }));
    }

    public async Task<Company?> TryCreateAsync(Company company, CancellationToken ct = default)
    {
        using var transaction = context.BeginTransaction();
        var created = await transaction.CommandWithResponse<Company>(new QueryObject(CompanySql.Create, new
        {
            id = company.Id,
            name = company.Name,
            inn = company.Inn,
            creator_id = company.CreatorId,
            parent_company_id = company.ParentCompanyId,
            region = company.Region,
            revenue = company.Revenue,
            count_of_employees = company.CountOfEmployees,
            bank_profile_id = company.BankProfileId,
            created_at = company.CreatedAt
        }));
        if (!await transaction.CommandWithResponse<bool>(new QueryObject(CompanySql.ReserveMember,
                new { companyId = company.Id, userId = company.CreatorId, joinedAt = DateTime.UtcNow })))
        {
            return null;
        }
        transaction.Commit();
        return created;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default)
    {
        if (await GetByIdAsync(id, ct) is null) return false;

        // частичный PATCH: COALESCE — null в запросе означает «поле не меняли»
        await context.Command(new QueryObject(CompanySql.Update, new
        {
            id,
            name = patch.Name,
            inn = patch.Inn,
            region = patch.Region,
            revenue = patch.Revenue,
            count_of_employees = patch.CountOfEmployees,
            bank_profile_id = patch.BankProfileId
        }));
        return true;
    }

    public async Task SetIndustriesAsync(Guid companyId, IReadOnlyCollection<Guid> industryIds, CancellationToken ct = default)
    {
        await context.Command(new QueryObject(CompanySql.SetIndustriesDelete, new { companyId }));
        foreach (var industryId in industryIds)
        {
            await context.Command(new QueryObject(CompanySql.SetIndustriesInsert, new { companyId, industryId }));
        }
    }

    public async Task<List<Industry>> GetIndustriesAsync(Guid companyId, CancellationToken ct = default)
    {
        return await context.ListOrEmpty<Industry>(new QueryObject(CompanySql.GetIndustries, new { companyId }));
    }

    public async Task<List<CompanyMember>> GetMembersAsync(Guid companyId, CancellationToken ct = default)
    {
        return await context.ListOrEmpty<CompanyMember>(new QueryObject(CompanySql.GetMembers, new { companyId }));
    }

    public async Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        return await context.ListOrEmpty<Company>(new QueryObject(CompanySql.GetChildren, new { parentId }));
    }

    public async Task<JoinCompanyOutcome> TryJoinAsync(
        Guid companyId,
        Guid userId,
        Func<Task<bool>> tryClaimInvite,
        CancellationToken ct = default)
    {
        using var transaction = context.BeginTransaction();
        if (!await transaction.CommandWithResponse<bool>(new QueryObject(CompanySql.ReserveMember,
                new { companyId, userId, joinedAt = DateTime.UtcNow })))
        {
            return JoinCompanyOutcome.AlreadyMember;
        }
        if (!await tryClaimInvite())
        {
            return JoinCompanyOutcome.InviteUnavailable;
        }
        transaction.Commit();
        return JoinCompanyOutcome.Joined;
    }
}
