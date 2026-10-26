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
using Api.Services.PublicKnowledge;

namespace Api.Services;

// Canonical source data is independent of operational registration and grants no membership.
public sealed class CompanyBankProfileService(ICompanyBankProfiles profiles, ICompanyMaterialsClient materials,
    IKnowledgeOperationStore operations)
{
    private const string Operation = "company-bank-profile";
    private sealed record FrozenCapture(CompanyMaterialWrite Material, CompanyBankProfile Profile, string? ExpectedRevisionId);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public Task<Result<CompanyBankProfile>> Capture(Guid companyId, CaptureCompanyBankProfileRequest request, CancellationToken ct) => Run(async () =>
    {
        if (companyId == Guid.Empty || request is null || request.CaptureId is null || request.CaptureId.Length is < 1 or > 128 ||
            request.CaptureId.Any(c => c < '!' || c > '~') || !CompanyBankDataValidation.Valid(request.BankData)) throw PublicKnowledgeError.Invalid();
        var requestHash = Hash(Json(request.BankData));
        var operation = await operations.Find(Guid.Empty, companyId, Operation, request.CaptureId, requestHash);
        if (operation is null)
        {
            var previous = await profiles.Get(companyId, DateTimeOffset.UtcNow);
            var identity = Hash(companyId.ToString("D") + ":" + request.CaptureId);
            operation = await operations.Reserve(Guid.Empty, companyId, Operation, request.CaptureId, requestHash, time =>
            {
                var material = new CompanyMaterialWrite("company-bank-" + companyId.ToString("D"), "capture-" + identity,
                    CompanyBankProfilePolicy.SourceId, "company", "Declared canonical company bank profile",
                    Json(new CompanyBankSnapshot(companyId, request.BankData)), time, null, null, [companyId], [], new("event_payload", null, time, "full"));
                if (material.Text.Length > 200000 || Encoding.UTF8.GetByteCount(Json(material)) > 1024 * 1024) throw PublicKnowledgeError.Invalid();
                var profile = new CompanyBankProfile(companyId, "bank-capture-" + identity, time, request.BankData, CompanyBankProfilePolicy.SourceId,
                    [new() { MaterialId = material.MaterialId, VersionId = material.VersionId, ContentHash = Hash(Json(material)) }], material.Provenance!);
                return Json(new FrozenCapture(material, profile, previous?.RevisionId));
            });
        }
        var frozen = JsonSerializer.Deserialize<FrozenCapture>(operation.Payload, KnowledgeJson.Options) ?? throw PublicKnowledgeError.Unavailable();
        if (frozen.Profile.CompanyId != companyId || !CompanyBankProfilePolicy.Valid(frozen.Profile) || frozen.Profile.KnownAt != operation.KnownAt ||
            operation.KnownAt > DateTimeOffset.UtcNow) throw PublicKnowledgeError.Unavailable();
        var saved = operation.Completed ? await materials.Get(frozen.Material.MaterialId, frozen.Material.VersionId, ct) : await materials.Save(frozen.Material, ct);
        var actual = new CompanyMaterialWrite(saved.MaterialId, saved.VersionId, saved.SourceId, saved.Kind, saved.Title, saved.Text, saved.KnownAt,
            saved.PublishedAt, saved.CorrectsVersionId, saved.CompanyIds, saved.BankProfileIds, saved.Provenance);
        if (Json(actual) != Json(frozen.Material) || saved.ContentHash != Hash(Json(frozen.Material))) throw PublicKnowledgeError.Unavailable();
        if (!operation.Completed)
        {
            var result = await profiles.Save(frozen.Profile, frozen.ExpectedRevisionId);
            if (Json(result) != Json(frozen.Profile)) throw PublicKnowledgeError.Unavailable();
            await operations.Complete(operation);
        }
        return frozen.Profile;
    });

    public Task<Result<CompanyBankProfile>> Get(Guid companyId, DateTimeOffset asOf) => Run(async () =>
    {
        if (companyId == Guid.Empty || asOf == default) throw PublicKnowledgeError.Invalid();
        var profile = await profiles.Get(companyId, asOf) ?? throw PublicKnowledgeError.Missing();
        if (profile.CompanyId != companyId || profile.KnownAt > asOf || !CompanyBankProfilePolicy.Valid(profile)) throw PublicKnowledgeError.Unavailable();
        return profile;
    });

    public Task<Result<CompanyBankProfilePage>> Versions(string? after, int limit) => Run(async () =>
    {
        if (limit is < 1 or > 100 || after is not null && (after.Length is < 1 or > 19 || after.Any(c => c is < '0' or > '9'))) throw PublicKnowledgeError.Invalid();
        var sequence = 0L;
        if (after is not null && !long.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out sequence)) throw PublicKnowledgeError.Invalid();
        var page = await profiles.Versions(sequence, limit);
        if (page.Profiles.Length > limit || page.Profiles.Any(p => !CompanyBankProfilePolicy.Valid(p))) throw PublicKnowledgeError.Unavailable();
        return page;
    });

    private static async Task<Result<T>> Run<T>(Func<Task<T>> action)
    {
        try { return new(await action(), null); }
        catch (PublicKnowledgeError error) { return new(new Error(error.Message, ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = error.Status }); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(new Error("Canonical company profile was not confirmed. Preserve unknowns; after an uncertain capture outcome retry the same captureId.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
    }
}
