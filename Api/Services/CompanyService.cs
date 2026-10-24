using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Companies;
using Api.Contracts.Industries;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Services;

public class CompanyService(ICompanyRepository companies, IIndustryRepository industries) : ICompanyService
{
    public async Task<Result<CompanyResponse>> CreateMyAsync(
        Guid userId,
        CreateCompanyRequest request,
        CancellationToken ct = default)
    {
        var created = await CreateAsync(new CreateCompanyInternalRequest
        {
            CreatorId = userId,
            Name = request.Name,
            Inn = request.Inn,
            Region = request.Region,
            Revenue = request.Revenue,
            CountOfEmployees = request.CountOfEmployees,
            IndustryCodes = request.IndustryCodes
        }, ct);
        if (created.Error is { } error)
        {
            return new(error);
        }
        return new(await ToResponseAsync(created.Content!, userId, ct), null);
    }

    public async Task<Result<CompanyResponse>> GetMyAsync(Guid userId, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        return new(await ToResponseAsync(company, userId, ct), null);
    }

    public async Task<Result<CompanyResponse>> PatchMyAsync(
        Guid userId,
        UpdateCompanyRequest patch,
        CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        if (!await companies.UpdateAsync(company.Id, patch, ct))
        {
            return new(MissingCompany());
        }
        return new(await ToResponseAsync(company, userId, ct), null);
    }

    public async Task<Result<CompanyResponse>> SetMyIndustriesAsync(
        Guid userId,
        SetIndustriesRequest request,
        CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        if ((await SetIndustriesAsync(company.Id, request.IndustryCodes, ct)).Error is { } error)
        {
            return new(error);
        }
        return new(await ToResponseAsync(company, userId, ct), null);
    }

    public async Task<Result<List<MemberResponse>>> GetMyMembersAsync(Guid userId, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        var members = await companies.GetMembersAsync(company.Id, ct);
        return new(members.Select(member => new MemberResponse
        {
            UserId = member.UserId,
            JoinedAt = member.JoinedAt,
            IsCreator = member.UserId == company.CreatorId
        }).ToList(), null);
    }

    public async Task<Result<CompanyGroupResponse>> GetMyGroupAsync(Guid userId, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        var parent = company.ParentCompanyId is { } parentId
            ? await companies.GetByIdAsync(parentId, ct)
            : null;
        var children = await companies.GetChildrenAsync(company.Id, ct);
        return new(new CompanyGroupResponse
        {
            Current = await ToResponseAsync(company, userId, ct),
            Parent = parent is null ? null : await ToResponseAsync(parent, userId, ct),
            Children = await Task.WhenAll(children.Select(child => ToResponseAsync(child, userId, ct)))
        }, null);
    }

    public async Task<Result<CreatedCompanyResponse>> CreateInternalAsync(CreateCompanyInternalRequest request, CancellationToken ct = default)
    {
        var created = await CreateAsync(request, ct);
        if (created.Error is { } error)
        {
            return new(error);
        }
        return new(new CreatedCompanyResponse { Id = created.Content!.Id, Name = created.Content.Name }, null);
    }

    public async Task<Result<CompanyMembershipResponse>> GetByUserInternalAsync(Guid userId, CancellationToken ct = default)
    {
        var company = await companies.GetByUserAsync(userId, ct);
        if (company is null)
        {
            return new(MissingMembership());
        }
        return new(new CompanyMembershipResponse { Id = company.Id, Name = company.Name, BankProfileId = company.BankProfileId }, null);
    }

    public async Task<Result<List<Guid>>> GetMemberIdsAsync(Guid companyId, CancellationToken ct = default)
    {
        if (await companies.GetByIdAsync(companyId, ct) is null)
        {
            return new(MissingCompany());
        }
        return new((await companies.GetMembersAsync(companyId, ct)).Select(member => member.UserId).ToList(), null);
    }

    private async Task<Result<Company>> CreateAsync(CreateCompanyInternalRequest request, CancellationToken ct)
    {
        var created = await companies.TryCreateAsync(new Company
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Inn = request.Inn,
            CreatorId = request.CreatorId,
            Region = request.Region,
            Revenue = request.Revenue,
            CountOfEmployees = request.CountOfEmployees,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (created is null)
        {
            return new(new Error("Пользователь уже состоит в компании", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
        }
        if (request.IndustryCodes is { Count: > 0 })
        {
            // Сохраняем прежнюю семантику создания: неизвестные коды не мешают созданию компании.
            await SetIndustriesAsync(created.Id, request.IndustryCodes, ct);
        }
        return new(created, null);
    }

    private async Task<Result> SetIndustriesAsync(
        Guid companyId,
        IReadOnlyCollection<string> codes,
        CancellationToken ct)
    {
        var found = await industries.GetByCodesAsync(codes, ct);
        if (found.Count != codes.Distinct().Count())
        {
            return new(new Error("Есть неизвестные коды отраслей", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }
        await companies.SetIndustriesAsync(companyId, found.Select(industry => industry.Id).ToList(), ct);
        return new();
    }

    private async Task<CompanyResponse> ToResponseAsync(
        Company company,
        Guid requesterId,
        CancellationToken ct)
    {
        var found = await companies.GetIndustriesAsync(company.Id, ct);
        return new CompanyResponse
        {
            Id = company.Id,
            Name = company.Name,
            Inn = company.Inn,
            CreatorId = company.CreatorId,
            ParentCompanyId = company.ParentCompanyId,
            Region = company.Region,
            Revenue = company.Revenue,
            CountOfEmployees = company.CountOfEmployees,
            BankProfileId = company.BankProfileId,
            CreatedAt = company.CreatedAt,
            IsCreator = company.CreatorId == requesterId,
            Industries = found.Select(industry => new IndustryResponse { Id = industry.Id, Code = industry.Code, Name = industry.Name }).ToList()
        };
    }

    private static Error MissingMembership()
    {
        return new("Пользователь не состоит в компании", ErrorSource.Client, ErrorType.NotFound) { HttpStatusCode = HttpStatusCode.NotFound };
    }

    private static Error MissingCompany()
    {
        return new("Компания не найдена", ErrorSource.Client, ErrorType.NotFound) { HttpStatusCode = HttpStatusCode.NotFound };
    }
}
