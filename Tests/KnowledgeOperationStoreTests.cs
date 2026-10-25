using System.Net;
using AlfaHackaton.DapperContext;
using Api.Migrations;
using Api.Repositories;
using Api.Resources;
using Api.Services.PublicKnowledge;
using Api.Settings;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

// Native gate only: the caller must explicitly supply an isolated synthetic instance.
public sealed class KnowledgeOperationStoreTests : IAsyncLifetime
{
    private readonly string database = "company_operation_test_" + Guid.NewGuid().ToString("N");
    private string admin = "";
    private string connectionString = "";
    private ServiceProvider services = null!;
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("COMPANY_TEST_POSTGRES") ?? throw new InvalidOperationException("Set COMPANY_TEST_POSTGRES only to an isolated synthetic PostgreSQL instance.");
        await using (var connection = new NpgsqlConnection(admin)) { await connection.ExecuteAsync($"CREATE DATABASE {database}"); }
        connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database, Pooling = false }.ToString();
        var settings = new DapperSettings(connectionString);
        services = new ServiceCollection().AddDapper(settings).AddMigrations(settings, typeof(AddPublicKnowledgeOperations).Assembly)
            .AddSingleton<KnowledgeOperationStore>().BuildServiceProvider();
        services.UseMigrations();
    }
    [Fact]
    public async Task ConcurrentReservationUsesOneFrozenPayloadAndMonotonicCompletion()
    {
        var store = services.GetRequiredService<KnowledgeOperationStore>();
        var actor = Guid.NewGuid(); var company = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 8).Select(i => store.Reserve(actor, company, "material", "same-key", "same-hash", time => $"candidate-{i}-{time:O}"));
        var operations = await Task.WhenAll(tasks);
        Assert.Single(operations.Select(o => o.Payload).Distinct());
        Assert.Single(operations.Select(o => o.KnownAt).Distinct());
        await Task.WhenAll(operations.Select(store.Complete));
        Assert.True((await store.Find(actor, company, "material", "same-key", "same-hash"))!.Completed);
        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<PublicKnowledgeError>(() => store.Reserve(actor, company, "material", "same-key", "different-hash", _ => "must not run"))).Status);
        var next = await store.Reserve(actor, company, "knowledge", "next-key", "other-hash", time => time.ToString("O"));
        Assert.True(next.KnownAt > operations[0].KnownAt);
        Assert.Null(await store.Find(Guid.NewGuid(), company, "material", "same-key", "same-hash"));
    }
    [Fact]
    public async Task LegacyFutureTimeFailsBeforeReservingOrPublishingNewMaterial()
    {
        var actor = Guid.NewGuid(); var company = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("INSERT INTO company_knowledge_revisions VALUES('company',@company,'future',@time,'hash','{}')", new { company = company.ToString("D"), time = DateTimeOffset.UtcNow.AddDays(1).UtcTicks });
        var store = services.GetRequiredService<KnowledgeOperationStore>();
        var error = await Assert.ThrowsAsync<PublicKnowledgeError>(() => store.Reserve(actor, company, "material", "future-key", "hash", _ => throw new InvalidOperationException("Freeze must not run")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.Status);
        Assert.Null(await store.Find(actor, company, "material", "future-key", "hash"));
    }
    public async Task DisposeAsync()
    {
        services?.Dispose();
        if (admin.Length > 0) { await using var connection = new NpgsqlConnection(admin); await connection.ExecuteAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)"); }
    }
}
