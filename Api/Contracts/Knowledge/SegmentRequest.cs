using System.Text.Json.Serialization;

namespace Api.Contracts.Knowledge;

public class SegmentRequest
{
    public DateTimeOffset AsOf { get; set; }
    [JsonRequired]
    public Guid[] CompanyIds { get; set; } = [];
    public string Granularity { get; set; } = "activity";
}
