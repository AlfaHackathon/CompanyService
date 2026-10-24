namespace Api.Contracts.Companies;

public class CompanyMembershipResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BankProfileId { get; set; }
}
