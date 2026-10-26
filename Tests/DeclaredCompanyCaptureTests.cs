using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Context;
using Api.Contracts.Knowledge;
using Api.Contracts.PublicKnowledge;
using Api.Controllers;
using Api.Services;
using Api.Services.PublicKnowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class DeclaredCompanyCaptureTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, KnowledgeJson.Options);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public async Task CaptureRouteRequiresInternalSecretAndExplicitPostWithOnlyCaptureId()
    {
        var f = new Fixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["Internal:Secret"] = "synthetic-capture-secret";
        builder.Services.AddSingleton(f.Service);
        builder.Services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
            .AddApplicationPart(typeof(DeclaredCompanyCaptureController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var http = app.GetTestClient();
        var route = $"/company-service/internal/knowledge/companies/{f.CompanyId:D}/capture-declared-profile";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(route, new { captureId = "route" })).StatusCode);
        Assert.Equal(0, f.Companies.Reads);
        http.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-capture-secret");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(route, new { captureId = "route", knownAt = "2000-01-01" })).StatusCode);
        Assert.Equal(0, f.Companies.Reads);
        var response = await http.PostAsJsonAsync(route, new { captureId = "route" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<CompanyProfile>())!.DeclaredProfile);
        Assert.Single(f.Materials.Writes);
    }

    [Fact]
    public async Task CaptureFreezesExactOperationalFieldsAndProvenanceWithoutInventingFinancialExposure()
    {
        var f = new Fixture();
        f.Companies.Current!.Region = "Synthetic region A";
        f.Companies.Current.Revenue = 9999999999999999.99m;
        f.Companies.Industries = [new() { Id = Guid.NewGuid(), Code = "C", Name = "Manufacturing" }];
        var before = DateTimeOffset.UtcNow;
        var result = await f.Service.Capture(f.CompanyId, new("capture-1"), default);
        var profile = result.Content!;
        Assert.Null(result.Error);
        Assert.InRange(profile.KnownAt, before, DateTimeOffset.UtcNow);
        Assert.Equal("Synthetic region A", profile.DeclaredProfile!.Region);
        Assert.Equal(["C"], Assert.IsType<string[]>(profile.DeclaredProfile.IndustrySections));
        Assert.Equal("9999999999999999.99", profile.DeclaredProfile.Revenue!.Amount);
        Assert.Null(profile.DeclaredProfile.Revenue.Currency);
        Assert.Null(profile.DeclaredProfile.Revenue.Period);
        Assert.Null(profile.DeclaredProfile.CountOfEmployees);
        Assert.Empty(profile.IndustryCodes);
        Assert.Empty(profile.Exposures);
        var material = Assert.Single(f.Materials.Writes);
        Assert.Equal("company-operational-declared-profile", material.SourceId);
        Assert.Equal("company", material.Kind);
        Assert.Equal([f.CompanyId], material.CompanyIds);
        Assert.Empty(material.BankProfileIds);
        Assert.Equal("event_payload", material.Provenance!.Acquisition);
        Assert.Equal(material.KnownAt, material.Provenance.RetrievedAt);
        Assert.Null(material.Provenance.SourceUrl);
        Assert.Null(material.Provenance.Locator);
        var reference = Assert.Single(profile.DeclaredProfile.SourceRefs);
        Assert.Equal(Hash(Json(material)), reference.ContentHash);
        Assert.Equal(material.MaterialId, reference.MaterialId);
        Assert.Equal(material.VersionId, reference.VersionId);
        var payload = JsonSerializer.Deserialize<DeclaredCompanySnapshot>(material.Text, KnowledgeJson.Options)!;
        Assert.Equal(f.CompanyId, payload.CompanyId);
        Assert.Equal(profile.DeclaredProfile.Region, payload.Region);
        Assert.Equal(5, JsonSerializer.Deserialize<JsonElement>(material.Text).EnumerateObject().Count());
        Assert.DoesNotContain("sourceRefs", material.Text);
    }

    [Fact]
    public async Task RetryResumesFrozenSnapshotAfterLostResponseAndDoesNotCaptureChangedCurrentFields()
    {
        var f = new Fixture();
        f.Companies.Current!.Region = "Before";
        f.Materials.LoseNextSave = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(f.CompanyId, new("retry"), default)).Error!.HttpStatusCode);
        f.Companies.Current.Region = "After";
        f.Companies.Current.CountOfEmployees = 42;
        var retry = await f.Service.Capture(f.CompanyId, new("retry"), default);
        Assert.Null(retry.Error);
        Assert.Equal("Before", retry.Content!.DeclaredProfile!.Region);
        Assert.Null(retry.Content.DeclaredProfile.CountOfEmployees);
        Assert.Equal(Json(f.Materials.Writes[0]), Json(f.Materials.Writes[1]));
        var reads = f.Companies.Reads;
        var replay = await f.Service.Capture(f.CompanyId, new("retry"), default);
        Assert.Equal(Json(retry.Content), Json(replay.Content));
        Assert.Equal(reads, f.Companies.Reads);
        Assert.Equal(2, f.Materials.Writes.Count);
        Assert.Single(f.Profiles.Versions);
    }

    [Fact]
    public async Task RetryAfterUnknownProfileWriteOutcomeRetainsTheSameImmutableRevision()
    {
        var f = new Fixture();
        f.Profiles.LoseNextSave = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(f.CompanyId, new("lost-profile"), default)).Error!.HttpStatusCode);
        var revision = Assert.Single(f.Profiles.Versions).RevisionId;
        f.Companies.Current!.Region = "Changed after unknown outcome";
        var repeated = await f.Service.Capture(f.CompanyId, new("lost-profile"), default);
        Assert.Null(repeated.Error);
        Assert.Equal(revision, repeated.Content!.RevisionId);
        Assert.Null(repeated.Content.DeclaredProfile!.Region);
        Assert.Single(f.Profiles.Versions);
    }

    [Fact]
    public async Task CaptureDoesNotReplaceKnowledgeChangedBetweenReadAndReservation()
    {
        var f = new Fixture();
        f.Materials.OnSave = material => f.Profiles.Versions.Add(new CompanyProfile
        {
            CompanyId = f.CompanyId,
            RevisionId = "concurrent-knowledge",
            Name = "Concurrent",
            Description = "New documentary knowledge",
            KnownAt = material.KnownAt.AddTicks(-1)
        });
        Assert.Equal(HttpStatusCode.Conflict, (await f.Service.Capture(f.CompanyId, new("race"), default)).Error!.HttpStatusCode);
        Assert.Equal("concurrent-knowledge", Assert.Single(f.Profiles.Versions).RevisionId);
    }

    [Fact]
    public async Task EarlierFrozenCaptureCannotPublishAfterLaterKnownRevisionCommitsFirst()
    {
        var f = new Fixture();
        var original = new CompanyProfile
        {
            CompanyId = f.CompanyId,
            RevisionId = "R0",
            Name = "Original",
            Description = "Original knowledge",
            KnownAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        f.Profiles.Versions.Add(original);
        DateTimeOffset? frozenCaptureTime = null;
        f.Materials.OnSave = material =>
        {
            frozenCaptureTime = material.KnownAt;
            // B was based on R0, froze after A, and committed while A was publishing its material.
            f.Profiles.Versions.Add(new CompanyProfile
            {
                CompanyId = f.CompanyId,
                RevisionId = "B",
                Name = "Public update",
                Description = "Later public knowledge",
                KnownAt = material.KnownAt.AddTicks(1),
                DeclaredProfile = null
            });
        };
        var result = await f.Service.Capture(f.CompanyId, new("earlier-frozen-A"), default);
        Assert.Equal(HttpStatusCode.Conflict, result.Error!.HttpStatusCode);
        var current = f.Profiles.Versions.MaxBy(p => p.KnownAt)!;
        Assert.Equal("B", current.RevisionId);
        Assert.True(current.KnownAt > frozenCaptureTime);
        Assert.Null(current.DeclaredProfile);
        Assert.Equal(2, f.Profiles.Versions.Count);
        Assert.DoesNotContain(f.Profiles.Versions, p => p.RevisionId.StartsWith("declared-capture-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingImmutableCaptureReplaySucceedsWithoutReplacingLaterCommittedKnowledge()
    {
        var f = new Fixture();
        f.Profiles.LoseNextSave = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(f.CompanyId, new("already-written-A"), default)).Error!.HttpStatusCode);
        var original = Assert.Single(f.Profiles.Versions);
        f.Profiles.Versions.Add(new CompanyProfile
        {
            CompanyId = f.CompanyId,
            RevisionId = "later-B",
            Name = "Later knowledge",
            Description = "Later public knowledge",
            KnownAt = original.KnownAt.AddTicks(1)
        });
        var replay = await f.Service.Capture(f.CompanyId, new("already-written-A"), default);
        Assert.Null(replay.Error);
        Assert.Equal(original.RevisionId, replay.Content!.RevisionId);
        Assert.Equal("later-B", f.Profiles.Versions.MaxBy(p => p.KnownAt)!.RevisionId);
        Assert.Equal(2, f.Profiles.Versions.Count);
    }

    [Fact]
    public async Task CapturePreservesKnowledgeAndNullsAndHasNoHistoricalBackfill()
    {
        var f = new Fixture();
        var previous = new CompanyProfile
        {
            CompanyId = f.CompanyId,
            RevisionId = "documented-r1",
            Name = "Documented company",
            Description = "Original source-backed profile",
            IndustryCodes = ["28.1"],
            Exposures = ["Original explicitly declared feature"],
            KnownAt = DateTimeOffset.UtcNow.AddDays(-1),
            SourceRefs = [new() { MaterialId = "existing", VersionId = "v1", ContentHash = new string('a', 64) }]
        };
        f.Profiles.Versions.Add(previous);
        var profile = (await f.Service.Capture(f.CompanyId, new("capture"), default)).Content!;
        Assert.Equal(previous.Name, profile.Name);
        Assert.Equal(previous.Description, profile.Description);
        Assert.Equal(previous.IndustryCodes, profile.IndustryCodes);
        Assert.Equal(previous.Exposures, profile.Exposures);
        Assert.Contains(profile.SourceRefs, r => r.MaterialId == "existing");
        Assert.Null(profile.DeclaredProfile!.Region);
        Assert.Null(profile.DeclaredProfile.IndustrySections);
        Assert.Null(profile.DeclaredProfile.Revenue);
        Assert.Null(profile.DeclaredProfile.CountOfEmployees);
        Assert.Null((await f.Profiles.Get(f.CompanyId, profile.KnownAt.AddTicks(-1))).DeclaredProfile);
        Assert.Equal(profile.RevisionId, (await f.Profiles.Get(f.CompanyId, profile.KnownAt)).RevisionId);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("text")]
    [InlineData("source")]
    [InlineData("company")]
    [InlineData("time")]
    public async Task IncompatibleContextWriteResponseNeverPublishesProfile(string cause)
    {
        var f = new Fixture();
        f.Materials.Corrupt = cause;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Service.Capture(f.CompanyId, new("bad-response"), default)).Error!.HttpStatusCode);
        Assert.Empty(f.Profiles.Versions);
    }

    [Fact]
    public async Task UnknownCaptureValuesStayNullAndZeroIsPreserved()
    {
        var f = new Fixture();
        f.Companies.Current!.Revenue = 0;
        f.Companies.Current.CountOfEmployees = 0;
        var profile = (await f.Service.Capture(f.CompanyId, new("zero"), default)).Content!;
        Assert.Equal("0", profile.DeclaredProfile!.Revenue!.Amount);
        Assert.Equal(0, profile.DeclaredProfile.CountOfEmployees);
        Assert.Null((await f.Service.Capture(f.CompanyId, new("another"), default)).Error);
        Assert.Equal(2, f.Profiles.Versions.Count);
        Assert.Single(f.Profiles.Versions[^1].SourceRefs);
    }

    [Fact]
    public async Task ReplacingDeclaredSnapshotDoesNotExhaustTheSourceReferenceBudget()
    {
        var f = new Fixture();
        for (var i = 0; i < 70; i++)
        {
            f.Companies.Current!.Region = "Declared region " + i;
            var captured = await f.Service.Capture(f.CompanyId, new("capture-" + i), default);
            Assert.Null(captured.Error);
            Assert.Single(captured.Content!.SourceRefs);
            Assert.Equal("Declared region " + i, captured.Content.DeclaredProfile!.Region);
        }
        Assert.Equal(70, f.Profiles.Versions.Count);
    }

    [Fact]
    public void LegacyHashShapeAndStrictInternalCaptureRequestArePreserved()
    {
        Assert.DoesNotContain("declaredProfile", Json(new CompanyProfile()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CaptureDeclaredProfileRequest>("{\"captureId\":\"a\",\"knownAt\":\"2000-01-01\"}", KnowledgeJson.Options));
    }

    [Fact]
    public async Task DeclaredFieldsMustMatchTheirExactScopedSnapshot()
    {
        var f = new Fixture();
        var profile = (await f.Service.Capture(f.CompanyId, new("policy"), default)).Content!;
        var write = Assert.Single(f.Materials.Writes);
        var reference = Assert.Single(profile.DeclaredProfile!.SourceRefs);
        var evidence = new MaterialEvidence
        {
            MaterialId = write.MaterialId,
            VersionId = write.VersionId,
            SourceId = write.SourceId,
            Kind = write.Kind,
            Text = write.Text,
            KnownAt = write.KnownAt,
            CompanyIds = write.CompanyIds,
            ContentHash = reference.ContentHash
        };
        Assert.True(DeclaredCompanyProfilePolicy.Valid(profile));
        Assert.True(DeclaredCompanyProfilePolicy.Matches(profile, evidence));
        profile.DeclaredProfile = profile.DeclaredProfile with { Region = "Invented region" };
        Assert.False(DeclaredCompanyProfilePolicy.Matches(profile, evidence));
        profile.DeclaredProfile = profile.DeclaredProfile with { Revenue = new("100", "RUB", "2026") };
        Assert.False(DeclaredCompanyProfilePolicy.Valid(profile));
    }

    private sealed class Fixture
    {
        public readonly PublicKnowledgeTests.Companies Companies = PublicKnowledgeTests.Setup().Companies;
        public readonly Profiles Profiles = new();
        public readonly Materials Materials = new();
        public Guid CompanyId => Companies.Current!.Id;
        public DeclaredCompanyCaptureService Service => new(Companies, Profiles, Materials, operations);
        private readonly PublicKnowledgeTests.Operations operations = new();
    }
    private sealed class Profiles : IPublicCompanyProfiles
    {
        public readonly List<CompanyProfile> Versions = [];
        public bool LoseNextSave;
        public Task<CompanyProfile> Get(Guid company, DateTimeOffset asOf) => Task.FromResult(Versions.Where(p => p.CompanyId == company && p.KnownAt <= asOf)
            .OrderByDescending(p => p.KnownAt).FirstOrDefault() ?? throw PublicKnowledgeError.Missing());
        public Task<CompanyProfile> Save(CompanyProfile profile)
        {
            var existing = Versions.SingleOrDefault(p => p.RevisionId == profile.RevisionId);
            if (existing is null) Versions.Add(profile);
            else Assert.Equal(Json(existing), Json(profile));
            if (LoseNextSave) { LoseNextSave = false; throw new HttpRequestException("Synthetic lost profile response"); }
            return Task.FromResult(profile);
        }
        public Task<CompanyProfile> SaveIfCurrent(CompanyProfile profile, string? expectedRevisionId)
        {
            var existing = Versions.SingleOrDefault(p => p.RevisionId == profile.RevisionId);
            var previous = Versions.OrderByDescending(p => p.KnownAt).FirstOrDefault();
            if (existing is null && previous?.RevisionId != expectedRevisionId) throw new PublicKnowledgeError(HttpStatusCode.Conflict, "Knowledge changed");
            return Save(profile);
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
            if (stored.TryGetValue((p.MaterialId, p.VersionId), out var existing)) Assert.Equal(Json(existing), Json(value));
            stored[(p.MaterialId, p.VersionId)] = value;
            OnSave?.Invoke(p);
            if (LoseNextSave) { LoseNextSave = false; throw new HttpRequestException("Synthetic lost response"); }
            value = Corrupt switch
            {
                "hash" => value with { ContentHash = new string('f', 64) },
                "text" => value with { Text = "Other text" },
                "source" => value with { SourceId = "document" },
                "company" => value with { CompanyIds = [Guid.NewGuid()] },
                "time" => value with { KnownAt = p.KnownAt.AddDays(1) },
                _ => value
            };
            return Task.FromResult(value);
        }
        public Task<CompanyMaterialsResponse> List(Guid company, string? bank, DateTimeOffset at, int limit, string? cursor, CancellationToken ct) => throw new NotSupportedException();
    }
}
