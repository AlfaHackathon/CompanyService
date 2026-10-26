using System.Net;
using System.Reflection;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using Api.Repositories;
using Api.Resources;
using Api.Services.PublicKnowledge;
using Xunit;

public sealed partial class PublicKnowledgeTests
{
    [Fact]
    public async Task AbsentReservationNeverUsesQueryFirstForAnEmptyResult()
    {
        var database = new ReservationDatabase(false);
        var store = new KnowledgeOperationStore(database.Context());
        var freezes = 0;
        var result = await store.Reserve(Actor, CompanyId, "knowledge", "key", "hash", time =>
        {
            freezes++;
            return time.ToString("O");
        });
        Assert.Equal(1, freezes);
        Assert.False(result.Completed);
        Assert.Equal(result.KnownAt.ToString("O"), result.Payload);
        Assert.Equal(["begin", "lock", "exists", "last-time", "insert", "commit", "dispose"], database.Events);
        Assert.Equal(Actor.ToString("D"), database.LookupParameters!.GetType().GetProperty("actor")!.GetValue(database.LookupParameters));
        Assert.Equal(CompanyId.ToString("D"), database.LookupParameters.GetType().GetProperty("company")!.GetValue(database.LookupParameters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingReservationReusesFrozenReceiptInsideTheLockedTransaction(bool completed)
    {
        var database = new ReservationDatabase(true) { Completed = completed };
        var store = new KnowledgeOperationStore(database.Context());
        var result = await store.Reserve(Actor, CompanyId, "knowledge", "key", "hash", _ =>
            throw new InvalidOperationException("An existing reservation must never be frozen again."));
        Assert.Equal(database.Time, result.KnownAt);
        Assert.Equal("frozen payload", result.Payload);
        Assert.Equal(completed, result.Completed);
        Assert.Equal(["begin", "lock", "exists", "read", "commit", "dispose"], database.Events);
    }

    [Fact]
    public async Task ConflictingReservationDoesNotCommitOrOverwriteItsFrozenReceipt()
    {
        var database = new ReservationDatabase(true) { RequestHash = "original hash" };
        var store = new KnowledgeOperationStore(database.Context());
        var error = await Assert.ThrowsAsync<PublicKnowledgeError>(() => store.Reserve(
            Actor, CompanyId, "knowledge", "key", "different hash", _ =>
                throw new InvalidOperationException("Conflicting input must never be frozen.")));
        Assert.Equal(HttpStatusCode.Conflict, error.Status);
        Assert.Equal(["begin", "lock", "exists", "read", "dispose"], database.Events);
    }

    internal sealed class ReservationDatabase(bool exists)
    {
        internal List<string> Events { get; } = [];
        internal object? LookupParameters { get; set; }
        internal DateTimeOffset Time { get; } = DateTimeOffset.Parse("2026-10-25T10:00:00Z");
        internal string RequestHash { get; init; } = "hash";
        internal bool Completed { get; init; }

        internal IDapperContext Context()
        {
            var context = DispatchProxy.Create<IDapperContext, ReservationDatabaseProxy>();
            ((ReservationDatabaseProxy)(object)context).Database = this;
            return context;
        }

        internal object? Invoke(MethodInfo method, object?[] args, bool transaction)
        {
            if (!transaction)
            {
                Assert.Equal("BeginTransaction", method.Name);
                Events.Add("begin");
                var value = DispatchProxy.Create<ITransaction, ReservationDatabaseProxy>();
                var proxy = (ReservationDatabaseProxy)(object)value;
                proxy.Database = this;
                proxy.IsTransaction = true;
                return value;
            }
            if (method.Name is "Commit" or "Dispose")
            {
                Events.Add(method.Name.ToLowerInvariant());
                return null;
            }
            var query = Assert.IsAssignableFrom<IQueryObject>(args[0]);
            object? response = null;
            if (method.Name == "Command")
            {
                if (query.Sql == PublicKnowledgeSql.ReservationLock) Events.Add("lock");
                else
                {
                    Assert.Equal(PublicKnowledgeSql.InsertOperation, query.Sql);
                    Events.Add("insert");
                }
            }
            else
            {
                Assert.Equal("CommandWithResponse", method.Name);
                var type = method.GetGenericArguments()[0];
                if (type == typeof(bool))
                {
                    Assert.Equal("lock", Events[^1]);
                    Assert.Equal($"SELECT EXISTS ({PublicKnowledgeSql.FindOperation})", query.Sql);
                    LookupParameters = query.Parameters;
                    Events.Add("exists");
                    response = exists;
                }
                else if (type == typeof(long))
                {
                    Assert.False(exists);
                    Assert.Equal(PublicKnowledgeSql.LastCompanyTime, query.Sql);
                    Events.Add("last-time");
                    response = 0L;
                }
                else
                {
                    if (!exists) throw new InvalidOperationException("QueryFirstAsync has no row.");
                    Assert.Equal(PublicKnowledgeSql.FindOperation, query.Sql);
                    Assert.Same(LookupParameters, query.Parameters);
                    Events.Add("read");
                    response = Activator.CreateInstance(type, RequestHash, Time.UtcTicks, "frozen payload", Completed);
                }
            }
            if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
            var resultType = method.ReturnType.GetGenericArguments()[0];
            if (response is null && resultType.IsValueType) response = Activator.CreateInstance(resultType);
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [response]);
        }
    }

    public class ReservationDatabaseProxy : DispatchProxy
    {
        internal ReservationDatabase Database { get; set; } = null!;
        internal bool IsTransaction { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Database.Invoke(method ?? throw new InvalidOperationException("Missing method."), args ?? [], IsTransaction);
    }
}
