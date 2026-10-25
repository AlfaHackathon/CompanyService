namespace Api.Contracts.Companies;

public class CompanyMembershipResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Создатель компании (нужен user-service для проверок «роли меняет только создатель»).</summary>
    public Guid CreatorId { get; set; }
    public string? BankProfileId { get; set; }
}
