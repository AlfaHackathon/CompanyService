using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Common;
using Api.Contracts.Payments;
using Api.Resources;
using Api.Services;

namespace Api.Repositories;

public interface IPaymentRows
{
    Task<PaymentRowReceipt> Save(PaymentRowInput input);
    Task<PaymentActivity?> Activity(Guid companyId, string sourceId, string sourceVersionId, DateTimeOffset asOf);
}

public sealed class PaymentRowStore(IDapperContext context, TimeProvider? clock = null) : IPaymentRows
{
    private sealed record Row(long Sequence, long KnownTicks, string Payload);
    private sealed record Totals(string SourceContentHash, long SourceKnownTicks, long ObservedKnownTicks, string ObservedRowCount, string Watermark,
        string IncomingAmount, string IncomingCount, string IncomingRows, string OutgoingAmount, string OutgoingCount, string OutgoingRows,
        string SelfAmount, string SelfCount, string SelfRows);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static PaymentRowReceipt Receipt(Row row)
    {
        var input = JsonSerializer.Deserialize<PaymentRowInput>(row.Payload, KnowledgeJson.Options) ?? throw PaymentActivityError.Unavailable();
        return new(input, new DateTimeOffset(row.KnownTicks, TimeSpan.Zero), row.Sequence.ToString(CultureInfo.InvariantCulture), input.PayerCompanyId == input.BeneficiaryCompanyId);
    }
    public async Task<PaymentRowReceipt> Save(PaymentRowInput input)
    {
        var payload = Json(input);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(KnowledgeSql.WriteLock));
        var source = new { source = input.SourceId, version = input.SourceVersionId, row = input.SourceRowId };
        var versionHash = await transaction.CommandWithResponse<string>(new QueryObject(PaymentRowSql.FindVersionHash, source));
        if (versionHash.Length > 0 && versionHash != input.SourceContentHash)
            throw new PaymentActivityError(HttpStatusCode.Conflict, "A logical payment source version is already bound to another content hash.");
        var existing = await transaction.CommandWithResponse<string>(new QueryObject(PaymentRowSql.FindRowHash, source));
        if (existing.Length > 0)
        {
            if (existing != hash) throw new PaymentActivityError(HttpStatusCode.Conflict, "The source row identity already has different immutable content or import metadata.");
            var replay = await transaction.CommandWithResponse<Row>(new QueryObject(PaymentRowSql.ReadRow, source));
            transaction.Commit();
            return Receipt(replay);
        }
        var previous = await transaction.CommandWithResponse<long>(new QueryObject(PaymentRowSql.LastKnownTicks));
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcTicks;
        if (previous > now) throw new PaymentActivityError(HttpStatusCode.ServiceUnavailable, "Payment knowledge clock moved backwards. No new row was confirmed.");
        var ticks = Math.Max(now, checked(previous + 1));
        if (versionHash.Length == 0)
            await transaction.Command(new QueryObject(PaymentRowSql.InsertVersion,
                new { source = input.SourceId, version = input.SourceVersionId, sourceHash = input.SourceContentHash, ticks }));
        var saved = await transaction.CommandWithResponse<Row>(new QueryObject(PaymentRowSql.InsertRow, new
        {
            source = input.SourceId,
            version = input.SourceVersionId,
            row = input.SourceRowId,
            import = input.ImportId,
            payer = input.PayerCompanyId,
            beneficiary = input.BeneficiaryCompanyId,
            amount = input.PayAmt,
            count = input.PayCnt,
            ticks,
            hash,
            payload
        }));
        transaction.Commit();
        return Receipt(saved);
    }

    public async Task<PaymentActivity?> Activity(Guid companyId, string sourceId, string sourceVersionId, DateTimeOffset asOf)
    {
        // One MVCC statement binds all totals and provenance to the same observed source snapshot.
        var totals = await context.FirstOrDefault<Totals>(new QueryObject(PaymentRowSql.Activity,
            new { company = companyId, source = sourceId, version = sourceVersionId, ticks = asOf.UtcTicks }));
        if (totals is null) return null;
        return new(companyId, sourceId, sourceVersionId, totals.SourceContentHash, asOf, "RUB", new("since_january_1", null, null),
            new(totals.IncomingAmount, totals.IncomingCount, totals.IncomingRows), new(totals.OutgoingAmount, totals.OutgoingCount, totals.OutgoingRows),
            new(totals.SelfAmount, totals.SelfCount, totals.SelfRows), new(null, totals.ObservedRowCount, totals.Watermark),
            new(new DateTimeOffset(totals.SourceKnownTicks, TimeSpan.Zero), new DateTimeOffset(totals.ObservedKnownTicks, TimeSpan.Zero), "observed_source_rows"));
    }
}
