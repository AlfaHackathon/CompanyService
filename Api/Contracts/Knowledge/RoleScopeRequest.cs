using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class RoleScopeRequest
{
    public DateTimeOffset AsOf { get; set; }
    [JsonRequired]
    public string[] PersonIds { get; set; } = [];
    [JsonRequired]
    public Guid[] CompanyIds { get; set; } = [];
}
