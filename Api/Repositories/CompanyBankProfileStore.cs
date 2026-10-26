using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Resources;
using Api.Services.PublicKnowledge;

namespace Api.Repositories;

public interface ICompanyBankProfiles
{
    Task<CompanyBankProfile?> Get(Guid companyId, DateTimeOffset asOf);
    Task<CompanyBankProfilePage> Versions(long after, int limit);
    Task<CompanyBankProfile> Save(CompanyBankProfile profile, string? expectedRevisionId);
}

// The same Company UUID may have canonical bank data without an account or operational company row.
public sealed class CompanyBankProfileStore(IDapperContext context) : ICompanyBankProfiles
{
    private sealed record Row(long Sequence, string Payload);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    public async Task<CompanyBankProfile?> Get(Guid companyId, DateTimeOffset asOf)
    {
        var payload = await context.FirstOrDefault<string>(new QueryObject(CompanyBankSql.Latest, new { company = companyId, ticks = asOf.UtcTicks }));
        return payload is null ? null : JsonSerializer.Deserialize<CompanyBankProfile>(payload, KnowledgeJson.Options);
    }
    public async Task<CompanyBankProfilePage> Versions(long after, int limit)
    {
        var rows = await context.ListOrEmpty<Row>(new QueryObject(CompanyBankSql.Versions, new { after, limit }));
        return new(rows.Select(row => JsonSerializer.Deserialize<CompanyBankProfile>(row.Payload, KnowledgeJson.Options)
            ?? throw PublicKnowledgeError.Unavailable()).ToArray(), rows.Count == limit ? rows[^1].Sequence.ToString(CultureInfo.InvariantCulture) : null);
    }
    public async Task<CompanyBankProfile> Save(CompanyBankProfile profile, string? expectedRevisionId)
    {
        var payload = Json(profile);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(KnowledgeSql.WriteLock));
        var existing = await transaction.CommandWithResponse<string>(new QueryObject(CompanyBankSql.FindHash,
            new { company = profile.CompanyId, revision = profile.RevisionId }));
        if (existing.Length != 0)
        {
            if (existing != hash) throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Canonical bank profile revisions are immutable.");
            transaction.Commit();
            return profile;
        }
        var latest = await transaction.CommandWithResponse<string>(new QueryObject(CompanyBankSql.LatestRevision, new { company = profile.CompanyId }));
        if (latest != (expectedRevisionId ?? ""))
            throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Bank profile changed before this capture. Submit a new captureId.");
        await transaction.Command(new QueryObject(CompanyBankSql.Insert, new
        {
            company = profile.CompanyId,
            revision = profile.RevisionId,
            ticks = profile.KnownAt.UtcTicks,
            hash,
            data = Json(profile.BankData),
            payload
        }));
        transaction.Commit();
        return profile;
    }
}
