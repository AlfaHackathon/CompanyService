using Api.Contracts.Knowledge;

namespace Api.Contracts.PublicKnowledge;

// Explicit public projections: operational grants and internal payloads are not forwarded.
public sealed record CompanyKnowledgeResponse(Guid CompanyId, string RevisionId, string Name, string Description,
    string[] IndustryCodes, string[] Exposures, DateTimeOffset KnownAt, SourceReference[] SourceRefs);
public sealed record MaterialProvenance(string Acquisition, string? SourceUrl, DateTimeOffset RetrievedAt,
    string TextCompleteness, string? OriginVersionId = null, string? Locator = null);
public sealed record CompanyMaterialMetadata(string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt, string? CorrectsVersionId,
    string ContentHash, string TextCompleteness, MaterialProvenance? Provenance, int LatestVersionCount);
public sealed record CompanyMaterialsResponse(Guid CompanyId, DateTimeOffset AsOf, CompanyMaterialMetadata[] Items, string? NextCursor,
    string Coverage);
public sealed record CompanyMaterialResponse(string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, string Text, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt, string? CorrectsVersionId,
    string ContentHash, string TextCompleteness, MaterialProvenance? Provenance);
