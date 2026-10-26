using Api.Contracts.Companies;

namespace Api.Models;

/// <summary>
/// Компания. Организационная сущность (кто создал, кто состоит), а не клиентский
/// профиль банка: ОКВЭД-контекст, контрагенты и пр. живут на стороне bank-данных
/// (graph-service) и подвязываются через BankProfileId (SYNTHETIC_U из данных кейса).
/// </summary>
public class Company
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Inn { get; set; }
    public required Guid CreatorId { get; set; }

    /// <summary>Группа компаний: дочка ссылается на холдинг. null — компания без родителя.</summary>
    public Guid? ParentCompanyId { get; set; }

    public string? Region { get; set; }
    public decimal? Revenue { get; set; }
    public int? CountOfEmployees { get; set; }

    /// <summary>SYNTHETIC_U — мост к профилю из данных кейса (graph-service).</summary>
    public string? BankProfileId { get; set; }

    public required DateTime CreatedAt { get; set; }
    // Read projection from the canonical profile owned by this Company UUID; never an auth grant.
    public CompanyBankData? BankData { get; set; }
}
