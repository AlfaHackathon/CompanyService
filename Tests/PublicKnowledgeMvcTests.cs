using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Api.Common;
using Api.Controllers;
using Api.Services.PublicKnowledge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

// Real MVC route/binding/Authorize/Swagger pipeline with synthetic auth and fake services.
// This does not validate a real JWT or the gateway's network boundary.
public sealed class PublicKnowledgeMvcTests
{
    private sealed class SyntheticAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer synthetic-owner") return Task.FromResult(AuthenticateResult.NoResult());
            const string actor = "11111111-1111-4111-8111-111111111111";
            Request.Headers["X-User-Id"] = actor;
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor)], Scheme.Name)), Scheme.Name)));
        }
    }
    private static async Task<WebApplication> Start(PublicKnowledgeService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(service);
        builder.Services.AddAuthentication("synthetic").AddScheme<AuthenticationSchemeOptions, SyntheticAuthentication>("synthetic", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
            .AddApplicationPart(typeof(CompanyKnowledgeController).Assembly);
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Synthetic Company contract", Version = "v1" });
            options.OperationFilter<CompanyKnowledgeOperationFilter>();
        });
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.UseSwagger(options => options.RouteTemplate = "company-service/swagger/{documentName}/swagger.json");
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
    [Fact]
    public async Task AuthenticationPrecedesMembershipAndAllFiveRoutesBindWithoutCallerScope()
    {
        var f = PublicKnowledgeTests.Setup();
        await using var app = await Start(f.Service);
        using var client = app.GetTestClient();
        const string prefix = "/company-service/companies/me/";
        using var unauthenticated = await client.GetAsync(prefix + "knowledge");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(0, f.Companies.Reads);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-owner");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "mvc-declaration");
        using var created = await client.PostAsJsonAsync(prefix + "materials", new { title = "Own declaration", text = "Synthetic declared company information" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var material = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(material.TryGetProperty("companyIds", out _));
        Assert.False(material.TryGetProperty("bankProfileIds", out _));
        Assert.Equal("unknown", material.GetProperty("textCompleteness").GetString());
        var id = material.GetProperty("materialId").GetString();
        var version = material.GetProperty("versionId").GetString();
        using var exact = await client.GetAsync(prefix + $"materials/{id}/versions/{version}");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "mvc-profile");
        using var profile = await client.PutAsJsonAsync(prefix + "knowledge", new
        {
            description = "Own profile",
            industryCodes = new[] { "45.11" },
            exposures = Array.Empty<string>(),
            sourceRefs = new[] { new { materialId = id, versionId = version } }
        });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var read = await client.GetAsync(prefix + "knowledge");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl!.Private);
        Assert.True(read.Headers.CacheControl.NoStore);
        using var list = await client.GetAsync(prefix + "materials?limit=1");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }
    [Fact]
    public async Task StrictJsonQueriesHeaderAndSwaggerDescribeTheActualPublicBoundary()
    {
        var f = PublicKnowledgeTests.Setup();
        await using var app = await Start(f.Service);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-owner");
        const string prefix = "/company-service/companies/me/";
        foreach (var suffix in new[] { "materials?companyId=spoofed", "materials?limit=0", "materials?limit=1&limit=2", "knowledge?asOf=2026-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(prefix + suffix)).StatusCode);
        foreach (var field in new[] { "companyId", "provenance", "contentHash", "knownAt" })
        {
            using var response = await client.PostAsync(prefix + "materials", new StringContent("{\"title\":\"Own\",\"text\":\"Text\",\"" + field + "\":\"spoofed\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var missingKey = await client.PostAsJsonAsync(prefix + "materials", new { title = "Own", text = "Text" });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Empty(f.Operations.Items);
        var spec = await client.GetFromJsonAsync<JsonElement>("/company-service/swagger/v1/swagger.json");
        foreach (var pair in new[] { ("knowledge", "put"), ("materials", "post") })
        {
            var operation = spec.GetProperty("paths").GetProperty(prefix + pair.Item1).GetProperty(pair.Item2);
            var key = operation.GetProperty("parameters").EnumerateArray().Single(value => value.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.True(key.GetProperty("required").GetBoolean());
            foreach (var status in new[] { "200", "400", "401", "403", "404", "409", "413", "503" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
    }
}
