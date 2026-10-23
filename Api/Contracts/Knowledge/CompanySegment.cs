namespace Api.Contracts.Knowledge;

public class CompanySegment
{
    public string Key { get; set; } = string.Empty;
    public string[] Labels { get; set; } = [];
    public Guid[] CompanyIds { get; set; } = [];
    public SourceReference[] ProfileRefs { get; set; } = [];
}
