namespace Api.Contracts.Context;

public class MaterialEvidence
{
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset KnownAt { get; set; }
    public Guid[] CompanyIds { get; set; } = [];
}
