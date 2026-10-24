using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Context;

namespace Api.Services;

// Context owns material evidence and retrieval; company rules remain local.
public sealed class ContextKnowledgeClient(IHttpClientFactory clients, IConfiguration config)
{
    public Task<Result<MaterialEvidence>> MaterialAsync(string materialId, string versionId)
    {
        return SendAsync<MaterialEvidence>(HttpMethod.Get, $"material-versions/{Uri.EscapeDataString(materialId)}/{Uri.EscapeDataString(versionId)}", null, default);
    }

    public Task<Result<SearchHits>> SearchAsync(
        string query,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        return SendAsync<SearchHits>(HttpMethod.Post, "search", new { query, asOf, limit = 48 }, ct);
    }

    private async Task<Result<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
    {
        var baseUrl = config["ContextService:BaseUrl"];
        var secret = config["ContextService:InternalSecret"] ?? config["Internal:Secret"];
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(secret))
        {
            return new(new Error("ContextService URL and internal secret are required.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }

        using var request = new HttpRequestMessage(method, new Uri(address, "context-service/internal/" + path));
        request.Headers.Add("X-Internal-Secret", secret);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        try
        {
            using var client = clients.CreateClient(nameof(ContextKnowledgeClient));
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode == HttpStatusCode.NotFound ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable;
                return new(new Error("Context evidence or retrieval is unavailable.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = status });
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return value is null
                ? new(new Error("Context returned an empty response.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable })
                : new(value, null);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new(new Error("Context evidence or retrieval is unavailable.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.ServiceUnavailable });
        }
    }
}
