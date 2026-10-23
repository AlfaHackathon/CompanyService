using System.Net;
using AlfaHackaton.Controllers.Controllers;
using AlfaHackaton.Result.Errors;
using Api.Contracts.Changes;
using Api.Contracts.Knowledge;
using Api.Repositories;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[ApiController]
[Route("internal/knowledge")]
public sealed class KnowledgeController(KnowledgeService knowledge, CompanySelectionService selection, KnowledgeChanges changes, IConfiguration config) : BaseController, IAsyncActionFilter
{
    async Task IAsyncActionFilter.OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = config["Internal:Secret"];
        var supplied = Request.Headers["X-Internal-Secret"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || supplied != expected)
        {
            context.Result = Unauthorized();
            return;
        }
        await next();
    }

    [HttpPost("companies")]
    public async Task<IActionResult> SaveCompany([FromBody] CompanyProfile request) => OkOrProblem(await knowledge.SaveCompany(request));
    [HttpGet("companies")]
    public async Task<IActionResult> Companies([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { companies = await knowledge.Companies(asOf) });
    [HttpGet("companies/versions")]
    public async Task<IActionResult> Versions() => Ok(new { companies = await knowledge.CompanyVersions() });
    [HttpGet("companies/{companyId:guid}")]
    public async Task<IActionResult> Company(Guid companyId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        var company = (await knowledge.Companies(asOf)).SingleOrDefault(p => p.CompanyId == companyId);
        return company is null ? OkOrProblem<CompanyProfile>(new(Missing("Company profile is unavailable at asOf."))) : Ok(company);
    }
    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroup([FromBody] GroupProfile request) => OkOrProblem(await knowledge.SaveGroup(request));
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { groups = await knowledge.Groups(asOf) });
    [HttpGet("groups/{groupId}")]
    public async Task<IActionResult> Expand(string groupId, [FromQuery, BindRequired] DateTimeOffset asOf) => OkOrProblem(await knowledge.Expand(groupId, asOf));
    [HttpPost("roles")]
    public async Task<IActionResult> SaveRole([FromBody] RoleProfile request) => OkOrProblem(await knowledge.SaveRole(request));
    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { roles = await knowledge.Roles(asOf) });
    [HttpGet("role-scope")]
    public async Task<IActionResult> Role(string personId, Guid companyId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        var role = (await knowledge.Roles(asOf)).SingleOrDefault(r => r.PersonId == personId && r.CompanyId == companyId);
        return role is null ? OkOrProblem<RoleProfile>(new(Missing("Role profile is unavailable at asOf."))) : Ok(role);
    }
    [HttpPost("role-scope")]
    public async Task<IActionResult> Scope([FromBody] RoleScopeRequest request) => OkOrProblem(await selection.Scope(request));
    [HttpPost("people")]
    public async Task<IActionResult> SavePerson([FromBody] PersonProfile request) => OkOrProblem(await knowledge.SavePerson(request));
    [HttpGet("people")]
    public async Task<IActionResult> People([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { people = await knowledge.People(asOf) });
    [HttpGet("people/{personId}")]
    public async Task<IActionResult> Person(string personId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        var person = (await knowledge.People(asOf)).SingleOrDefault(p => p.PersonId == personId);
        return person is null ? OkOrProblem<PersonProfile>(new(Missing("Person profile is unavailable at asOf."))) : Ok(person);
    }
    [HttpGet("changes")]
    public async Task<IActionResult> Changes([FromQuery] long after = 0)
    {
        var result = await changes.Read(after);
        return result.Error is { } error ? OkOrProblem<KnowledgeChange[]>(new(error)) : Ok(new { changes = result.Content });
    }
    [HttpPost("selection")]
    public async Task<IActionResult> Select([FromBody] CompanySelectionRequest request, CancellationToken ct) => OkOrProblem(await selection.Select(request, ct));
    [HttpPost("segments")]
    public async Task<IActionResult> Segments([FromBody] SegmentRequest request)
    {
        var result = await selection.Segments(request);
        return result.Error is { } error ? OkOrProblem<CompanySegment[]>(new(error)) : Ok(new { segments = result.Content });
    }

    private static Error Missing(string message) => new(message, ErrorSource.Client, ErrorType.NotFound) { HttpStatusCode = HttpStatusCode.NotFound };
}
