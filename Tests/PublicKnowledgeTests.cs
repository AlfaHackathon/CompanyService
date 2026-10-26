using System.Net;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Companies;
using Api.Contracts.Knowledge;
using Api.Contracts.PublicKnowledge;
using Api.Controllers;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Services.PublicKnowledge;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public sealed partial class PublicKnowledgeTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
    private static readonly string Hash = new('a', 64);
    private static PutCompanyKnowledgeRequest Input() => new(" Own description ", ["45.11"], ["cost exposure"], [new("own", "v1")]);
    internal static Fixture Setup()
    {
        var companies = new Companies { Current = new() { Id = CompanyId, CreatorId = Actor, Name = "Operational name", BankProfileId = "U_96", CreatedAt = At.UtcDateTime } };
        var materials = new Materials();
        materials.Stored[("own", "v1")] = new("own", "v1", "synthetic", "company", "Own evidence", "Own original", At, null, null, [CompanyId], [], Hash, new("uploaded", null, At, "unknown"));
        var operations = new Operations();
        var profiles = new Profiles();
        return new(new(companies, profiles, materials, operations), companies, materials, operations, profiles);
    }
    [Fact]
    public async Task MaterialWriteFreezesServerIdentityAndSurvivesLostResponseWithSameKey()
    {
        var f = Setup();
        f.Materials.LoseNextSave = true;
        var request = new CreateCompanyMaterialRequest("Declaration", "User declared content");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.CreateMaterial(Actor, request, "create-1", default)).Error!.HttpStatusCode);
        Assert.Single(f.Operations.Items);
        Assert.False(f.Operations.Items.Single().Value.Completed);
        var saved = (await f.Service.CreateMaterial(Actor, request, "create-1", default)).Content!;
        Assert.Equal(2, f.Materials.Writes.Count);
        Assert.Equal(JsonSerializer.Serialize(f.Materials.Writes[0], KnowledgeJson.Options), JsonSerializer.Serialize(f.Materials.Writes[1], KnowledgeJson.Options));
        Assert.StartsWith("user-material-", saved.MaterialId);
        Assert.Equal("user-declaration", saved.SourceId);
        Assert.Equal("unknown", saved.TextCompleteness);
        Assert.Equal("uploaded", saved.Provenance!.Acquisition);
        Assert.Null(saved.Provenance.SourceUrl);
        Assert.Equal([CompanyId], f.Materials.Writes[0].CompanyIds);
        Assert.Empty(f.Materials.Writes[0].BankProfileIds);
        Assert.True(f.Operations.Items.Single().Value.Completed);
        Assert.Null((await f.Service.CreateMaterial(Actor, request, "create-1", default)).Error);
        Assert.Equal(2, f.Materials.Writes.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Service.CreateMaterial(Actor, request with { Text = "different" }, "create-1", default)).Error!.HttpStatusCode);
    }
    [Fact]
    public async Task ProfileUsesOperationalIdentityExactEvidenceAndNormalizedReplay()
    {
        var f = Setup();
        var first = (await f.Service.PutKnowledge(Actor, Input(), "profile-1", default)).Content!;
        Assert.Equal("Operational name", first.Name);
        Assert.Equal(CompanyId, first.CompanyId);
        Assert.Equal(Hash, Assert.Single(first.SourceRefs).ContentHash);
        Assert.Equal("U_96", f.Profiles.Current!.BankProfileId);
        var repeat = await f.Service.PutKnowledge(Actor, Input() with { Description = "Own description" }, "profile-1", default);
        Assert.Equal(first.RevisionId, repeat.Content!.RevisionId);
        Assert.Equal(1, f.Profiles.Writes);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Service.PutKnowledge(Actor, Input() with { Description = "Changed" }, "profile-1", default)).Error!.HttpStatusCode);
        Assert.Null((await f.Service.GetKnowledge(Actor, default)).Error);
        f.Materials.Stored[("own", "v1")] = f.Materials.Stored[("own", "v1")] with { BankProfileIds = ["U_97"] };
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.GetKnowledge(Actor, default)).Error!.HttpStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.PutKnowledge(Actor, Input(), "profile-1", default)).Error!.HttpStatusCode);
    }
    [Fact]
    public async Task MissingMembershipAndNonCreatorCannotReadOrWriteBeyondCurrentScope()
    {
        var f = Setup();
        f.Companies.Current = null;
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.GetKnowledge(Actor, default)).Error!.HttpStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error!.HttpStatusCode);
        Assert.Equal(0, f.Materials.Reads);
        f = Setup();
        f.Companies.Current!.CreatorId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Service.PutKnowledge(Actor, Input(), "key", default)).Error!.HttpStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error!.HttpStatusCode);
        Assert.Null((await f.Service.GetMaterial(Actor, "own", "v1", default)).Error);
        Assert.Empty(f.Operations.Items);
    }
    [Theory]
    [InlineData("company")]
    [InlineData("bank")]
    [InlineData("kind")]
    [InlineData("identity")]
    [InlineData("hash")]
    [InlineData("future")]
    public async Task ExactReadAndProfileRejectIncompatibleEvidenceBeforeProjection(string cause)
    {
        var f = Setup();
        var source = f.Materials.Stored[("own", "v1")];
        source = cause switch
        {
            "company" => source with { CompanyIds = [Guid.NewGuid()] },
            "bank" => source with { BankProfileIds = ["U_97"] },
            "kind" => source with { Kind = "market", CompanyIds = [] },
            "identity" => source with { VersionId = "v2" },
            "hash" => source with { ContentHash = "invalid" },
            _ => source with { KnownAt = DateTimeOffset.UtcNow.AddDays(1) }
        };
        f.Materials.Stored[("own", "v1")] = source;
        Assert.NotNull((await f.Service.GetMaterial(Actor, "own", "v1", default)).Error);
        Assert.NotNull((await f.Service.PutKnowledge(Actor, Input(), "key", default)).Error);
        Assert.Equal(0, f.Profiles.Writes);
    }
    [Fact]
    public async Task EveryReplayRechecksCreatorAndMembershipBeforeUsingReceipt()
    {
        var f = Setup();
        Assert.Null((await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error);
        f.Companies.Current!.CreatorId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error!.HttpStatusCode);
        Assert.Single(f.Materials.Writes);
        f.Companies.Current = null;
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error!.HttpStatusCode);
    }
    [Fact]
    public async Task MismatchedSuccessfulUpstreamResponseDoesNotCompleteReceipt()
    {
        var f = Setup();
        f.Materials.CorruptResponse = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), "key", default)).Error!.HttpStatusCode);
        Assert.False(f.Operations.Items.Single().Value.Completed);
    }
    [Fact]
    public async Task BodyAndKeyBoundsFailBeforeReservationIncludingEscapedCyrillicPayload()
    {
        var f = Setup();
        foreach (var key in new string?[] { null, "", "with space", new('x', 129), "не-ascii" })
            Assert.Equal(HttpStatusCode.BadRequest, (await f.Service.CreateMaterial(Actor, new("Title", "Text"), key, default)).Error!.HttpStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Service.CreateMaterial(Actor, new("Title", new string('я', 200000)), "large", default)).Error!.HttpStatusCode);
        Assert.Empty(f.Operations.Items);
        Assert.Empty(f.Materials.Writes);
    }
    [Fact]
    public async Task ListFreezesSnapshotAndRejectsCursorAfterBankOrCompanyChange()
    {
        var f = Setup();
        f.Materials.Next = "inner-cursor";
        var first = (await f.Service.ListMaterials(Actor, 2, null, default)).Content!;
        Assert.Equal("canonical_changes_only", first.Coverage);
        Assert.NotNull(first.NextCursor);
        var next = (await f.Service.ListMaterials(Actor, 2, first.NextCursor, default)).Content!;
        Assert.Equal(first.AsOf, next.AsOf);
        Assert.Equal("inner-cursor", f.Materials.LastInner);
        Assert.Equal("U_96", f.Materials.LastBank);
        f.Companies.Current!.BankProfileId = "U_97";
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Service.ListMaterials(Actor, 2, first.NextCursor, default)).Error!.HttpStatusCode);
    }
    [Fact]
    public void PublicRequestsRejectCallerIdentityHashTimeAndProvenance()
    {
        foreach (var property in new[] { "companyId", "materialId", "knownAt", "contentHash", "provenance" })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateCompanyMaterialRequest>("{\"title\":\"Title\",\"text\":\"Text\",\"" + property + "\":\"spoofed\"}", KnowledgeJson.Options));
        }
    }
    [Fact]
    public async Task ActualControllerRejectsUnknownRepeatedQueriesAndMissingWriteKey()
    {
        var f = Setup();
        var controller = new CompanyKnowledgeController(f.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["X-User-Id"] = Actor.ToString("D");
        controller.Request.QueryString = new QueryString("?companyId=spoofed");
        Assert.IsType<BadRequestObjectResult>(await controller.Get(default));
        controller.Request.QueryString = new QueryString("?limit=1&limit=2");
        Assert.IsType<BadRequestObjectResult>(await controller.Materials());
        controller.Request.QueryString = QueryString.Empty;
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Create(new("Title", "Text"), default));
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(f.Operations.Items);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    internal sealed record Fixture(PublicKnowledgeService Service, Companies Companies, Materials Materials, Operations Operations, Profiles Profiles);
    internal sealed class Operations : IKnowledgeOperationStore
    {
        public readonly Dictionary<string, KnowledgeOperation> Items = new();
        private static string Id(Guid actor, Guid company, string operation, string key) => $"{actor}/{company}/{operation}/{key}";
        public Task<KnowledgeOperation?> Find(Guid actor, Guid company, string operation, string key, string hash)
        {
            Items.TryGetValue(Id(actor, company, operation, key), out var value);
            if (value is not null && value.RequestHash != hash) throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Conflict");
            return Task.FromResult(value);
        }
        public async Task<KnowledgeOperation> Reserve(Guid actor, Guid company, string operation, string key, string hash, Func<DateTimeOffset, string> freeze)
        {
            var existing = await Find(actor, company, operation, key, hash);
            if (existing is not null) return existing;
            var at = DateTimeOffset.UtcNow;
            var value = new KnowledgeOperation(actor, company, operation, key, hash, at, freeze(at), false);
            Items.Add(Id(actor, company, operation, key), value);
            return value;
        }
        public Task Complete(KnowledgeOperation operation)
        {
            var id = Id(operation.ActorId, operation.CompanyId, operation.Operation, operation.Key);
            Items[id] = Items[id] with { Completed = true };
            return Task.CompletedTask;
        }
    }
    internal sealed class Materials : ICompanyMaterialsClient
    {
        public readonly Dictionary<(string, string), ScopedCompanyMaterial> Stored = new();
        public readonly List<CompanyMaterialWrite> Writes = new();
        public bool LoseNextSave, CorruptResponse;
        public int Reads;
        public string? Next, LastInner, LastBank;
        public Task<ScopedCompanyMaterial> Get(string id, string version, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Stored[(id, version)]);
        }
        public Task<ScopedCompanyMaterial> Save(CompanyMaterialWrite p, CancellationToken ct)
        {
            Writes.Add(p);
            var value = new ScopedCompanyMaterial(p.MaterialId, p.VersionId, p.SourceId, p.Kind, p.Title, p.Text, p.KnownAt, p.PublishedAt, p.CorrectsVersionId, p.CompanyIds, p.BankProfileIds, Hash, p.Provenance);
            Stored[(p.MaterialId, p.VersionId)] = value;
            if (LoseNextSave) { LoseNextSave = false; throw new HttpRequestException("synthetic lost response"); }
            return Task.FromResult(CorruptResponse ? value with { Text = "foreign text" } : value);
        }
        public Task<CompanyMaterialsResponse> List(Guid company, string? bank, DateTimeOffset at, int limit, string? cursor, CancellationToken ct)
        {
            LastInner = cursor; LastBank = bank;
            return Task.FromResult(new CompanyMaterialsResponse(company, at, [], Next, "canonical_changes_only"));
        }
    }
    internal sealed class Profiles : IPublicCompanyProfiles
    {
        public CompanyProfile? Current;
        public int Writes;
        public Task<CompanyProfile> Get(Guid company, DateTimeOffset asOf) => Task.FromResult(Current ?? throw PublicKnowledgeError.Missing());
        public Task<CompanyProfile> Save(CompanyProfile profile) { Writes++; Current = profile; return Task.FromResult(profile); }
    }
    internal sealed class Companies : ICompanyRepository
    {
        public Company? Current;
        public int Reads;
        public Task<Company?> GetByUserAsync(Guid user, CancellationToken ct = default) { Reads++; return Task.FromResult(Current); }
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Company?> TryCreateAsync(Company company, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetIndustriesAsync(Guid company, IReadOnlyCollection<Guid> industries, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Industry>> GetIndustriesAsync(Guid company, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<CompanyMember>> GetMembersAsync(Guid company, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Company>> GetChildrenAsync(Guid parent, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JoinCompanyOutcome> TryJoinAsync(Guid company, Guid user, Func<Task<bool>> claim, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
