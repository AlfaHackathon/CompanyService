using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class CompanyProfile
{
    public Guid CompanyId { get; set; }
    public string RevisionId { get; set; } = string.Empty;
    public string? BankProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [JsonRequired]
    public string[] IndustryCodes { get; set; } = [];
    [JsonRequired]
    public string[] Exposures { get; set; } = [];
    public DateTimeOffset KnownAt { get; set; }
    [JsonRequired]
    public SourceReference[] SourceRefs { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeclaredCompanyProfile? DeclaredProfile { get; set; }
}
