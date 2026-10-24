using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class RoleProfile
{
    public string PersonId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string RevisionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    [JsonRequired]
    public string[] Responsibilities { get; set; } = [];
    [JsonRequired]
    public Guid[] AllowedCompanyIds { get; set; } = [];
    [JsonRequired]
    public string[] AllowedMaterialIds { get; set; } = [];
    public DateTimeOffset KnownAt { get; set; }
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
}
