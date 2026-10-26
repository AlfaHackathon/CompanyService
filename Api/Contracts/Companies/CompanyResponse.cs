using Api.Contracts.Industries;

namespace Api.Contracts.Companies;

public class CompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Inn { get; set; }
    public Guid CreatorId { get; set; }
    public Guid? ParentCompanyId { get; set; }
    public string? Region { get; set; }
    public decimal? Revenue { get; set; }
    public int? CountOfEmployees { get; set; }
    public string? BankProfileId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCreator { get; set; }
    public List<IndustryResponse> Industries { get; set; } = [];
    public CompanyBankData? BankData { get; set; }
}
