namespace Api.Contracts.Companies;

public class CreatedCompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
