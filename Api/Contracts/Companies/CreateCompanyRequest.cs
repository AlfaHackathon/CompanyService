namespace Api.Contracts.Companies;

/// <summary>Создание своей компании. У пользователя не должно быть другой компании.</summary>
public record CreateCompanyRequest(
    string Name,
    string? Inn,
    string? Region,
    decimal? Revenue,
    int? CountOfEmployees,
    List<string>? IndustryCodes);
