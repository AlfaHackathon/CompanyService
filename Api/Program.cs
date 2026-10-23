using System.Reflection;
using AlfaHackaton.Controllers;
using AlfaHackaton.DapperContext;
using AlfaHackaton.MongoContext;
using Api.Common;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Services;
using Api.Services.Interfaces;
using Api.Settings;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

// Postgres (Dapper + FluentMigrator), как в user-service
var dapperSettings = new DapperSettings(
    builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Не задана строка подключения Postgres (ConnectionStrings:Postgres)"));
builder.Services.AddDapper(dapperSettings);
builder.Services.AddMigrations(dapperSettings, Assembly.GetExecutingAssembly());

// Mongo (AlfaHackaton.MongoContext): инвайты с самоочисткой по TTL
builder.Services.AddMongo(new MongoSettings(
    builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017",
    builder.Configuration["Mongo:Database"] ?? "company-service"));
builder.Services.AddMongoTtlIndex<InviteDocument>("invites", x => x.ExpiresAt, TimeSpan.Zero);
builder.Services.AddMongoUniqueIndex<InviteDocument>("invites", x => x.Code);
builder.Services.AddMongoIndex<InviteDocument>("invites", x => x.CompanyId);

builder.Services.AddSingleton<ICompanyRepository, CompanyRepository>();
builder.Services.AddSingleton<IIndustryRepository, IndustryRepository>();
builder.Services.AddSingleton<IInviteRepository, InviteRepository>();

// Слой бизнес-логики: контроллеры работают только с сервисами
builder.Services.AddSingleton<ICompanyService, CompanyService>();
builder.Services.AddSingleton<IIndustryService, IndustryService>();
builder.Services.AddSingleton<IInviteService, InviteService>();
builder.Services.AddHttpClient(nameof(ContextKnowledgeClient), client => client.Timeout = TimeSpan.FromSeconds(120))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ContextKnowledgeClient>();
builder.Services.AddSingleton<KnowledgeStore>();
builder.Services.AddSingleton<KnowledgeChanges>();
builder.Services.AddSingleton<KnowledgeService>();
builder.Services.AddSingleton<CompanySelectionService>();

// Префикс маршрутов: /company-service/... (JWT валидируется на auth-gateway)
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new RoutePrefixConvention("company-service"));
});
builder.Services.AddAuthenticationWithoutValidation();

// Swagger с поддержкой Bearer-авторизации
builder.Services.AddSwaggerAuth();

var app = builder.Build();

// Применяем миграции Postgres и создаём Mongo-индексы (идемпотентно)
app.Services.UseMigrations();
await app.Services.UseMongoIndexesAsync();

// snake_case-маппинг колонок (bank_profile_id -> BankProfileId)
DefaultTypeMap.MatchNamesWithUnderscores = true;

if (app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger(options => options.RouteTemplate = "company-service/swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/company-service/swagger/v1/swagger.json", "Company Service API");
        options.RoutePrefix = "company-service/swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
