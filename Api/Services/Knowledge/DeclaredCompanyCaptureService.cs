using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Services.PublicKnowledge;

namespace Api.Services;

// Uses the existing operation journal to freeze one company's capture before external writes.
public sealed class DeclaredCompanyCaptureService(ICompanyRepository companies, IPublicCompanyProfiles profiles,
    ICompanyMaterialsClient materials, IKnowledgeOperationStore operations)
{
    public const string SourceId = "company-operational-declared-profile";
    private const string Operation = "declared-profile-capture";
    private sealed record FrozenCapture(CompanyMaterialWrite Material, CompanyProfile Profile, string? BaseRevisionId);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<Result<CompanyProfile>> Capture(Guid companyId, CaptureDeclaredProfileRequest request, CancellationToken ct)
    {
        try
        {
            if (companyId == Guid.Empty || request is null || request.CaptureId is null ||
                request.CaptureId.Length is < 1 or > 128 || request.CaptureId.Any(c => c < '!' || c > '~'))
                throw PublicKnowledgeError.Invalid();
            var requestHash = Hash(Json(new { companyId, request.CaptureId }));
            // A retry returns or resumes its frozen record, even if current operational fields changed.
            var operation = await operations.Find(Guid.Empty, companyId, Operation, request.CaptureId, requestHash);
            if (operation is null)
            {
                var company = await companies.GetByIdAsync(companyId, ct) ?? throw PublicKnowledgeError.Missing();
                if (company.Id != companyId || string.IsNullOrWhiteSpace(company.Name) || company.Name.Length > 1000 ||
                    company.Region?.Length > 128 || company.CountOfEmployees < 0 || company.Revenue < 0)
                    throw PublicKnowledgeError.Invalid();
                var industries = await companies.GetIndustriesAsync(companyId, ct);
                if (industries.Any(i => i.Code is null || i.Code.Length != 1 || i.Code[0] is < 'A' or > 'U'))
                    throw PublicKnowledgeError.Invalid();
                CompanyProfile? previous = null;
                try { previous = await profiles.Get(companyId, DateTimeOffset.UtcNow); }
                catch (PublicKnowledgeError error) when (error.Status == HttpStatusCode.NotFound) { }
                if (previous is not null && (previous.CompanyId != companyId || previous.KnownAt > DateTimeOffset.UtcNow))
                    throw PublicKnowledgeError.Unavailable();
                var snapshot = new DeclaredCompanySnapshot(companyId, company.Region,
                    industries.Count == 0 ? null : industries.Select(i => i.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    company.Revenue is { } revenue ? new(revenue.ToString(CultureInfo.InvariantCulture), null, null) : null,
                    company.CountOfEmployees);
                var identity = Hash(companyId.ToString("D") + ":" + request.CaptureId);
                operation = await operations.Reserve(Guid.Empty, companyId, Operation, request.CaptureId, requestHash, time =>
                {
                    var material = new CompanyMaterialWrite("declared-company-" + companyId.ToString("D"), "capture-" + identity,
                        SourceId, "company", "Declared operational company profile", Json(snapshot), time, null, null,
                        [companyId], [], new("event_payload", null, time, "full"));
                    var reference = new SourceReference { MaterialId = material.MaterialId, VersionId = material.VersionId, ContentHash = Hash(Json(material)) };
                    var profile = new CompanyProfile
                    {
                        CompanyId = companyId,
                        RevisionId = "declared-capture-" + identity,
                        BankProfileId = previous?.BankProfileId ?? company.BankProfileId,
                        Name = previous?.Name ?? company.Name,
                        Description = previous?.Description ?? "Operational declared company profile; no documented financial exposure established",
                        IndustryCodes = previous?.IndustryCodes ?? [],
                        Exposures = previous?.Exposures ?? [],
                        KnownAt = time,
                        SourceRefs = (previous?.SourceRefs ?? []).Where(r => previous?.DeclaredProfile?.SourceRefs.Any(old =>
                                old.MaterialId == r.MaterialId && old.VersionId == r.VersionId && old.ContentHash == r.ContentHash) != true).Concat([reference])
                            .DistinctBy(r => (r.MaterialId, r.VersionId, r.ContentHash)).ToArray(),
                        DeclaredProfile = new(snapshot.Region, snapshot.IndustrySections, snapshot.Revenue, snapshot.CountOfEmployees, [reference])
                    };
                    if (profile.SourceRefs.Length > 64) throw PublicKnowledgeError.Invalid();
                    return Json(new FrozenCapture(material, profile, previous?.RevisionId));
                });
            }
            var frozen = JsonSerializer.Deserialize<FrozenCapture>(operation.Payload, KnowledgeJson.Options) ?? throw PublicKnowledgeError.Unavailable();
            if (frozen.Profile.CompanyId != companyId || frozen.Material.KnownAt != operation.KnownAt || operation.KnownAt > DateTimeOffset.UtcNow)
                throw PublicKnowledgeError.Unavailable();
            var saved = operation.Completed
                ? await materials.Get(frozen.Material.MaterialId, frozen.Material.VersionId, ct)
                : await materials.Save(frozen.Material, ct);
            var actual = new CompanyMaterialWrite(saved.MaterialId, saved.VersionId, saved.SourceId, saved.Kind, saved.Title, saved.Text,
                saved.KnownAt, saved.PublishedAt, saved.CorrectsVersionId, saved.CompanyIds, saved.BankProfileIds, saved.Provenance);
            if (Json(actual) != Json(frozen.Material) || saved.ContentHash != Hash(Json(frozen.Material)))
                throw PublicKnowledgeError.Unavailable();
            if (!operation.Completed)
            {
                var result = await profiles.SaveIfCurrent(frozen.Profile, frozen.BaseRevisionId);
                if (Json(result) != Json(frozen.Profile)) throw PublicKnowledgeError.Unavailable();
                await operations.Complete(operation);
            }
            return new(frozen.Profile, null);
        }
        catch (PublicKnowledgeError error)
        {
            return new(new Error(error.Message, ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = error.Status });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(new Error("Declared profile capture was not confirmed. Retry the same captureId; do not assume a fresh snapshot.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
    }
}
