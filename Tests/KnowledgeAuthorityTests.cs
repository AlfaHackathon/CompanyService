using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlfaHackaton.DapperContext;
using Api.Common;
using Api.Contracts.Context;
using Api.Contracts.Knowledge;
using Api.Controllers;
using Api.Migrations;
using Api.Repositories;
using Api.Services;
using Api.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

public sealed class KnowledgeAuthorityTests : IAsyncLifetime
{
    private const string Prefix = "/company-service/internal/knowledge/";
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
    private static readonly SourceReference[] Evidence = [new() { MaterialId = "source", VersionId = "v1", ContentHash = new string('a', 64) }];
    private string administrativeConnection = "";
    private readonly string database = "company_test_" + Guid.NewGuid().ToString("N");
    private TestServer server = null!;
    private IHost host = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        administrativeConnection = Environment.GetEnvironmentVariable("COMPANY_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set COMPANY_TEST_POSTGRES to an isolated synthetic PostgreSQL instance.");
        await using (var connection = new NpgsqlConnection(administrativeConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await command.ExecuteNonQueryAsync();
        }
        var settings = new DapperSettings(new NpgsqlConnectionStringBuilder(administrativeConnection) { Database = database, Pooling = false }.ToString());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Internal:Secret"] = "synthetic-test",
            ["ContextService:BaseUrl"] = "http://context.fixture"
        }).Build();
        host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            services.AddDapper(settings);
            services.AddMigrations(settings, typeof(AddKnowledgeRevisions).Assembly);
            services.AddHttpClient(nameof(ContextKnowledgeClient)).ConfigurePrimaryHttpMessageHandler(() => new EvidenceHandler());
            services.AddSingleton<ContextKnowledgeClient>();
            services.AddSingleton<KnowledgeStore>();
            services.AddSingleton<KnowledgeChanges>();
            services.AddSingleton<KnowledgeService>();
            services.AddSingleton<CompanySelectionService>();
            services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
                .AddApplicationPart(typeof(KnowledgeController).Assembly);
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers()); })).StartAsync();
        server = host.GetTestServer();
        server.Services.UseMigrations();
        http = server.CreateClient();
        http.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-test");
    }

    [Fact]
    public async Task Revision_is_immutable_and_evidence_and_asof_are_authoritative()
    {
        var profile = Profile(CompanyA);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        profile.Name = "Changed";
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        profile = Profile(CompanyA);
        profile.RevisionId = "bad";
        profile.SourceRefs = [new() { MaterialId = "source", VersionId = "v1", ContentHash = "wrong" }];
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        profile = Profile(CompanyA);
        profile.RevisionId = "early";
        profile.KnownAt = At.AddDays(-2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        profile = Profile(CompanyA);
        profile.RevisionId = "r2";
        profile.KnownAt = At.AddDays(1);
        profile.Name = "New name";
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "companies", profile)).StatusCode);
        var before = await http.GetFromJsonAsync<JsonElement>(Prefix + "companies?asOf=" + Uri.EscapeDataString(At.ToString("O")));
        Assert.Equal("Company A", before.GetProperty("companies")[0].GetProperty("name").GetString());
        var all = await http.GetFromJsonAsync<JsonElement>(Prefix + "companies/versions");
        Assert.Equal(2, all.GetProperty("companies").GetArrayLength());
        var changes = server.Services.GetRequiredService<KnowledgeChanges>();
        var first = Assert.Single((await changes.Read(0)).Content!);
        var second = Assert.Single((await changes.Read(first.Sequence)).Content!);
        Assert.Empty((await changes.Read(second.Sequence)).Content!);
        http.DefaultRequestHeaders.Remove("X-Internal-Secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(Prefix + "companies/versions")).StatusCode);
    }

    [Fact]
    public async Task Groups_selection_segments_and_grants_are_resolved_by_company_service()
    {
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "companies", Profile(CompanyA))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(Prefix + "groups", new
        {
            groupId = "missing-members", revisionId = "r1", name = "Missing members", knownAt = At, sourceRefs = Evidence
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "groups", new GroupProfile
        {
            GroupId = "child", RevisionId = "r1", Name = "Child", KnownAt = At,
            Members = [new() { Kind = "company", Id = CompanyA.ToString(), SourceRefs = Evidence }], SourceRefs = Evidence
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "groups", new GroupProfile
        {
            GroupId = "root", RevisionId = "r1", Name = "Root", KnownAt = At,
            Members = [new() { Kind = "group", Id = "child", SourceRefs = Evidence }], SourceRefs = Evidence
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "people", new PersonProfile
        {
            PersonId = "person", RevisionId = "r1", KnownAt = At,
            CompanyLinks = [new() { CompanyId = CompanyB, RelationKind = "owner", SourceRefs = Evidence }], SourceRefs = Evidence
        })).StatusCode);
        var ungranted = await http.PostAsJsonAsync(Prefix + "role-scope", new RoleScopeRequest { AsOf = At, PersonIds = ["person"], CompanyIds = [] });
        var scope = (await ungranted.Content.ReadFromJsonAsync<RoleScope>())!;
        Assert.Empty(scope.CompanyIds);
        Assert.Contains("person", scope.MissingPersonIds);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(Prefix + "roles", new RoleProfile
        {
            PersonId = "person", CompanyId = CompanyA, RevisionId = "r1", Role = "analyst",
            Responsibilities = [], AllowedCompanyIds = [CompanyA], AllowedMaterialIds = [], KnownAt = At, SourceRefs = Evidence
        })).StatusCode);
        var selected = await http.PostAsJsonAsync(Prefix + "selection", new CompanySelectionRequest
        {
            AsOf = At, TargetCompanyIds = [], GroupIds = ["root"], PersonIds = ["person"], Queries = ["industry"], PreviousCompanyIds = [CompanyB], CompanyBudget = 1
        });
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var selection = (await selected.Content.ReadFromJsonAsync<CompanySelection>())!;
        Assert.Equal([CompanyA], selection.SelectedCompanyIds);
        Assert.Equal([CompanyB], selection.PendingCompanyIds);
        var group = Assert.Single(selection.Groups);
        Assert.True(group.Complete);
        Assert.Equal(["root", "child"], Assert.Single(group.Paths).GroupIds);
        var segmented = await http.PostAsJsonAsync(Prefix + "segments", new SegmentRequest { AsOf = At, CompanyIds = [CompanyA] });
        var segments = (await segmented.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("segments");
        Assert.Equal("activity:28.1", segments[0].GetProperty("key").GetString());
        Assert.Equal(CompanyA, segments[0].GetProperty("companyIds")[0].GetGuid());
    }

    private static CompanyProfile Profile(Guid id)
    {
        return new()
        {
            CompanyId = id,
            RevisionId = "r1",
            Name = "Company A",
            Description = "Equipment; details",
            IndustryCodes = ["28.1"],
            Exposures = [],
            KnownAt = At,
            SourceRefs = Evidence
        };
    }
    private sealed class EvidenceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("synthetic-test", Assert.Single(request.Headers.GetValues("X-Internal-Secret")));
            object body = request.Method == HttpMethod.Get
                ? new MaterialEvidence { ContentHash = Evidence[0].ContentHash, KnownAt = At.AddDays(-1), CompanyIds = [CompanyA, CompanyB] }
                : new SearchHits { Hits = [new() { CompanyId = CompanyB }] };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }

    public async Task DisposeAsync()
    {
        http?.Dispose();
        host?.Dispose();
        if (string.IsNullOrEmpty(administrativeConnection)) return;
        await using var connection = new NpgsqlConnection(administrativeConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
