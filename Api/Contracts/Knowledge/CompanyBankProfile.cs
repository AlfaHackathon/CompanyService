using System.Text.Json.Serialization;
using Api.Contracts.Companies;
using Api.Contracts.PublicKnowledge;

namespace Api.Contracts.Knowledge;

public sealed record CompanyBankProfile(Guid CompanyId, string RevisionId, DateTimeOffset KnownAt,
    CompanyBankData BankData, string SourceId, SourceReference[] SourceRefs, MaterialProvenance Provenance);
public sealed record CompanyBankSnapshot(Guid CompanyId, CompanyBankData BankData);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CaptureCompanyBankProfileRequest(string CaptureId, CompanyBankData BankData);
public sealed record CompanyBankProfilePage(CompanyBankProfile[] Profiles, string? NextCursor);
