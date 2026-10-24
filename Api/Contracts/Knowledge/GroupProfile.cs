using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class GroupProfile
{
    public string GroupId { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset KnownAt { get; set; }
    [JsonRequired]
    public GroupMember[] Members { get; set; } = [];
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
    public string? AggregationRule { get; set; }
}
