using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Contracts.PublicKnowledge;
using Api.Controllers;
using Api.Repositories;
using Api.Services;
using Api.Services.PublicKnowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class CompanyBankProfileCaptureTests
{
    private static readonly Guid Company = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public async Task InternalRoutesRequireSecretStrictCaptureBodyAndExplicitAsOfWithoutGetSideEffects()
    {
        var f = new Fixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["Internal:Secret"] = "synthetic-bank-capture";
        builder.Services.AddSingleton(f.Service);
        builder.Services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
            .AddApplicationPart(typeof(CompanyBankProfilesController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var http = app.GetTestClient();
        var prefix = "/company-service/internal/bank-profiles/";
        var capture = prefix + Company.ToString("D") + "/capture";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(capture, new { captureId = "capture", bankData = new { } })).StatusCode);
        http.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-bank-capture");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(capture, new { captureId = "capture", bankData = new { }, knownAt = "spoofed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(prefix + Company.ToString("D"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.GetAsync(capture)).StatusCode);
        Assert.Empty(f.Materials.Writes);
        var saved = await http.PostAsJsonAsync(capture, new { captureId = "capture", bankData = CompanyBankDataTests.AllFields() });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = (await saved.Content.ReadFromJsonAsync<CompanyBankProfile>())!;
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(prefix + Company.ToString("D") + "?asOf=" + Uri.EscapeDataString(profile.KnownAt.ToString("O")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(prefix + "versions?limit=101")).StatusCode);
        Assert.Single(f.Materials.Writes);
    }

    [Fact]
    public async Task CanonicalOnlyCompanyNeedsNoOperationalNameAccountOrMembershipAndKeepsAllFields()
    {
        var f = new Fixture();
        var before = DateTimeOffset.UtcNow;
        var result = await f.Service.Capture(Company, new("capture-1", CompanyBankDataTests.AllFields()), default);
        Assert.Null(result.Error);
        var profile = result.Content!;
        Assert.Equal(Company, profile.CompanyId);
        Assert.InRange(profile.KnownAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(Json(CompanyBankDataTests.AllFields()), Json(profile.BankData));
        Assert.Equal("company-bank-profile", profile.SourceId);
        var material = Assert.Single(f.Materials.Writes);
        Assert.Equal("company", material.Kind);
        Assert.Equal([Company], material.CompanyIds);
        Assert.Empty(material.BankProfileIds);
        Assert.Equal("event_payload", profile.Provenance.Acquisition);
        Assert.Equal(profile.KnownAt, profile.Provenance.RetrievedAt);
        Assert.Null(profile.Provenance.SourceUrl);
        Assert.Null(profile.Provenance.OriginVersionId);
        Assert.Null(profile.Provenance.Locator);
        Assert.Equal(Hash(Json(material)), Assert.Single(profile.SourceRefs).ContentHash);
        var snapshot = JsonSerializer.Deserialize<CompanyBankSnapshot>(material.Text, KnowledgeJson.Options)!;
        Assert.Equal(Json(profile.BankData), Json(snapshot.BankData));
        Assert.Equal(Company, snapshot.CompanyId);
        var json = JsonSerializer.SerializeToElement(profile, KnowledgeJson.Options);
        Assert.False(json.TryGetProperty("name", out _));
        Assert.False(json.TryGetProperty("creatorId", out _));
        Assert.False(json.TryGetProperty("email", out _));
        Assert.False(json.TryGetProperty("parentCompanyId", out _));
    }

    [Fact]
    public async Task ExplicitCaptureSupportsNullsAndImmutableReplayAfterUnknownMaterialOutcome()
    {
        var f = new Fixture();
        f.Materials.LoseNextSave = true;
        var request = new CaptureCompanyBankProfileRequest("same", new() { Region = "Initial declared region" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(Company, request, default)).Error!.HttpStatusCode);
        var captured = await f.Service.Capture(Company, request, default);
        Assert.Null(captured.Error);
        Assert.Equal(Json(f.Materials.Writes[0]), Json(f.Materials.Writes[1]));
        Assert.Null(captured.Content!.BankData.RevenueCompany);
        Assert.Null(captured.Content.BankData.GroupId);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Service.Capture(Company, request with { BankData = request.BankData with { Region = "Changed" } }, default)).Error!.HttpStatusCode);
        Assert.Single(f.Profiles.Items);
    }

    [Fact]
    public async Task HistoricalReadDoesNotUseLatestOrOperationalFallbackAndVersionPagesAreBounded()
    {
        var f = new Fixture();
        var first = (await f.Service.Capture(Company, new("one", new() { Region = "R1" }), default)).Content!;
        var second = (await f.Service.Capture(Company, new("two", new() { Region = "R2" }), default)).Content!;
        Assert.Equal(HttpStatusCode.NotFound, (await f.Service.Get(Company, first.KnownAt.AddTicks(-1))).Error!.HttpStatusCode);
        Assert.Equal(first.RevisionId, (await f.Service.Get(Company, first.KnownAt)).Content!.RevisionId);
        Assert.Equal(second.RevisionId, (await f.Service.Get(Company, second.KnownAt)).Content!.RevisionId);
        var page = (await f.Service.Versions(null, 1)).Content!;
        Assert.Equal(first.RevisionId, Assert.Single(page.Profiles).RevisionId);
        Assert.NotNull(page.NextCursor);
        Assert.Equal(second.RevisionId, Assert.Single((await f.Service.Versions(page.NextCursor, 1)).Content!.Profiles).RevisionId);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Service.Versions("-1", 100)).Error!.HttpStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Service.Versions(null, 101)).Error!.HttpStatusCode);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("text")]
    [InlineData("company")]
    [InlineData("source")]
    public async Task IncorrectContextResponseCannotPublishAnOwnerProfile(string cause)
    {
        var f = new Fixture();
        f.Materials.Corrupt = cause;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(Company, new("bad", new()), default)).Error!.HttpStatusCode);
        Assert.Empty(f.Profiles.Items);
    }

    [Fact]
    public async Task EarlierFrozenCaptureCannotOverwriteLaterCommittedBankProfileAndExactReplayIsSafe()
    {
        var f = new Fixture();
        f.Materials.OnSave = p => f.Profiles.Items.Add(new CompanyBankProfile(Company, "later", p.KnownAt.AddTicks(1), new(), "company-bank-profile", [], p.Provenance!));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Service.Capture(Company, new("earlier", new()), default)).Error!.HttpStatusCode);
        Assert.Equal("later", Assert.Single(f.Profiles.Items).RevisionId);
    }

    [Fact]
    public async Task UnknownOwnerSaveOutcomeReplaysExactRevisionWithoutReplacingLaterHead()
    {
        var f = new Fixture();
        f.Profiles.LoseNextSave = true;
        var firstRequest = new CaptureCompanyBankProfileRequest("saved-A", new() { Region = "A" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(Company, firstRequest, default)).Error!.HttpStatusCode);
        var original = Assert.Single(f.Profiles.Items);
        var later = (await f.Service.Capture(Company, new("later-B", new() { Region = "B" }), default)).Content!;
        var replay = await f.Service.Capture(Company, firstRequest, default);
        Assert.Null(replay.Error);
        Assert.Equal(original.RevisionId, replay.Content!.RevisionId);
        Assert.Equal(later.RevisionId, f.Profiles.Items.MaxBy(p => p.KnownAt)!.RevisionId);
        Assert.Equal(2, f.Profiles.Items.Count);
    }

    private sealed class Fixture
    {
        public readonly Materials Materials = new();
        public readonly Profiles Profiles = new();
        private readonly PublicKnowledgeTests.Operations operations = new();
        public CompanyBankProfileService Service => new(Profiles, Materials, operations);
    }
    private sealed class Profiles : ICompanyBankProfiles
    {
        public readonly List<CompanyBankProfile> Items = [];
        public bool LoseNextSave;
        public Task<CompanyBankProfile?> Get(Guid companyId, DateTimeOffset asOf) => Task.FromResult(Items.Where(p => p.CompanyId == companyId && p.KnownAt <= asOf).MaxBy(p => p.KnownAt));
        public Task<CompanyBankProfilePage> Versions(long after, int limit)
        {
            var page = Items.Skip(checked((int)after)).Take(limit).ToArray();
            return Task.FromResult(new CompanyBankProfilePage(page, page.Length == limit ? (after + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
        public Task<CompanyBankProfile> Save(CompanyBankProfile profile, string? expectedRevisionId)
        {
            var existing = Items.SingleOrDefault(p => p.CompanyId == profile.CompanyId && p.RevisionId == profile.RevisionId);
            if (existing is not null) { Assert.Equal(Json(existing), Json(profile)); return Task.FromResult(existing); }
            if (Items.Where(p => p.CompanyId == profile.CompanyId).MaxBy(p => p.KnownAt)?.RevisionId != expectedRevisionId)
                throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Bank profile changed");
            Items.Add(profile);
            if (LoseNextSave) { LoseNextSave = false; throw new HttpRequestException("Synthetic unknown owner write outcome"); }
            return Task.FromResult(profile);
        }
    }
    private sealed class Materials : ICompanyMaterialsClient
    {
        public readonly List<CompanyMaterialWrite> Writes = [];
        private readonly Dictionary<(string, string), ScopedCompanyMaterial> stored = [];
        public bool LoseNextSave;
        public string? Corrupt;
        public Action<CompanyMaterialWrite>? OnSave;
        public Task<ScopedCompanyMaterial> Get(string id, string version, CancellationToken ct) => Task.FromResult(stored[(id, version)]);
        public Task<ScopedCompanyMaterial> Save(CompanyMaterialWrite p, CancellationToken ct)
        {
            Writes.Add(p);
            var value = new ScopedCompanyMaterial(p.MaterialId, p.VersionId, p.SourceId, p.Kind, p.Title, p.Text, p.KnownAt, p.PublishedAt, p.CorrectsVersionId,
                p.CompanyIds, p.BankProfileIds, Hash(Json(p)), p.Provenance);
            stored[(p.MaterialId, p.VersionId)] = value;
            OnSave?.Invoke(p);
            if (LoseNextSave) { LoseNextSave = false; throw new HttpRequestException("Synthetic unknown write outcome"); }
            return Task.FromResult(Corrupt switch
            {
                "hash" => value with { ContentHash = new string('f', 64) },
                "text" => value with { Text = "Other" },
                "company" => value with { CompanyIds = [Guid.NewGuid()] },
                "source" => value with { SourceId = "documentary-source" },
                _ => value
            });
        }
        public Task<CompanyMaterialsResponse> List(Guid company, string? bank, DateTimeOffset at, int limit, string? cursor, CancellationToken ct) => throw new NotSupportedException();
    }
}
