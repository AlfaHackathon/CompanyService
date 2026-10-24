namespace Api.Contracts.Knowledge;

public class GroupExpansion
{
    public string GroupId { get; set; } = string.Empty;
    public Guid[] CompanyIds { get; set; } = [];
    public GroupPath[] Paths { get; set; } = [];
    public string[] MissingGroups { get; set; } = [];
    public string[][] Cycles { get; set; } = [];
    public bool Complete { get; set; }
    public SourceReference[] SourceRefs { get; set; } = [];
    public string? AggregationRule { get; set; }
}
