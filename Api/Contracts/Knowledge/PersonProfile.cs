using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class PersonProfile
{
    public string PersonId { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public DateTimeOffset KnownAt { get; set; }
    [JsonRequired]
    public PersonCompanyLink[] CompanyLinks { get; set; } = [];
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
}
