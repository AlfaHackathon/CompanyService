using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Contracts.PublicKnowledge;
using Api.Repositories;
using Api.Resources;
using Api.Services.PublicKnowledge;
using Xunit;

public sealed class CompanyBankProfileStoreTests
{
    private static CompanyBankProfile Profile() => new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "A", DateTimeOffset.Parse("2026-10-26T10:00:00Z"),
        CompanyBankDataTests.AllFields(), "company-bank-profile", [], new MaterialProvenance("event_payload", null, DateTimeOffset.Parse("2026-10-26T10:00:00Z"), "full"));

    [Fact]
    public async Task CompareAndInsertHoldSameWriteLockAndRejectLatestCommittedRevisionRegardlessOfFrozenTime()
    {
        var db = new Database { Latest = "later-committed-B" };
        var error = await Assert.ThrowsAsync<PublicKnowledgeError>(() => new CompanyBankProfileStore(db.Context()).Save(Profile(), "R0"));
        Assert.Equal(HttpStatusCode.Conflict, error.Status);
        Assert.Equal(["begin", "lock", "exact-hash", "latest", "dispose"], db.Events);
        Assert.DoesNotContain("@ticks", CompanyBankSql.LatestRevision);
        Assert.DoesNotContain("@t", CompanyBankSql.LatestRevision);
    }

    [Fact]
    public async Task ExactImmutableHashReplayPrecedesLatestCommittedRevisionCheck()
    {
        var profile = Profile();
        var db = new Database { ExistingHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile, KnowledgeJson.Options)))), Latest = "B" };
        Assert.Equal(profile, await new CompanyBankProfileStore(db.Context()).Save(profile, "R0"));
        Assert.Equal(["begin", "lock", "exact-hash", "commit", "dispose"], db.Events);
    }

    private sealed class Database
    {
        public string ExistingHash = "", Latest = "";
        public readonly List<string> Events = [];
        public IDapperContext Context()
        {
            var value = DispatchProxy.Create<IDapperContext, Proxy>();
            ((Proxy)(object)value).State = this;
            return value;
        }
        public object? Invoke(MethodInfo method, object?[] args, bool transaction)
        {
            if (!transaction)
            {
                Assert.Equal("BeginTransaction", method.Name);
                Events.Add("begin");
                var value = DispatchProxy.Create<ITransaction, Proxy>();
                ((Proxy)(object)value).State = this;
                ((Proxy)(object)value).Transaction = true;
                return value;
            }
            if (method.Name is "Commit" or "Dispose") { Events.Add(method.Name.ToLowerInvariant()); return null; }
            var query = Assert.IsAssignableFrom<IQueryObject>(args[0]);
            if (method.Name == "Command")
            {
                Assert.Equal(KnowledgeSql.WriteLock, query.Sql);
                Events.Add("lock");
                return Task.CompletedTask;
            }
            Assert.Equal("CommandWithResponse", method.Name);
            Assert.Equal(typeof(string), method.GetGenericArguments()[0]);
            if (query.Sql == CompanyBankSql.FindHash) { Events.Add("exact-hash"); return Task.FromResult(ExistingHash); }
            Assert.Equal(CompanyBankSql.LatestRevision, query.Sql);
            Events.Add("latest");
            Assert.Null(query.Parameters.GetType().GetProperty("ticks"));
            return Task.FromResult(Latest);
        }
    }
    public class Proxy : DispatchProxy
    {
        internal object State = null!;
        internal bool Transaction;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => ((Database)State).Invoke(method!, args ?? [], Transaction);
    }
}
