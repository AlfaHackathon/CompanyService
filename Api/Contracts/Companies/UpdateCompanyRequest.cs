namespace Api.Contracts.Companies;

/// <summary>PATCH /companies/me — частичное обновление (null = не трогаем).</summary>
public record UpdateCompanyRequest(
    string? Name,
    string? Inn,
    string? Region,
    decimal? Revenue,
    int? CountOfEmployees,
    string? BankProfileId);
