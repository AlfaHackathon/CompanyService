using System.Net;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Knowledge;

namespace Api.Services;

public sealed class CompanySelectionService(KnowledgeService knowledge, ContextKnowledgeClient context)
{
    public async Task<Result<CompanySelection>> Select(CompanySelectionRequest request, CancellationToken ct)
    {
        if (request.AsOf == default || request.TargetCompanyIds is null || request.GroupIds is null ||
            request.PersonIds is null || request.Queries is null || request.PreviousCompanyIds is null ||
            request.TargetCompanyIds.Length > 256 || request.GroupIds.Length > 64 || request.PersonIds.Length > 64 ||
            request.PreviousCompanyIds.Length > 10000 || request.CompanyBudget is < 1 or > 64 ||
            request.Queries.Length > 32 || request.Queries.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 16000) ||
            request.TargetCompanyIds.Concat(request.PreviousCompanyIds).Contains(Guid.Empty) ||
            request.GroupIds.Concat(request.PersonIds).Any(id => Common.IdentifierValidation.RequireId(id, "id").Error is not null))
        {
            return new(Invalid("A bounded selection with asOf, valid IDs and companyBudget 1–64 is required."));
        }

        var ranks = new Dictionary<Guid, double>();
        if (request.TargetCompanyIds.Length == 0 && !request.ProjectionOnly)
        {
            foreach (var query in request.Queries)
            {
                var found = await context.Search(query, request.AsOf, ct);
                if (found.Error is { } error) return new(error);
                if (found.Content?.Hits is not { } hits) return new(Invalid("Retrieval did not return hits."));
                for (var rank = 0; rank < hits.Length; rank++)
                {
                    var id = hits[rank].CompanyId;
                    if (id != Guid.Empty) ranks[id] = ranks.GetValueOrDefault(id) + 1.0 / (60 + rank + 1);
                }
            }
        }

        var profiles = await knowledge.Groups(request.AsOf);
        var groupIds = request.GroupIds.Length > 0 ? request.GroupIds : profiles.Select(g => g.GroupId).ToArray();
        var groups = new List<GroupExpansion>();
        foreach (var id in groupIds.Distinct(StringComparer.Ordinal))
        {
            var expanded = GroupExpansionService.Expand(id, profiles);
            if (expanded.Error is { } error) return new(error);
            var group = expanded.Content!;
            if (request.GroupIds.Contains(id, StringComparer.Ordinal) || group.CompanyIds.Any(ranks.ContainsKey)) groups.Add(group);
        }

        var companies = new List<Guid>();
        if (request.TargetCompanyIds.Length > 0) companies.AddRange(request.TargetCompanyIds);
        else
        {
            companies.AddRange(groups.SelectMany(g => g.CompanyIds));
            if (request.PersonIds.Length > 0)
            {
                companies.AddRange((await knowledge.Roles(request.AsOf))
                    .Where(r => request.PersonIds.Contains(r.PersonId, StringComparer.Ordinal)).SelectMany(r => r.AllowedCompanyIds));
            }
            companies.AddRange(ranks.OrderByDescending(pair => pair.Value).Select(pair => pair.Key));
            companies.AddRange(request.PreviousCompanyIds);
        }

        var unique = companies.Distinct().ToArray();
        return new(new CompanySelection(groups.ToArray(), unique.Take(request.CompanyBudget).ToArray(), unique.Skip(request.CompanyBudget).ToArray()), null);
    }

    public async Task<Result<RoleScope>> Scope(RoleScopeRequest request)
    {
        if (request.AsOf == default || request.PersonIds is null || request.CompanyIds is null ||
            request.PersonIds.Length > 64 || request.CompanyIds.Length > 10000 || request.CompanyIds.Contains(Guid.Empty) ||
            request.PersonIds.Any(id => Common.IdentifierValidation.RequireId(id, "personId").Error is not null))
            return new(Invalid("A bounded role scope with asOf and explicit person/company IDs is required."));

        var roles = (await knowledge.Roles(request.AsOf)).Where(r => request.PersonIds.Length > 0
            ? request.PersonIds.Contains(r.PersonId, StringComparer.Ordinal)
            : r.AllowedCompanyIds.Any(request.CompanyIds.Contains)).ToArray();
        var missing = request.PersonIds.Where(id => !roles.Any(r => r.PersonId == id)).Distinct(StringComparer.Ordinal).ToArray();
        // Company links and group membership never create a read grant.
        return new(new RoleScope(roles, missing, request.CompanyIds.Concat(roles.SelectMany(r => r.AllowedCompanyIds)).Distinct().ToArray()), null);
    }

    public async Task<Result<CompanySegment[]>> Segments(SegmentRequest request)
    {
        if (request.AsOf == default || request.CompanyIds is null || request.CompanyIds.Length > 10000 ||
            request.CompanyIds.Contains(Guid.Empty) || request.Granularity is not ("activity" or "broad"))
            return new(Invalid("Segment scope requires asOf, valid company IDs and activity/broad granularity."));

        var profiles = (await knowledge.Companies(request.AsOf)).Where(p => request.CompanyIds.Contains(p.CompanyId));
        var rows = profiles.SelectMany(p => p.IndustryCodes.Where(c => c is not null && Regex.IsMatch(c, @"^\d{2}(?:\.\d{1,2}){0,2}$"))
            .Select(c => request.Granularity == "broad" ? c[..2] : c).Distinct()
            .Select(code => new { Key = (request.Granularity == "broad" ? "okved2:" : "activity:") + code, Profile = p }));
        return new(rows.GroupBy(row => row.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CompanySegment(g.Key,
                g.Select(row => row.Profile.Description.Split(';')[0]).Distinct().Order(StringComparer.Ordinal).ToArray(),
                g.Select(row => row.Profile.CompanyId).Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray(),
                g.SelectMany(row => row.Profile.SourceRefs).Distinct().ToArray())).ToArray(), null);
    }

    private static Error Invalid(string message) => new(message, ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest };
}
