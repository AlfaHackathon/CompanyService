namespace Api.Contracts.Companies;

public class CreateCompanyInternalRequest
{
    public Guid CreatorId { get; set; }
    public required string Name { get; set; }
    public string? Inn { get; set; }
    public string? Region { get; set; }
    public decimal? Revenue { get; set; }
    public int? CountOfEmployees { get; set; }
    public List<string>? IndustryCodes { get; set; }
}
