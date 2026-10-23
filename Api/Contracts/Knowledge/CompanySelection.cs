namespace Api.Contracts.Knowledge;

public sealed record CompanySelectionRequest(DateTimeOffset AsOf, Guid[] TargetCompanyIds,
    string[] GroupIds, string[] PersonIds, string[] Queries, Guid[] PreviousCompanyIds,
    int CompanyBudget = 12, bool ProjectionOnly = false);
public sealed record CompanySelection(GroupExpansion[] Groups, Guid[] SelectedCompanyIds, Guid[] PendingCompanyIds);
public sealed record RoleScopeRequest(DateTimeOffset AsOf, string[] PersonIds, Guid[] CompanyIds);
public sealed record RoleScope(RoleProfile[] Roles, string[] MissingPersonIds, Guid[] CompanyIds);
public sealed record SegmentRequest(DateTimeOffset AsOf, Guid[] CompanyIds, string Granularity = "activity");
public sealed record CompanySegment(string Key, string[] Labels, Guid[] CompanyIds, SourceReference[] ProfileRefs);
