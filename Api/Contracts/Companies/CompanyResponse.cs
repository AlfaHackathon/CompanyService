using Api.Contracts.Industries;

namespace Api.Contracts.Companies;

public record CompanyResponse(
    Guid Id,
    string Name,
    string? Inn,
    Guid CreatorId,
    Guid? ParentCompanyId,
    string? Region,
    decimal? Revenue,
    int? CountOfEmployees,
    string? BankProfileId,
    DateTime CreatedAt,
    bool IsCreator,
    List<IndustryResponse> Industries);
