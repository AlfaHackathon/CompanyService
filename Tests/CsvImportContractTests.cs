using System.Reflection;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using Api.CsvImport;
using Api.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class CsvImportContractTests
{
    private static CsvBatch Batch()
    {
        var manifest=CsvProtocol.Serialize(new {format=1,source_id="synthetic",version="v1",
            files=new {groups=new {sha256=new string('a',64),headers=new[]{"GROUP_ID"},rows=1}}, expected=new {groups=1}});
        return new("synthetic","v1",CsvProtocol.Hash(manifest),manifest,"groups",1,
            [new(1,["11111111-1111-4111-8111-111111111111"])]);
    }
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CsvImport:Enabled","true"}}).Build();

    [Fact]
    public void ManifestHashAndContiguousRowsAreCheckedWithoutAnyDatabase()
    {
        using var valid=CsvProtocol.Validate(Batch(),["groups"]);
        Assert.Throws<InvalidDataException>(()=>CsvProtocol.Validate(Batch() with {ManifestHash=new string('b',64)},["groups"]));
        Assert.Throws<InvalidDataException>(()=>CsvProtocol.Validate(Batch() with {Rows=[new(2,["value"])]},["groups"]));
    }
    [Fact]
    public async Task CommittedBatchReplayPrecedesContextOrTransactionAndReturnsOriginalReceipt()
    {
        var batch=Batch(); var hash=CsvProtocol.Hash(CsvProtocol.Serialize(batch));
        var state=new Fake {Replay=CsvProtocol.Serialize(CsvProtocol.Receipt(batch,hash))};
        var controller=new CsvController(state.Context(),null!,null!,Config());
        var result=Assert.IsType<ContentResult>(await controller.Batch(batch,CancellationToken.None));
        Assert.Equal(state.Replay,result.Content);
        Assert.DoesNotContain("begin",state.Events);
    }
    [Fact]
    public async Task ChangedBodyUnderCommittedCheckpointIsConflictWithoutNewWrite()
    {
        var state=new Fake {Replay=CsvProtocol.Serialize(CsvProtocol.Receipt(Batch(),new string('a',64)))};
        var result=Assert.IsType<ObjectResult>(await new CsvController(state.Context(),null!,null!,Config()).Batch(Batch(),CancellationToken.None));
        Assert.Equal(409,result.StatusCode); Assert.DoesNotContain("begin",state.Events);
    }
    [Fact]
    public async Task VerificationDoesNotMarkDoneForMissingRows()
    {
        var state=new Fake {Count=0};
        var result=Assert.IsType<ObjectResult>(await new CsvController(state.Context(),null!,null!,Config()).Verify(Batch()));
        Assert.Equal(409,result.StatusCode); Assert.DoesNotContain("mark",state.Events); Assert.DoesNotContain("commit",state.Events);
    }
    [Fact]
    public async Task VerificationMarkerAndSnapshotShareTheOwnerTransaction()
    {
        var state=new Fake {Count=1};
        Assert.IsType<ContentResult>(await new CsvController(state.Context(),null!,null!,Config()).Verify(Batch()));
        Assert.Equal(new[]{"begin","lock","bound","snapshot","next","mark","commit","dispose"},state.Events);
    }
    private sealed class Fake
    {
        public string Replay=""; public int Count; public readonly List<string> Events=[];
        public IDapperContext Context()
        {
            var proxy=DispatchProxy.Create<IDapperContext,Proxy>(); ((Proxy)(object)proxy).State=this; return proxy;
        }
        private static object? Reply(MethodInfo method,object? value)
        {
            if(method.ReturnType==typeof(Task)) return Task.CompletedTask;
            var type=method.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value]);
        }
        public object? Call(MethodInfo method,object?[] args,bool tx)
        {
            if(method.Name=="BeginTransaction") {Events.Add("begin"); var p=DispatchProxy.Create<ITransaction,Proxy>(); ((Proxy)(object)p).State=this; ((Proxy)(object)p).Transaction=true; return p;}
            if(method.Name is "Commit" or "Dispose") {Events.Add(method.Name.ToLowerInvariant()); return null;}
            var q=(IQueryObject)args[0]!;
            if(q.Sql==CsvSql.Replay) return Reply(method,Replay);
            if(q.Sql==KnowledgeSql.WriteLock) {Events.Add("lock"); return Reply(method,null);}
            if(q.Sql==CsvSql.Bound) {Events.Add("bound"); return Reply(method,true);}
            if(q.Sql==CsvSql.VerifyRaw) {Events.Add("snapshot"); return Reply(method,CsvProtocol.Serialize(new {rows=Count,groups=Count}));}
            if(q.Sql==CsvSql.Next) {Events.Add("next"); return Reply(method,(long)Count+1);}
            if(q.Sql==CsvSql.MarkVerified) {Assert.True(tx); Events.Add("mark"); return Reply(method,null);}
            throw new InvalidOperationException("Unexpected persistence dispatch");
        }
    }
    public class Proxy:DispatchProxy
    {
        internal object State=null!; internal bool Transaction;
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>((Fake)State).Call(targetMethod!,args??[],Transaction);
    }
}
