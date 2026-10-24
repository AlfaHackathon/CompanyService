namespace Api.Contracts.Changes;

public class KnowledgeChange
{
    public long Sequence { get; set; }
    public string Category { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public DateTimeOffset KnownAt { get; set; }
    public Guid[] CompanyIds { get; set; } = [];
}
