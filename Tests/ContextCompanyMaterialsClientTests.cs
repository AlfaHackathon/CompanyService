using System.Net;
using System.Text;
using System.Text.Json;
using Api.Common;
using Api.Services.PublicKnowledge;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class ContextCompanyMaterialsClientTests
{
    [Fact]
    public async Task ClientUsesOnlyConfiguredInternalRouteAndServerCredential()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("http://context.fixture/context-service/internal/material-versions/own/v1", request.RequestUri!.AbsoluteUri);
            Assert.Equal("synthetic-only", Assert.Single(request.Headers.GetValues("X-Internal-Secret")));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(HttpMethod.Get, request.Method);
            await Task.Yield();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new ScopedCompanyMaterial("own", "v1", "synthetic", "company", "Own", "Original", DateTimeOffset.UtcNow, null, null, [Guid.NewGuid()], [], new string('a', 64), null), KnowledgeJson.Options), Encoding.UTF8, "application/json") };
        });
        var result = await Client(handler).Get("own", "v1", default);
        Assert.Equal("Original", result.Text);
        Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(302)]
    [InlineData(413)]
    public async Task UpstreamErrorsNeverLeakOrBecomeEmptySuccess(int code)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("PRIVATE raw body") }));
        var error = await Assert.ThrowsAsync<PublicKnowledgeError>(() => Client(handler).Get("own", "v1", default));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.Status);
        Assert.DoesNotContain("PRIVATE", error.Message);
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task ListForwardsOnlyTrustedScopeSnapshotAndCursor()
    {
        var company = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-10-25T00:00:00.0000001Z");
        var handler = new Handler(request =>
        {
            Assert.Equal($"/context-service/internal/companies/{company:D}/materials", request.RequestUri!.AbsolutePath);
            Assert.Contains("bankProfileId=U_96", request.RequestUri.Query);
            Assert.Contains("cursor=opaque", request.RequestUri.Query);
            Assert.Contains("0000001", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"companyId\":\"{company:D}\",\"asOf\":\"{at:O}\",\"items\":[],\"nextCursor\":null,\"coverage\":\"canonical_changes_only\"}}", Encoding.UTF8, "application/json") });
        });
        Assert.Empty((await Client(handler).List(company, "U_96", at, 20, "opaque", default)).Items);
    }
    [Fact]
    public async Task MissingCatalogDependencyIsUnavailableWhileMissingExactVersionIsNotFound()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = Client(handler);
        var catalog = await Assert.ThrowsAsync<PublicKnowledgeError>(() => client.List(Guid.NewGuid(), null, DateTimeOffset.UtcNow, 20, null, default));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, catalog.Status);
        var exact = await Assert.ThrowsAsync<PublicKnowledgeError>(() => client.Get("own", "v1", default));
        Assert.Equal(HttpStatusCode.NotFound, exact.Status);
    }
    [Theory]
    [InlineData("http://user:password@context.fixture")]
    [InlineData("http://context.fixture?private=true")]
    [InlineData("file:///private")]
    public async Task CredentialBearingOrNonHttpConfigurationIsRejectedBeforeHttp(string url)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("HTTP must not run"));
        await Assert.ThrowsAsync<PublicKnowledgeError>(() => Client(handler, url).Get("own", "v1", default));
        Assert.Equal(0, handler.Calls);
    }
    private static ContextCompanyMaterialsClient Client(Handler handler, string url = "http://context.fixture") =>
        new(new Factory(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ContextService:BaseUrl"] = url, ["Internal:Secret"] = "synthetic-only" }).Build());
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return response(request); }
    }
}
