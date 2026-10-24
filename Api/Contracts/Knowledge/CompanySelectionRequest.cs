using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class CompanySelectionRequest
{
    public DateTimeOffset AsOf { get; set; }
    [JsonRequired]
    public Guid[] TargetCompanyIds { get; set; } = [];
    [JsonRequired]
    public string[] GroupIds { get; set; } = [];
    [JsonRequired]
    public string[] PersonIds { get; set; } = [];
    [JsonRequired]
    public string[] Queries { get; set; } = [];
    [JsonRequired]
    public Guid[] PreviousCompanyIds { get; set; } = [];
    public int CompanyBudget { get; set; } = 12;
    public bool ProjectionOnly { get; set; } = false;
}
