namespace Api.Contracts.Context;

public class MaterialEvidence
{
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset KnownAt { get; set; }
    public Guid[] CompanyIds { get; set; } = [];
    public string? MaterialId { get; set; }
    public string? VersionId { get; set; }
    public string? SourceId { get; set; }
    public string? Kind { get; set; }
    public string? Text { get; set; }
}
