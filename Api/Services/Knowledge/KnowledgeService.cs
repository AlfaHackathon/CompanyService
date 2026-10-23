using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Common;
using Api.Contracts.Changes;
using Api.Contracts.Knowledge;
using Api.Repositories;

namespace Api.Services;

public sealed class KnowledgeService(
    KnowledgeStore store,
    ContextKnowledgeClient materials,
    KnowledgeChanges changes)
{
    public async Task<Result<CompanyProfile>> SaveCompanyAsync(CompanyProfile p)
    {
        if (p.CompanyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(p.Name) ||
            string.IsNullOrWhiteSpace(p.Description) ||
            p.IndustryCodes is null ||
            p.Exposures is null ||
            p.Description.Length > 16000)
        {
            return new(new Error("Company identity, name, bounded description and feature arrays are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await ValidateAsync(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId)).Error is { } companyEvidenceError)
        {
            return new(companyEvidenceError);
        }
        p.KnownAt = p.KnownAt.ToUniversalTime();
        return await store.Save("company", p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt, p);
    }

    public async Task<Result<GroupProfile>> SaveGroupAsync(GroupProfile p)
    {
        if (IdentifierValidation.RequireId(p.GroupId, "groupId").Error is { } groupIdError)
        {
            return new(groupIdError);
        }
        if (string.IsNullOrWhiteSpace(p.Name) ||
            p.Members is null ||
            p.Members.Length > 512)
        {
            return new(new Error("A group requires a name and at most 512 explicit members.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await ValidateAsync(p.RevisionId, p.KnownAt, p.SourceRefs)).Error is { } groupEvidenceError)
        {
            return new(groupEvidenceError);
        }
        if (p.AggregationRule is not (null or "sum_compatible_owner_effects"))
        {
            return new(new Error("Only an explicitly sourced sum_compatible_owner_effects rule is supported.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        var keys = new HashSet<string>();
        foreach (var member in p.Members)
        {
            if (member is null ||
                member.Kind is not ("company" or "group"))
            {
                return new(new Error("Each member must identify a company or a nested group.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if (IdentifierValidation.RequireId(member.Id, "member id").Error is { } memberIdError)
            {
                return new(memberIdError);
            }
            if (member.Kind == "company" &&
                (!Guid.TryParse(member.Id, out var id) ||
                    id == Guid.Empty))
            {
                return new(new Error("Company members use application Guids.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if (!keys.Add(member.Kind + ":" + member.Id.ToLowerInvariant()))
            {
                return new(new Error("A direct group edge may occur only once.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if ((await ValidateAsync(p.RevisionId, p.KnownAt, member.SourceRefs)).Error is { } memberEvidenceError)
            {
                return new(memberEvidenceError);
            }
        }

        p.KnownAt = p.KnownAt.ToUniversalTime();
        return await store.Save("group", p.GroupId, p.RevisionId, p.KnownAt, p);
    }

    public async Task<Result<RoleProfile>> SaveRoleAsync(RoleProfile p)
    {
        if (IdentifierValidation.RequireId(p.PersonId, "personId").Error is { } personIdError)
        {
            return new(personIdError);
        }
        if (p.CompanyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(p.Role) ||
            p.Responsibilities is null ||
            p.AllowedCompanyIds is null ||
            p.AllowedMaterialIds is null ||
            !p.AllowedCompanyIds.Contains(p.CompanyId) ||
            p.AllowedCompanyIds.Contains(Guid.Empty) ||
            p.AllowedCompanyIds.Length > 512 ||
            p.AllowedMaterialIds.Length > 2048)
        {
            return new(new Error("An explicit role, responsibilities and bounded read grant including its company are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        foreach (var id in p.AllowedMaterialIds)
        {
            if (IdentifierValidation.RequireId(id, "allowedMaterialId").Error is { } allowedMaterialIdError)
            {
                return new(allowedMaterialIdError);
            }
        }

        if ((await ValidateAsync(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId)).Error is { } bankEvidenceError)
        {
            return new(bankEvidenceError);
        }
        p.KnownAt = p.KnownAt.ToUniversalTime();
        return await store.Save("role", p.PersonId + ":" + p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt, p);
    }

    public async Task<Result<PersonProfile>> SavePersonAsync(PersonProfile p)
    {
        if (IdentifierValidation.RequireId(p.PersonId, "personId").Error is { } personIdError2)
        {
            return new(personIdError2);
        }
        if (p.CompanyLinks is null ||
            p.CompanyLinks.Length > 512)
        {
            return new(new Error("A person requires an explicit bounded links array.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await ValidateAsync(p.RevisionId, p.KnownAt, p.SourceRefs)).Error is { } personEvidenceError)
        {
            return new(personEvidenceError);
        }
        foreach (var link in p.CompanyLinks)
        {
            if (link is null || link.CompanyId == Guid.Empty ||
                link.RelationKind is not ("unspecified" or "employee" or "representative" or "owner"))
            {
                return new(new Error("A link requires an application company ID and an explicit relation kind.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if ((await ValidateAsync(p.RevisionId, p.KnownAt, link.SourceRefs, link.CompanyId)).Error is { } linkEvidenceError)
            {
                return new(linkEvidenceError);
            }
        }

        p.KnownAt = p.KnownAt.ToUniversalTime();
        return await store.Save("person", p.PersonId, p.RevisionId, p.KnownAt, p);
    }

    public Task<PersonProfile[]> PeopleAsync(DateTimeOffset asOf)
    {
        return store.List<PersonProfile>("person", asOf);
    }

    public Task<CompanyProfile[]> CompaniesAsync(DateTimeOffset asOf)
    {
        return store.List<CompanyProfile>("company", asOf);
    }

    public Task<CompanyProfile[]> CompanyVersionsAsync()
    {
        return store.List<CompanyProfile>("company", DateTimeOffset.MaxValue, false);
    }

    public Task<RoleProfile[]> RolesAsync(DateTimeOffset asOf)
    {
        return store.List<RoleProfile>("role", asOf);
    }

    public Task<GroupProfile[]> GroupsAsync(DateTimeOffset asOf)
    {
        return store.List<GroupProfile>("group", asOf);
    }

    private async Task<Result> ValidateAsync(
        string revision,
        DateTimeOffset knownAt,
        SourceReference[] refs,
        Guid? company = null)
    {
        if (IdentifierValidation.RequireId(revision, "revisionId").Error is { } revisionIdError)
        {
            return new(revisionIdError);
        }
        if (knownAt == default ||
            refs is null ||
            refs.Length is < 1 or > 64)
        {
            return new(new Error("Knowledge time and 1–64 immutable source references are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        foreach (var r in refs)
        {
            if (r is null)
            {
                return new(new Error("Source references cannot contain null.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if (IdentifierValidation.RequireId(r.MaterialId, "materialId").Error is { } materialIdError)
            {
                return new(materialIdError);
            }
            if (IdentifierValidation.RequireId(r.VersionId, "versionId").Error is { } versionIdError)
            {
                return new(versionIdError);
            }

            var materialIdResolution = await materials.MaterialAsync(r.MaterialId, r.VersionId);
            if (materialIdResolution.Error is { } materialIdResolutionError)
            {
                return new(materialIdResolutionError);
            }

            var source = materialIdResolution.Content!;
            if (source.ContentHash != r.ContentHash ||
                source.KnownAt > knownAt)
            {
                return new(new Error("Evidence must match its original hash and be known at the profile revision.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
            }

            if (company.HasValue &&
                !source.CompanyIds.Contains(company.Value))
            {
                return new(new Error("Company and role evidence must explicitly belong to the target company.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
            }
        }

        return new();
    }

    public async Task<Result<GroupExpansion>> ExpandAsync(string id, DateTimeOffset asOf)
    {
        return GroupExpansionService.Expand(id, await GroupsAsync(asOf));
    }

    public async Task<Result<CompanyProfilesResponse>> GetCompaniesAsync(DateTimeOffset asOf)
    {
        return new(new CompanyProfilesResponse { Companies = await CompaniesAsync(asOf) }, null);
    }

    public async Task<Result<CompanyProfilesResponse>> GetCompanyVersionsAsync()
    {
        return new(new CompanyProfilesResponse { Companies = await CompanyVersionsAsync() }, null);
    }

    public async Task<Result<CompanyProfile>> GetCompanyAsync(Guid companyId, DateTimeOffset asOf)
    {
        var company = (await CompaniesAsync(asOf)).SingleOrDefault(p => p.CompanyId == companyId);
        return company is null
            ? new(Missing("Company profile is unavailable at asOf."))
            : new(company, null);
    }

    public async Task<Result<GroupProfilesResponse>> GetGroupsAsync(DateTimeOffset asOf)
    {
        return new(new GroupProfilesResponse { Groups = await GroupsAsync(asOf) }, null);
    }

    public async Task<Result<RoleProfilesResponse>> GetRolesAsync(DateTimeOffset asOf)
    {
        return new(new RoleProfilesResponse { Roles = await RolesAsync(asOf) }, null);
    }

    public async Task<Result<RoleProfile>> GetRoleAsync(
        string personId,
        Guid companyId,
        DateTimeOffset asOf)
    {
        var role = (await RolesAsync(asOf)).SingleOrDefault(r => r.PersonId == personId && r.CompanyId == companyId);
        return role is null
            ? new(Missing("Role profile is unavailable at asOf."))
            : new(role, null);
    }

    public async Task<Result<PersonProfilesResponse>> GetPeopleAsync(DateTimeOffset asOf)
    {
        return new(new PersonProfilesResponse { People = await PeopleAsync(asOf) }, null);
    }

    public async Task<Result<PersonProfile>> GetPersonAsync(string personId, DateTimeOffset asOf)
    {
        var person = (await PeopleAsync(asOf)).SingleOrDefault(p => p.PersonId == personId);
        return person is null
            ? new(Missing("Person profile is unavailable at asOf."))
            : new(person, null);
    }

    private static Error Missing(string message)
    {
        return new(message, ErrorSource.Client, ErrorType.NotFound) { HttpStatusCode = HttpStatusCode.NotFound };
    }

    public Task<Result<KnowledgeChangesResponse>> GetChangesAsync(long after)
    {
        return changes.GetChangesAsync(after);
    }
}
