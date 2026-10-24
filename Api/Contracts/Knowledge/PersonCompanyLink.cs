using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class PersonCompanyLink
{
    public Guid CompanyId { get; set; }
    public string RelationKind { get; set; } = string.Empty;
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
}
