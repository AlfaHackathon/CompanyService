using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Common;
using Api.Contracts.PublicKnowledge;

namespace Api.Services.PublicKnowledge;

// Typed, server-only adapter. No user-supplied URL, scope, timestamp or credentials.
public sealed record CompanyMaterialWrite(string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, string Text, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt, string? CorrectsVersionId,
    Guid[] CompanyIds, string[] BankProfileIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MaterialProvenance? Provenance);
public sealed record ScopedCompanyMaterial(string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, string Text, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt, string? CorrectsVersionId,
    Guid[] CompanyIds, string[] BankProfileIds, string ContentHash, MaterialProvenance? Provenance);
public interface ICompanyMaterialsClient
{
    Task<ScopedCompanyMaterial> Get(string materialId, string versionId, CancellationToken ct);
    Task<ScopedCompanyMaterial> Save(CompanyMaterialWrite payload, CancellationToken ct);
    Task<CompanyMaterialsResponse> List(Guid company, string? bankProfile, DateTimeOffset asOf, int limit, string? cursor, CancellationToken ct);
}

public sealed class ContextCompanyMaterialsClient(IHttpClientFactory clients, IConfiguration configuration) : ICompanyMaterialsClient
{
    public Task<ScopedCompanyMaterial> Get(string materialId, string versionId, CancellationToken ct) =>
        Send<ScopedCompanyMaterial>(HttpMethod.Get, $"material-versions/{Uri.EscapeDataString(materialId)}/{Uri.EscapeDataString(versionId)}", null, ct);
    public Task<ScopedCompanyMaterial> Save(CompanyMaterialWrite payload, CancellationToken ct) =>
        Send<ScopedCompanyMaterial>(HttpMethod.Post, "material-versions", payload, ct);
    public Task<CompanyMaterialsResponse> List(Guid company, string? bankProfile, DateTimeOffset asOf, int limit, string? cursor, CancellationToken ct)
    {
        var query = $"asOf={Uri.EscapeDataString(asOf.ToString("O"))}&limit={limit}";
        if (bankProfile is not null) query += "&bankProfileId=" + Uri.EscapeDataString(bankProfile);
        if (cursor is not null) query += "&cursor=" + Uri.EscapeDataString(cursor);
        return Send<CompanyMaterialsResponse>(HttpMethod.Get, $"companies/{company:D}/materials?{query}", null, ct);
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var baseUrl = configuration["ContextService:BaseUrl"];
        var secret = configuration["ContextService:InternalSecret"] ?? configuration["Internal:Secret"];
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo) ||
            !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment) || string.IsNullOrWhiteSpace(secret))
            throw PublicKnowledgeError.Unavailable();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client = clients.CreateClient(nameof(ContextCompanyMaterialsClient));
        using var request = new HttpRequestMessage(method, new Uri(address, "context-service/internal/" + path));
        request.Headers.Add("X-Internal-Secret", secret);
        if (body is not null) request.Content = JsonContent.Create(body, options: KnowledgeJson.Options);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) throw PublicKnowledgeError.Missing();
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new PublicKnowledgeError(HttpStatusCode.Conflict, "The immutable company material conflicts with an existing version.");
            if (response.StatusCode == HttpStatusCode.BadRequest && method == HttpMethod.Get)
                throw PublicKnowledgeError.Invalid();
            if (!response.IsSuccessStatusCode) throw PublicKnowledgeError.Unavailable();
            // Bound the complete decoded response before deserializing, including chunked bodies.
            const int maximum = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum) throw PublicKnowledgeError.Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, timeout.Token)) > 0)
            {
                if (buffer.Length + count > maximum) throw PublicKnowledgeError.Unavailable();
                buffer.Write(block, 0, count);
            }
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), KnowledgeJson.Options) ?? throw PublicKnowledgeError.Unavailable();
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or IOException)
        {
            throw PublicKnowledgeError.Unavailable();
        }
    }
}
