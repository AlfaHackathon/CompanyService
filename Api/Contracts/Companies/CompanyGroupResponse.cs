namespace Api.Contracts.Companies;

public class CompanyGroupResponse
{
    public CompanyResponse Current { get; set; } = null!;
    public CompanyResponse? Parent { get; set; }
    public CompanyResponse[] Children { get; set; } = [];
}
