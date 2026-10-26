using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using Api.Common;
using Api.Repositories;
using Api.Resources;
using Api.Services;
using Xunit;

public sealed class PaymentRowStoreTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-26T10:00:00Z");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task ExactRowReplayRetainsStoredTimeAndPrecedesClockOrNewInsert()
    {
        var input = PaymentRowValidationTests.Row();
        var db = new Database { VersionHash = input.SourceContentHash, RowHash = Hash(Json(input)), Payload = Json(input) };
        var clock = new Clock { ThrowIfRead = true };
        var receipt = await new PaymentRowStore(db.Context(), clock).Save(input);
        Assert.Equal(input, receipt.SourceRaw);
        Assert.Equal(At, receipt.KnownAt);
        Assert.Equal("42", receipt.Watermark);
        Assert.False(receipt.SelfTransfer);
        Assert.Equal(["begin", "lock", "version", "row-hash", "row-read", "commit", "dispose"], db.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceVersionOrRowContentConflictNeverOverwritesExistingData(bool versionConflict)
    {
        var input = PaymentRowValidationTests.Row();
        var db = new Database { VersionHash = versionConflict ? new string('b', 64) : input.SourceContentHash, RowHash = "different-row-hash" };
        var error = await Assert.ThrowsAsync<PaymentActivityError>(() => new PaymentRowStore(db.Context(), new Clock()).Save(input));
        Assert.Equal(HttpStatusCode.Conflict, error.Status);
        Assert.DoesNotContain("insert", db.Events);
        Assert.DoesNotContain("commit", db.Events);
    }

    [Fact]
    public async Task NewRowFreezesMonotonicServerKnowledgeTimeAndPreservesEveryRawField()
    {
        var input = PaymentRowValidationTests.Row() with { BeneficiaryCompanyId = PaymentRowValidationTests.Row().PayerCompanyId };
        var db = new Database { PreviousTicks = At.UtcTicks };
        var receipt = await new PaymentRowStore(db.Context(), new Clock()).Save(input);
        Assert.Equal(At.AddTicks(1), receipt.KnownAt);
        Assert.Equal(input, receipt.SourceRaw);
        Assert.True(receipt.SelfTransfer);
        Assert.Equal(input.SourceRowId, Property(db.InsertParameters!, "row"));
        Assert.Equal(input.PayAmt, Property(db.InsertParameters!, "amount"));
        Assert.Equal(input.PayCnt, Property(db.InsertParameters!, "count"));
        Assert.Equal(Json(input), Property(db.InsertParameters!, "payload"));
        Assert.Equal(["begin", "lock", "version", "row-hash", "last-time", "bind-version", "insert", "commit", "dispose"], db.Events);
    }

    [Fact]
    public async Task BackwardsClockFailsBeforeBindingVersionOrWritingRow()
    {
        var db = new Database { PreviousTicks = At.AddHours(1).UtcTicks };
        var error = await Assert.ThrowsAsync<PaymentActivityError>(() => new PaymentRowStore(db.Context(), new Clock()).Save(PaymentRowValidationTests.Row()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.Status);
        Assert.DoesNotContain("bind-version", db.Events);
        Assert.DoesNotContain("insert", db.Events);
    }

    [Fact]
    public async Task AggregateUsesOneSqlSnapshotAndRetainsExactStringsAndUnknownPeriodCoverage()
    {
        var db = new Database { ReturnTotals = true };
        var input = PaymentRowValidationTests.Row();
        var activity = (await new PaymentRowStore(db.Context()).Activity(input.PayerCompanyId, input.SourceId, input.SourceVersionId, At.AddDays(1)))!;
        Assert.Equal(["activity-snapshot"], db.Events);
        Assert.Equal("12345678901234567890123.123456789012345", activity.Incoming.PayAmt);
        Assert.Equal("99999999999999999999999999999999999999", activity.Incoming.PayCnt);
        Assert.Equal("RUB", activity.Currency);
        Assert.Equal("since_january_1", activity.Period.Basis);
        Assert.Null(activity.Period.Year);
        Assert.Null(activity.Period.End);
        Assert.Null(activity.Coverage.Complete);
        Assert.Equal("2", activity.Coverage.ObservedRowCount);
        Assert.Equal("77", activity.Coverage.Watermark);
        Assert.Equal(new string('a', 64), activity.SourceContentHash);
        Assert.Equal(At, activity.Provenance.SourceKnownAt);
        Assert.Equal(At.AddTicks(1), activity.Provenance.ObservedKnownAt);
    }

    private static object? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    private sealed class Clock : TimeProvider
    {
        public bool ThrowIfRead;
        public override DateTimeOffset GetUtcNow() => ThrowIfRead ? throw new InvalidOperationException("Replay must not read a new clock value") : At;
    }
    private sealed class Database
    {
        public string VersionHash = "", RowHash = "", Payload = "";
        public long PreviousTicks;
        public object? InsertParameters;
        public bool ReturnTotals;
        public readonly List<string> Events = [];
        public IDapperContext Context()
        {
            var proxy = DispatchProxy.Create<IDapperContext, DatabaseProxy>();
            ((DatabaseProxy)(object)proxy).State = this;
            return proxy;
        }
        private static object? Reply(MethodInfo method, object? response)
        {
            if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
            var type = method.ReturnType.GetGenericArguments()[0];
            if (response is null && type.IsValueType) response = Activator.CreateInstance(type);
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, [response]);
        }
        public object? Invoke(MethodInfo method, object?[] args, bool transaction)
        {
            if (!transaction && method.Name == "BeginTransaction")
            {
                Events.Add("begin");
                var proxy = DispatchProxy.Create<ITransaction, DatabaseProxy>();
                ((DatabaseProxy)(object)proxy).State = this;
                ((DatabaseProxy)(object)proxy).Transaction = true;
                return proxy;
            }
            if (method.Name is "Commit" or "Dispose") { Events.Add(method.Name.ToLowerInvariant()); return null; }
            var query = Assert.IsAssignableFrom<IQueryObject>(args[0]);
            if (!transaction)
            {
                Assert.Equal("FirstOrDefault", method.Name);
                Assert.Equal(PaymentRowSql.Activity, query.Sql);
                Events.Add("activity-snapshot");
                var type = method.GetGenericArguments()[0];
                var totals = ReturnTotals ? Activator.CreateInstance(type, new string('a', 64), At.UtcTicks, At.AddTicks(1).UtcTicks,
                    "2", "77", "12345678901234567890123.123456789012345", "99999999999999999999999999999999999999", "1", "0", "0", "0", "5.0", "1", "1") : null;
                return Reply(method, totals);
            }
            if (query.Sql == KnowledgeSql.WriteLock) { Events.Add("lock"); return Reply(method, null); }
            if (query.Sql == PaymentRowSql.FindVersionHash) { Events.Add("version"); return Reply(method, VersionHash); }
            if (query.Sql == PaymentRowSql.FindRowHash) { Events.Add("row-hash"); return Reply(method, RowHash); }
            if (query.Sql == PaymentRowSql.LastKnownTicks) { Events.Add("last-time"); return Reply(method, PreviousTicks); }
            if (query.Sql == PaymentRowSql.InsertVersion) { Events.Add("bind-version"); return Reply(method, null); }
            if (query.Sql == PaymentRowSql.InsertRow)
            {
                Events.Add("insert");
                InsertParameters = query.Parameters;
                Payload = (string)Property(query.Parameters!, "payload")!;
                return Reply(method, Activator.CreateInstance(method.GetGenericArguments()[0], 42L, (long)Property(query.Parameters!, "ticks")!, Payload));
            }
            Assert.Equal(PaymentRowSql.ReadRow, query.Sql);
            Events.Add("row-read");
            return Reply(method, Activator.CreateInstance(method.GetGenericArguments()[0], 42L, At.UtcTicks, Payload));
        }
    }
    public class DatabaseProxy : DispatchProxy
    {
        internal object State = null!;
        internal bool Transaction;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => ((Database)State).Invoke(method!, args ?? [], Transaction);
    }
}
