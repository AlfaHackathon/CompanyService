using System.Net;
using System.Net.Http.Json;
using AlfaHackaton.DapperContext;
using Api.Common;
using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Controllers;
using Api.Migrations;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Resources;
using Api.Services;
using Api.Services.Interfaces;
using Api.Settings;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

public sealed class MembershipConcurrencyTests : IAsyncLifetime
{
    private const string Prefix = "/company-service/internal/";
    private readonly string database = "company_membership_test_" + Guid.NewGuid().ToString("N");
    private readonly MemoryInvites invites = new();
    private string admin = "";
    private string connectionString = "";
    private IHost host = null!;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("COMPANY_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set COMPANY_TEST_POSTGRES to an isolated synthetic PostgreSQL instance.");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.ExecuteAsync($"CREATE DATABASE {database}");
        }
        connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database, Pooling = false }.ToString();
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var settings = new DapperSettings(connectionString);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Internal:Secret"] = "synthetic-test"
        }).Build();
        host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            services.AddDapper(settings);
            services.AddMigrations(settings, typeof(EnforceSingleCompanyMembership).Assembly);
            services.AddSingleton<ICompanyRepository, CompanyRepository>();
            services.AddSingleton<IIndustryRepository, IndustryRepository>();
            services.AddSingleton<IInviteRepository>(invites);
            services.AddSingleton<ICompanyService, Api.Services.CompanyService>();
            services.AddSingleton<IInviteService, InviteService>();
            services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
                .AddApplicationPart(typeof(InternalController).Assembly);
        }).Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapControllers()); })).StartAsync();
        host.Services.UseMigrations();
        http = host.GetTestServer().CreateClient();
        http.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-test");
    }

    [Fact]
    public async Task Concurrent_creates_return_one_conflict_without_an_orphan_company()
    {
        var userId = Guid.NewGuid();
        var responses = await Race("companies",
            () => Create(userId, "First"), () => Create(userId, "Second"));

        AssertOneSuccessAndConflict(responses);
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM companies"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM company_members WHERE user_id = @userId", new { userId }));
    }

    [Fact]
    public async Task Competing_invites_for_one_user_do_not_claim_the_losing_invite()
    {
        var firstCompany = await SeedCompany();
        var secondCompany = await SeedCompany();
        invites.Seed("first", firstCompany);
        invites.Seed("second", secondCompany);
        var userId = Guid.NewGuid();

        var responses = await Race("company_members",
            () => Use("first", userId), () => Use("second", userId));

        AssertOneSuccessAndConflict(responses);
        Assert.Equal(1, invites.ClaimAttempts);
        var remaining = Assert.Single(invites.Remaining());
        Assert.Null(remaining.UsedAt);
        Assert.Null(remaining.UsedBy);
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM company_members WHERE user_id = @userId", new { userId }));
    }

    [Fact]
    public async Task Concurrent_invites_to_same_company_for_same_user_return_conflicts()
    {
        var company = await SeedCompany();
        var userId = Guid.NewGuid();
        var codes = Enumerable.Range(0, 16).Select(index => "same-company-" + index).ToArray();
        foreach (var code in codes) invites.Seed(code, company);

        var responses = await Race("company_members", codes
            .Select(code => (Func<Task<HttpResponseMessage>>)(() => Use(code, userId))).ToArray());

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(codes.Length - 1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        Assert.Equal(1, invites.ClaimAttempts);
        Assert.Equal(codes.Length - 1, invites.Remaining().Length);
        Assert.All(invites.Remaining(), invite =>
        {
            Assert.Null(invite.UsedAt);
            Assert.Null(invite.UsedBy);
        });
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM company_members WHERE user_id = @userId", new { userId }));
    }

    [Fact]
    public async Task Create_and_invite_share_the_same_membership_constraint()
    {
        var existingCompany = await SeedCompany();
        invites.Seed("join", existingCompany);
        var userId = Guid.NewGuid();

        var responses = await Race("companies, company_members",
            () => Create(userId, "Competing create"), () => Use("join", userId));

        AssertOneSuccessAndConflict(responses);
        await using var connection = new NpgsqlConnection(connectionString);
        var membership = await connection.ExecuteScalarAsync<Guid>("SELECT company_id FROM company_members WHERE user_id = @userId", new { userId });
        var inviteWon = membership == existingCompany;
        Assert.Equal(inviteWon ? 1 : 2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM companies"));
        Assert.Equal(inviteWon ? 1 : 0, invites.ClaimAttempts);
        if (!inviteWon) Assert.Null(Assert.Single(invites.Remaining()).UsedAt);
    }

    [Fact]
    public async Task One_invite_cannot_leave_memberships_for_two_users()
    {
        var company = await SeedCompany();
        invites.Seed("single", company);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var responses = await Race("company_members",
            () => Use("single", first), () => Use("single", second));

        AssertOneSuccessAndConflict(responses);
        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM company_members WHERE user_id = @first OR user_id = @second", new { first, second }));
    }

    [Fact]
    public async Task Migration_rejects_existing_conflicts_without_removing_memberships()
    {
        var first = await SeedCompany();
        var second = await SeedCompany();
        var userId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("ALTER TABLE company_members DROP CONSTRAINT uq_company_members_user");
        await connection.ExecuteAsync("INSERT INTO company_members (company_id, user_id) VALUES (@first, @userId), (@second, @userId)", new { first, second, userId });
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(CompanySql.EnforceSingleMembership, transaction: transaction));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            await transaction.RollbackAsync();
        }
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM company_members WHERE user_id = @userId", new { userId }));
    }

    private Task<HttpResponseMessage> Create(Guid userId, string name) => http.PostAsJsonAsync(Prefix + "companies",
        new CreateCompanyInternalRequest { CreatorId = userId, Name = name });

    private Task<HttpResponseMessage> Use(string code, Guid userId) => http.PostAsJsonAsync(Prefix + $"invites/{code}/use",
        new UseInviteInternalRequest { UserId = userId });

    private async Task<Guid> SeedCompany()
    {
        var company = await host.Services.GetRequiredService<ICompanyRepository>().TryCreateAsync(new Company
        {
            Id = Guid.NewGuid(), CreatorId = Guid.NewGuid(), Name = "Existing", CreatedAt = DateTime.UtcNow
        });
        return company!.Id;
    }

    private async Task<HttpResponseMessage[]> Race(string tables, params Func<Task<HttpResponseMessage>>[] attempts)
    {
        await using var blocker = new NpgsqlConnection(connectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        // These fixed table names are supplied only by tests. Reads remain available,
        // so the old check-then-insert implementation reaches both inserts before either wins.
        await blocker.ExecuteAsync($"LOCK TABLE {tables} IN SHARE MODE", transaction: transaction);
        var requests = attempts.Select(attempt => attempt()).ToArray();
        await using var observer = new NpgsqlConnection(connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (await observer.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%INSERT INTO%'") < requests.Length)
        {
            await Task.Delay(20, timeout.Token);
        }
        await transaction.CommitAsync();
        return await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void AssertOneSuccessAndConflict(HttpResponseMessage[] responses)
    {
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
    }

    public async Task DisposeAsync()
    {
        http?.Dispose();
        host?.Dispose();
        if (string.IsNullOrEmpty(admin)) return;
        await using var connection = new NpgsqlConnection(admin);
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }

    // Atomic invite claims model the existing Mongo FindOneAndUpdate contract.
    // PostgreSQL transactions, controllers and business services are real.
    private sealed class MemoryInvites : IInviteRepository
    {
        private readonly Dictionary<string, InviteDocument> items = new();
        public int ClaimAttempts { get; private set; }

        public void Seed(string code, Guid companyId)
        {
            lock (items) items.Add(code, new InviteDocument { Code = code, CompanyId = companyId, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        }

        public InviteDocument[] Remaining()
        {
            lock (items) return items.Values.Select(Copy).ToArray();
        }

        public Task<InviteDocument?> GetByCodeAsync(string code)
        {
            lock (items) return Task.FromResult(items.TryGetValue(code, out var invite) ? Copy(invite) : null);
        }

        public Task<InviteDocument?> TryUseAsync(string code, Guid userId)
        {
            lock (items)
            {
                ClaimAttempts++;
                if (!items.TryGetValue(code, out var invite) || invite.UsedAt is not null || invite.ExpiresAt <= DateTime.UtcNow)
                    return Task.FromResult<InviteDocument?>(null);
                var before = Copy(invite);
                invite.UsedAt = DateTime.UtcNow;
                invite.UsedBy = userId;
                return Task.FromResult<InviteDocument?>(before);
            }
        }

        public Task DeleteAsync(string code)
        {
            lock (items) items.Remove(code);
            return Task.CompletedTask;
        }

        private static InviteDocument Copy(InviteDocument invite) => new()
        {
            Code = invite.Code, CompanyId = invite.CompanyId, ExpiresAt = invite.ExpiresAt, UsedAt = invite.UsedAt, UsedBy = invite.UsedBy
        };

        public Task<InviteDocument> CreateAsync(Guid companyId, Guid createdBy, string code, TimeSpan ttl) => throw new NotSupportedException();
        public Task<List<InviteDocument>> ListByCompanyAsync(Guid companyId) => throw new NotSupportedException();
        public Task InsertExpiredForTestAsync(string code, Guid companyId) => throw new NotSupportedException();
    }
}
