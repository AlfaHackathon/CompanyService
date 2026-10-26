using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

// Operational declarations describe relevance, not documented financial exposure.
public sealed record DeclaredRevenue(string Amount, string? Currency, string? Period);
public sealed record DeclaredCompanyProfile(string? Region, string[]? IndustrySections,
    DeclaredRevenue? Revenue, int? CountOfEmployees, SourceReference[] SourceRefs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CaptureDeclaredProfileRequest(string CaptureId);
public sealed record DeclaredCompanySnapshot(Guid CompanyId, string? Region, string[]? IndustrySections,
    DeclaredRevenue? Revenue, int? CountOfEmployees);
