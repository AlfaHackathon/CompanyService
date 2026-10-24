using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class GroupMember
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
}
