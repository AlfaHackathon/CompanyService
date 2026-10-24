namespace Api.Contracts.Knowledge;

public class SourceReference
{
    public string MaterialId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}
