using AlfaHackaton.Controllers.Controllers;
using Api.Common;
using Api.Contracts.Knowledge;
using Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[ApiController]
[InternalSecret]
[Route("internal/knowledge")]
public sealed class KnowledgeController(KnowledgeService knowledge, CompanySelectionService selection) : BaseController
{
    [HttpPost("companies")]
    public async Task<IActionResult> SaveCompanyAsync([FromBody] CompanyProfile request)
    {
        return OkOrProblem(await knowledge.SaveCompanyAsync(request));
    }

    [HttpGet("companies")]
    public async Task<IActionResult> CompaniesAsync([FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetCompaniesAsync(asOf));
    }

    [HttpGet("companies/versions")]
    public async Task<IActionResult> VersionsAsync()
    {
        return OkOrProblem(await knowledge.GetCompanyVersionsAsync());
    }

    [HttpGet("companies/{companyId:guid}")]
    public async Task<IActionResult> CompanyAsync(Guid companyId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetCompanyAsync(companyId, asOf));
    }

    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroupAsync([FromBody] GroupProfile request)
    {
        return OkOrProblem(await knowledge.SaveGroupAsync(request));
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GroupsAsync([FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetGroupsAsync(asOf));
    }

    [HttpGet("groups/{groupId}")]
    public async Task<IActionResult> ExpandAsync(string groupId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.ExpandAsync(groupId, asOf));
    }

    [HttpPost("roles")]
    public async Task<IActionResult> SaveRoleAsync([FromBody] RoleProfile request)
    {
        return OkOrProblem(await knowledge.SaveRoleAsync(request));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> RolesAsync([FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetRolesAsync(asOf));
    }

    [HttpGet("role-scope")]
    public async Task<IActionResult> RoleAsync(
        string personId,
        Guid companyId,
        [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetRoleAsync(personId, companyId, asOf));
    }

    [HttpPost("role-scope")]
    public async Task<IActionResult> ScopeAsync([FromBody] RoleScopeRequest request)
    {
        return OkOrProblem(await selection.ScopeAsync(request));
    }

    [HttpPost("people")]
    public async Task<IActionResult> SavePersonAsync([FromBody] PersonProfile request)
    {
        return OkOrProblem(await knowledge.SavePersonAsync(request));
    }

    [HttpGet("people")]
    public async Task<IActionResult> PeopleAsync([FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetPeopleAsync(asOf));
    }

    [HttpGet("people/{personId}")]
    public async Task<IActionResult> PersonAsync(string personId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        return OkOrProblem(await knowledge.GetPersonAsync(personId, asOf));
    }

    [HttpGet("changes")]
    public async Task<IActionResult> ChangesAsync([FromQuery] long after = 0)
    {
        return OkOrProblem(await knowledge.GetChangesAsync(after));
    }

    [HttpPost("selection")]
    public async Task<IActionResult> SelectAsync([FromBody] CompanySelectionRequest request, CancellationToken ct)
    {
        return OkOrProblem(await selection.SelectAsync(request, ct));
    }

    [HttpPost("segments")]
    public async Task<IActionResult> SegmentsAsync([FromBody] SegmentRequest request)
    {
        return OkOrProblem(await selection.GetSegmentsAsync(request));
    }
}
