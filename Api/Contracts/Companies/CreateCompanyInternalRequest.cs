namespace Api.Contracts.Companies;

/// <summary>internal: создаётся при регистрации юзера без инвайта.</summary>
public record CreateCompanyInternalRequest(
    Guid CreatorId,
    string Name,
    string? Inn,
    string? Region,
    decimal? Revenue,
    int? CountOfEmployees,
    List<string>? IndustryCodes);
