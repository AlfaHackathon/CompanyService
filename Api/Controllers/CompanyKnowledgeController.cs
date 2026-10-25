using AlfaHackaton.Controllers.Controllers;
using Api.Contracts.PublicKnowledge;
using Api.Services.PublicKnowledge;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("companies/me")]
[RequestSizeLimit(1048576)]
public sealed class CompanyKnowledgeController(PublicKnowledgeService knowledge) : BaseAuthController
{
    private bool Query(params string[] allowed) => Request.Query.All(pair => allowed.Contains(pair.Key, StringComparer.Ordinal) && pair.Value.Count == 1);
    private string? Key() => Request.Headers["Idempotency-Key"].Count == 1 ? Request.Headers["Idempotency-Key"].ToString() : null;
    private IActionResult InvalidQuery() => BadRequest(new { code = "invalid_request", message = "Only documented, nonrepeated query parameters are accepted." });

    [HttpGet("knowledge")]
    [ProducesResponseType(typeof(CompanyKnowledgeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Query()) return InvalidQuery();
        return OkOrProblem(await knowledge.GetKnowledge(UserId, ct));
    }

    [HttpPut("knowledge")]
    [ProducesResponseType(typeof(CompanyKnowledgeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Put([FromBody] PutCompanyKnowledgeRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Query()) return InvalidQuery();
        return OkOrProblem(await knowledge.PutKnowledge(UserId, request, Key(), ct));
    }

    [HttpGet("materials")]
    [ProducesResponseType(typeof(CompanyMaterialsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Materials([FromQuery] int limit = 20, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Query("limit", "cursor")) return InvalidQuery();
        return OkOrProblem(await knowledge.ListMaterials(UserId, limit, cursor, ct));
    }

    [HttpPost("materials")]
    [ProducesResponseType(typeof(CompanyMaterialResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyMaterialRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Query()) return InvalidQuery();
        return OkOrProblem(await knowledge.CreateMaterial(UserId, request, Key(), ct));
    }

    [HttpGet("materials/{materialId}/versions/{versionId}")]
    [ProducesResponseType(typeof(CompanyMaterialResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Material(string materialId, string versionId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Query()) return InvalidQuery();
        return OkOrProblem(await knowledge.GetMaterial(UserId, materialId, versionId, ct));
    }
}
