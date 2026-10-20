namespace Api.Contracts.Industries;

/// <summary>PUT /companies/me/industries — заменить список отраслей кодами ОКВЭД ('G', 'C').</summary>
public record SetIndustriesRequest(List<string> IndustryCodes);
