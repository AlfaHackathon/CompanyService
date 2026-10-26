using System.Net;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Resources;
using Api.Services.PublicKnowledge;

namespace Api.Repositories;

public sealed record KnowledgeOperation(Guid ActorId, Guid CompanyId, string Operation, string Key,
    string RequestHash, DateTimeOffset KnownAt, string Payload, bool Completed);
public interface IKnowledgeOperationStore
{
    Task<KnowledgeOperation?> Find(Guid actor, Guid company, string operation, string key, string requestHash);
    Task<KnowledgeOperation> Reserve(Guid actor, Guid company, string operation, string key, string requestHash,
        Func<DateTimeOffset, string> freeze);
    Task Complete(KnowledgeOperation operation);
}

// Reservation is one short local transaction. Never holds a DB lock across HTTP.
public sealed class KnowledgeOperationStore(IDapperContext context) : IKnowledgeOperationStore
{
    private sealed record Row(string RequestHash, long KnownTicks, string Payload, bool Completed);
    public async Task<KnowledgeOperation?> Find(Guid actor, Guid company, string operation, string key, string requestHash)
    {
        var row = await context.FirstOrDefault<Row>(new QueryObject(PublicKnowledgeSql.FindOperation,
            new { actor = actor.ToString("D"), company = company.ToString("D"), operation, key }));
        if (row is null) return null;
        if (row.RequestHash != requestHash)
            throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Idempotency-Key was already used with different normalized input.");
        return new(actor, company, operation, key, requestHash, new DateTimeOffset(row.KnownTicks, TimeSpan.Zero), row.Payload, row.Completed);
    }
    public async Task<KnowledgeOperation> Reserve(Guid actor, Guid company, string operation, string key, string requestHash,
        Func<DateTimeOffset, string> freeze)
    {
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(PublicKnowledgeSql.ReservationLock));
        var parameters = new { actor = actor.ToString("D"), company = company.ToString("D"), operation, key };
        // DapperContext 1.1 transactions expose QueryFirstAsync-backed commands, not optional reads.
        var exists = await transaction.CommandWithResponse<bool>(new QueryObject(
            $"SELECT EXISTS ({PublicKnowledgeSql.FindOperation})", parameters));
        var existing = exists
            ? await transaction.CommandWithResponse<Row>(new QueryObject(PublicKnowledgeSql.FindOperation, parameters))
            : null;
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Idempotency-Key was already used with different normalized input.");
            transaction.Commit();
            return new(actor, company, operation, key, requestHash, new DateTimeOffset(existing.KnownTicks, TimeSpan.Zero), existing.Payload, existing.Completed);
        }
        // Public writes reserve distinct per-company times, even on low-resolution clocks.
        var previous = await transaction.CommandWithResponse<long>(new QueryObject(PublicKnowledgeSql.LastCompanyTime,
            new { company = company.ToString("D") }));
        var now = DateTimeOffset.UtcNow.UtcTicks;
        if (previous > now)
            throw new PublicKnowledgeError(HttpStatusCode.ServiceUnavailable, "A future company knowledge reservation requires reconciliation before a new public write.");
        var ticks = Math.Max(now, checked(previous + 1));
        var knownAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        var payload = freeze(knownAt);
        await transaction.Command(new QueryObject(PublicKnowledgeSql.InsertOperation, new
        {
            actor = actor.ToString("D"),
            company = company.ToString("D"),
            operation,
            key,
            requestHash,
            ticks,
            payload
        }));
        transaction.Commit();
        return new(actor, company, operation, key, requestHash, knownAt, payload, false);
    }
    public async Task Complete(KnowledgeOperation operation)
    {
        // Monotonic: failures/timeouts never write a status and cannot undo a completion.
        await context.Command(new QueryObject(PublicKnowledgeSql.CompleteOperation, new
        {
            actor = operation.ActorId.ToString("D"),
            company = operation.CompanyId.ToString("D"),
            operation = operation.Operation,
            key = operation.Key,
            requestHash = operation.RequestHash
        }));
    }
}
