using Api.Contracts.Companies;
using Api.Contracts.Industries;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Exceptions;
using Api.Services.Interfaces;

namespace Api.Services;

/// <summary>Бизнес-операции над компаниями.</summary>
public class CompanyService(
    ICompanyRepository companies,
    IIndustryRepository industries) : ICompanyService
{
    public Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default)
        => companies.GetByUserAsync(userId, ct);

    public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => companies.GetByIdAsync(id, ct);

    public async Task<CompanyResponse> ToResponseAsync(Company company, Guid requesterId, CancellationToken ct = default)
    {
        var inds = await companies.GetIndustriesAsync(company.Id, ct);
        return new CompanyResponse(
            company.Id, company.Name, company.Inn, company.CreatorId, company.ParentCompanyId,
            company.Region, company.Revenue, company.CountOfEmployees, company.BankProfileId,
            company.CreatedAt,
            IsCreator: company.CreatorId == requesterId,
            Industries: inds.Select(i => new IndustryResponse(i.Id, i.Code, i.Name)).ToList());
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default)
        => await companies.UpdateAsync(id, patch, ct);

    public async Task<(bool Ok, List<string> ValidCodes)> SetIndustriesByCodesAsync(
        Guid companyId, IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        var found = await industries.GetByCodesAsync(codes, ct);
        if (found.Count != codes.Distinct().Count())
        {
            return (false, found.Select(i => i.Code).ToList());
        }

        await companies.SetIndustriesAsync(companyId, found.Select(i => i.Id).ToList(), ct);
        return (true, []);
    }

    public async Task<List<MemberResponse>> GetMembersAsync(Guid companyId, CancellationToken ct = default)
    {
        var members = await companies.GetMembersAsync(companyId, ct);
        var creatorId = (await companies.GetByIdAsync(companyId, ct))?.CreatorId;
        return members
            .Select(m => new MemberResponse(m.UserId, m.JoinedAt, m.UserId == creatorId))
            .ToList();
    }

    public async Task<List<Guid>> GetMemberIdsAsync(Guid companyId, CancellationToken ct = default)
    {
        var members = await companies.GetMembersAsync(companyId, ct);
        return members.Select(m => m.UserId).ToList();
    }

    public Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
        => companies.GetChildrenAsync(parentId, ct);

    public async Task<Company> CreateInternalAsync(CreateCompanyInternalRequest request, CancellationToken ct = default)
    {
        // У юзера не больше одной компании: если уже состоит — не создаём
        var existing = await companies.GetByUserAsync(request.CreatorId, ct);
        if (existing is not null)
        {
            throw new UserAlreadyHasCompanyException(existing.Id);
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Inn = request.Inn,
            CreatorId = request.CreatorId,
            Region = request.Region,
            Revenue = request.Revenue,
            CountOfEmployees = request.CountOfEmployees,
            CreatedAt = DateTime.UtcNow
        };
        var created = await companies.CreateAsync(company, ct);

        // Создатель сразу становится участником, иначе компании/me отдаст 404
        await companies.AddMemberAsync(created.Id, request.CreatorId, ct);

        if (request.IndustryCodes is { Count: > 0 })
        {
            var (ok, _) = await SetIndustriesByCodesAsync(created.Id, request.IndustryCodes, ct);
        }

        return created;
    }

    public Task AddMemberAsync(Guid companyId, Guid userId, CancellationToken ct = default)
        => companies.AddMemberAsync(companyId, userId, ct);
}
