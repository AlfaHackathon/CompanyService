using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Resources;
using Npgsql;

namespace Api.Repositories;

public sealed class KnowledgeStore(IDapperContext context)
{
    public async Task<Result<T>> Save<T>(string category, string id, string revision, DateTimeOffset knownAt, T value)
    {
        var json = JsonSerializer.Serialize(value, KnowledgeJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(KnowledgeSql.WriteLock));
        var old = await transaction.CommandWithResponse<string>(new QueryObject(KnowledgeSql.FindKnowledgeHash, new
        {
            c = category,
            e = id,
            r = revision
        }));
        if (old.Length != 0)
        {
            if (old != hash)
            {
                return new(new Error("Knowledge revisions are immutable.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }

            transaction.Commit();
            return new(value, null);
        }

        try
        {
            await transaction.Command(new QueryObject(KnowledgeSql.InsertKnowledge,
                new
                {
                    c = category,
                    e = id,
                    r = revision,
                    t = knownAt.UtcTicks,
                    h = hash,
                    p = json
                }));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new(new Error("Two different revisions cannot share one knowledge time.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
        }

        var companies = value switch
        {
            CompanyProfile p => new[]
            {
                p.CompanyId
            },
            RoleProfile p => p.AllowedCompanyIds,
            _ => Array.Empty<Guid>()
        };
        await KnowledgeChanges.Append(transaction, category, id, revision, knownAt, companies);
        transaction.Commit();
        return new(value, null);
    }

    public async Task<T[]> List<T>(string category, DateTimeOffset asOf, bool latestOnly = true) => (await context.ListOrEmpty<string>(new QueryObject(latestOnly ? KnowledgeSql.LatestKnowledge : KnowledgeSql.AllKnowledge, new { c = category, t = asOf.UtcTicks }))).Select(json => JsonSerializer.Deserialize<T>(json, KnowledgeJson.Options)!).ToArray();
}
