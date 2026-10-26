using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Common;
using Api.Contracts.Companies;
using Api.Contracts.Knowledge;

namespace Api.Services;

public static class CompanyBankProfilePolicy
{
    public const string SourceId = "company-bank-profile";
    public static bool Valid(CompanyBankProfile profile) => profile.CompanyId != Guid.Empty &&
        IdentifierValidation.RequireId(profile.RevisionId, "revisionId").Error is null && profile.KnownAt != default && CompanyBankDataValidation.Valid(profile.BankData) &&
        profile.SourceId == SourceId && profile.Provenance is { Acquisition: "event_payload", SourceUrl: null, TextCompleteness: "full", OriginVersionId: null, Locator: null } &&
        profile.Provenance.RetrievedAt == profile.KnownAt && profile.SourceRefs is { Length: 1 } && profile.SourceRefs.All(r => r is not null &&
            IdentifierValidation.RequireId(r.MaterialId, "materialId").Error is null && IdentifierValidation.RequireId(r.VersionId, "versionId").Error is null &&
            r.ContentHash is not null && Regex.IsMatch(r.ContentHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant));
    public static string Snapshot(CompanyBankProfile profile) => JsonSerializer.Serialize(new CompanyBankSnapshot(profile.CompanyId, profile.BankData), KnowledgeJson.Options);
}
