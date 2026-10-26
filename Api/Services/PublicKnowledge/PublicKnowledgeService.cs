using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Contracts.PublicKnowledge;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;

namespace Api.Services.PublicKnowledge;

public sealed class PublicKnowledgeService(ICompanyRepository companies, IPublicCompanyProfiles profiles,
    ICompanyMaterialsClient materials, IKnowledgeOperationStore operations)
{
    private sealed record MaterialCursor(Guid CompanyId, string? BankProfileId, DateTimeOffset AsOf, string Inner);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, KnowledgeJson.Options) ?? throw PublicKnowledgeError.Unavailable();
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Json(value))));
    private static void Id(string? value)
    {
        if (IdentifierValidation.RequireId(value, "material/version identifier").Error is not null) throw PublicKnowledgeError.Invalid();
    }
    private static void Key(string? key)
    {
        if (key is null || key.Length is < 1 or > 128 || key.Any(c => c < '!' || c > '~')) throw PublicKnowledgeError.Invalid();
    }
    private async Task<Company> Scope(Guid actor, bool write, CancellationToken ct)
    {
        var company = await companies.GetByUserAsync(actor, ct) ?? throw PublicKnowledgeError.Missing();
        if (write && company.CreatorId != actor)
            throw new PublicKnowledgeError(HttpStatusCode.Forbidden, "Only the current company creator may change company knowledge or upload declarations.");
        return company;
    }
    private async Task Recheck(Guid actor, Company expected, CancellationToken ct)
    {
        var current = await Scope(actor, true, ct);
        if (current.Id != expected.Id || current.BankProfileId != expected.BankProfileId)
            throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Current company membership changed; start a new request.");
    }
    private static void MaterialScope(ScopedCompanyMaterial source, Company company, string material, string version, DateTimeOffset asOf, string? hash = null)
    {
        // Scope rejection precedes public projection, including for previously saved internal profiles.
        if (source.Kind != "company" || source.CompanyIds is null || !source.CompanyIds.Contains(company.Id) ||
            source.BankProfileIds is null || source.BankProfileIds.Length > 0 &&
            (company.BankProfileId is null || !source.BankProfileIds.Contains(company.BankProfileId))) throw PublicKnowledgeError.Missing();
        if (source.MaterialId != material || source.VersionId != version || source.KnownAt == default || source.KnownAt > asOf ||
            string.IsNullOrWhiteSpace(source.Title) || source.Title.Length > 1000 || source.Text is null || source.Text.Length > 200000 ||
            source.ContentHash is null || !Regex.IsMatch(source.ContentHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant) ||
            hash is not null && source.ContentHash != hash) throw PublicKnowledgeError.Unavailable();
    }
    private async Task ValidateProfile(CompanyProfile profile, Company company, CancellationToken ct)
    {
        if (profile.CompanyId != company.Id || profile.KnownAt == default || profile.KnownAt > DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 1000 ||
            string.IsNullOrWhiteSpace(profile.Description) || profile.Description.Length > 16000 ||
            profile.IndustryCodes is null || profile.IndustryCodes.Length > 64 || profile.Exposures is null || profile.Exposures.Length > 64 ||
            profile.IndustryCodes.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 100) ||
            profile.Exposures.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 1000) ||
            profile.SourceRefs is null || profile.SourceRefs.Length is < 1 or > 64) throw PublicKnowledgeError.Unavailable();
        foreach (var reference in profile.SourceRefs)
        {
            if (reference is null || reference.ContentHash is null || !Regex.IsMatch(reference.ContentHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)) throw PublicKnowledgeError.Unavailable();
            Id(reference.MaterialId); Id(reference.VersionId);
            var source = await materials.Get(reference.MaterialId, reference.VersionId, ct);
            MaterialScope(source, company, reference.MaterialId, reference.VersionId, profile.KnownAt, reference.ContentHash);
        }
    }
    private static MaterialProvenance? Provenance(MaterialProvenance? provenance)
    {
        if (provenance is null) return null;
        var url = provenance.SourceUrl;
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))) url = null;
        // Locator is internal source metadata and is not a public grant.
        return new(provenance.Acquisition, url, provenance.RetrievedAt, provenance.TextCompleteness, provenance.OriginVersionId);
    }
    private static CompanyMaterialResponse Public(ScopedCompanyMaterial source) => new(source.MaterialId, source.VersionId,
        source.SourceId, source.Kind, source.Title, source.Text, source.KnownAt, source.PublishedAt, source.CorrectsVersionId,
        source.ContentHash, source.Provenance?.TextCompleteness ?? "unknown", Provenance(source.Provenance));
    private static CompanyKnowledgeResponse Public(CompanyProfile profile) => new(profile.CompanyId, profile.RevisionId,
        profile.Name, profile.Description, profile.IndustryCodes, profile.Exposures, profile.KnownAt, profile.SourceRefs);

    public Task<Result<CompanyKnowledgeResponse>> GetKnowledge(Guid actor, CancellationToken cancellationToken) => Run(async (ct) =>
    {
        var company = await Scope(actor, false, ct);
        var profile = await profiles.Get(company.Id, DateTimeOffset.UtcNow);
        await ValidateProfile(profile, company, ct);
        return Public(profile);
    }, cancellationToken);
    private static PutCompanyKnowledgeRequest Normalize(PutCompanyKnowledgeRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 16000 ||
            request.IndustryCodes is null || request.IndustryCodes.Length > 64 || request.Exposures is null || request.Exposures.Length > 64 ||
            request.SourceRefs is null || request.SourceRefs.Length is < 1 or > 64) throw PublicKnowledgeError.Invalid();
        static string[] Features(string[] values, int max)
        {
            if (values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > max || v.Any(char.IsControl))) throw PublicKnowledgeError.Invalid();
            return values.Select(v => v.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        foreach (var reference in request.SourceRefs)
        {
            if (reference is null) throw PublicKnowledgeError.Invalid();
            Id(reference.MaterialId); Id(reference.VersionId);
        }
        return new(request.Description.Trim(), Features(request.IndustryCodes, 100), Features(request.Exposures, 1000),
            request.SourceRefs.Distinct().OrderBy(r => r.MaterialId, StringComparer.Ordinal).ThenBy(r => r.VersionId, StringComparer.Ordinal).ToArray());
    }
    public Task<Result<CompanyKnowledgeResponse>> PutKnowledge(Guid actor, PutCompanyKnowledgeRequest request, string? key, CancellationToken cancellationToken) => Run(async (ct) =>
    {
        var company = await Scope(actor, true, ct);
        Key(key);
        var normalized = Normalize(request);
        var hash = Hash(normalized);
        var operation = await operations.Find(actor, company.Id, "knowledge", key!, hash);
        if (operation is null)
        {
            var refs = new List<SourceReference>();
            foreach (var input in normalized.SourceRefs)
            {
                var source = await materials.Get(input.MaterialId, input.VersionId, ct);
                MaterialScope(source, company, input.MaterialId, input.VersionId, DateTimeOffset.UtcNow);
                refs.Add(new() { MaterialId = source.MaterialId, VersionId = source.VersionId, ContentHash = source.ContentHash });
            }
            operation = await operations.Reserve(actor, company.Id, "knowledge", key!, hash, time => Json(new CompanyProfile
            {
                CompanyId = company.Id,
                Name = company.Name,
                BankProfileId = company.BankProfileId,
                RevisionId = "user-profile-" + Guid.NewGuid().ToString("N"),
                KnownAt = time,
                Description = normalized.Description,
                IndustryCodes = normalized.IndustryCodes,
                Exposures = normalized.Exposures,
                SourceRefs = refs.ToArray()
            }));
        }
        // A concurrent insert loser always uses the winning frozen payload, not local IDs/time.
        var profile = Decode<CompanyProfile>(operation.Payload);
        if (profile.BankProfileId != company.BankProfileId) throw new PublicKnowledgeError(HttpStatusCode.Conflict, "The company bank bridge changed after reservation.");
        await ValidateProfile(profile, company, ct);
        await Recheck(actor, company, ct);
        if (!operation.Completed)
        {
            var saved = await profiles.Save(profile);
            if (Json(saved) != operation.Payload) throw PublicKnowledgeError.Unavailable();
            await operations.Complete(operation);
        }
        return Public(profile);
    }, cancellationToken);
    public Task<Result<CompanyMaterialResponse>> CreateMaterial(Guid actor, CreateCompanyMaterialRequest request, string? key, CancellationToken cancellationToken) => Run(async (ct) =>
    {
        var company = await Scope(actor, true, ct);
        Key(key);
        if (request is null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 1000 ||
            string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 200000) throw PublicKnowledgeError.Invalid();
        var normalized = new CreateCompanyMaterialRequest(request.Title.Trim(), request.Text);
        var operation = await operations.Reserve(actor, company.Id, "material", key!, Hash(normalized), time =>
        {
            var payload = new CompanyMaterialWrite("user-material-" + Guid.NewGuid().ToString("N"), "user-version-" + Guid.NewGuid().ToString("N"),
                "user-declaration", "company", normalized.Title, normalized.Text, time, null, null, [company.Id], [], new("uploaded", null, time, "unknown"));
            var json = Json(payload);
            // Match actual downstream serialization: oversized escaped JSON must fail before a receipt is reserved.
            if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw PublicKnowledgeError.Invalid();
            return json;
        });
        var frozen = Decode<CompanyMaterialWrite>(operation.Payload);
        if (operation.KnownAt > DateTimeOffset.UtcNow) throw PublicKnowledgeError.Unavailable();
        await Recheck(actor, company, ct);
        var saved = operation.Completed ? await materials.Get(frozen.MaterialId, frozen.VersionId, ct) : await materials.Save(frozen, ct);
        MaterialScope(saved, company, frozen.MaterialId, frozen.VersionId, operation.KnownAt);
        var actual = new CompanyMaterialWrite(saved.MaterialId, saved.VersionId, saved.SourceId, saved.Kind, saved.Title, saved.Text,
            saved.KnownAt, saved.PublishedAt, saved.CorrectsVersionId, saved.CompanyIds, saved.BankProfileIds, saved.Provenance);
        if (Json(actual) != operation.Payload) throw PublicKnowledgeError.Unavailable();
        await operations.Complete(operation);
        return Public(saved);
    }, cancellationToken);
    public Task<Result<CompanyMaterialResponse>> GetMaterial(Guid actor, string materialId, string versionId, CancellationToken cancellationToken) => Run(async (ct) =>
    {
        var company = await Scope(actor, false, ct);
        Id(materialId); Id(versionId);
        var source = await materials.Get(materialId, versionId, ct);
        MaterialScope(source, company, materialId, versionId, DateTimeOffset.UtcNow);
        return Public(source);
    }, cancellationToken);
    public Task<Result<CompanyMaterialsResponse>> ListMaterials(Guid actor, int limit, string? cursor, CancellationToken cancellationToken) => Run(async (ct) =>
    {
        var company = await Scope(actor, false, ct);
        if (limit is < 1 or > 20) throw PublicKnowledgeError.Invalid();
        var asOf = DateTimeOffset.UtcNow;
        string? inner = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length is < 1 or > 4096 || !Regex.IsMatch(cursor, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) throw PublicKnowledgeError.Invalid();
                var encoded = cursor.Replace('-', '+').Replace('_', '/');
                var value = Decode<MaterialCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='))));
                if (value.CompanyId != company.Id || value.BankProfileId != company.BankProfileId || value.AsOf == default ||
                    value.AsOf > DateTimeOffset.UtcNow || value.Inner is null || value.Inner.Length is < 1 or > 2048) throw PublicKnowledgeError.Invalid();
                asOf = value.AsOf; inner = value.Inner;
            }
            catch (Exception error) when (error is FormatException or JsonException) { throw PublicKnowledgeError.Invalid(); }
        }
        var page = await materials.List(company.Id, company.BankProfileId, asOf, limit, inner, ct);
        if (page.CompanyId != company.Id || page.AsOf.UtcTicks != asOf.UtcTicks || page.Items is null || page.Items.Length > limit ||
            page.Coverage != "canonical_changes_only" || page.NextCursor?.Length > 2048) throw PublicKnowledgeError.Unavailable();
        foreach (var item in page.Items)
        {
            if (item is null || item.Kind != "company" || item.KnownAt > asOf || item.LatestVersionCount < 1 ||
                item.ContentHash is null || !Regex.IsMatch(item.ContentHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)) throw PublicKnowledgeError.Unavailable();
        }
        var next = page.NextCursor is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(Json(new MaterialCursor(company.Id, company.BankProfileId, asOf, page.NextCursor))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new CompanyMaterialsResponse(company.Id, asOf, page.Items.Select(item => item with { Provenance = Provenance(item.Provenance) }).ToArray(), next, page.Coverage);
    }, cancellationToken);
    private static async Task<Result<T>> Run<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        try { return new(await operation(budget.Token), null); }
        catch (PublicKnowledgeError error)
        {
            return new(new Error(error.Message, ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = error.Status });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Never return raw HTTP/SQL exception messages or a success for an unknown write outcome.
            return new(new Error(PublicKnowledgeError.Unavailable().Message, ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
    }
}
