using System.Net;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Changes;
using Api.Resources;

namespace Api.Repositories;

public sealed class KnowledgeChanges(IDapperContext context)
{
    private sealed record ChangeRow(long Sequence, string Category, string EntityId, string VersionId, string KnownAt, string CompanyIds);
    internal static Task Append(ITransaction transaction, string category, string id, string version, DateTimeOffset knownAt, Guid[] companies) => transaction.Command(new QueryObject(KnowledgeSql.AppendChange,
        new
        {
            c = category,
            i = id,
            v = version,
            t = knownAt.ToString("O"),
            ticks = knownAt.UtcTicks,
            s = JsonSerializer.Serialize(companies)
        }));
    public async Task<Result<KnowledgeChange[]>> Read(long after)
    {
        if (after < 0)
        {
            return new(new Error("Change cursor must be nonnegative.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        return new((await context.ListOrEmpty<ChangeRow>(new QueryObject(KnowledgeSql.ReadNextChange, new
        {
            after
        }))).Select(row => new KnowledgeChange
            {
                Sequence = row.Sequence,
                Category = row.Category,
                EntityId = row.EntityId,
                VersionId = row.VersionId,
                KnownAt = DateTimeOffset.Parse(row.KnownAt, System.Globalization.CultureInfo.InvariantCulture),
                CompanyIds = JsonSerializer.Deserialize<Guid[]>(row.CompanyIds)!
            }).ToArray(),
            null);
    }

    public async Task<Result<KnowledgeChangesResponse>> GetChangesAsync(long after)
    {
        var result = await Read(after);
        return result.Error is { } error
            ? new(error)
            : new(new KnowledgeChangesResponse { Changes = result.Content! }, null);
    }
}
