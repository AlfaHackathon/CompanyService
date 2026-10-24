namespace Api.Contracts.Knowledge;

public class CompanySelection
{
    public GroupExpansion[] Groups { get; set; } = [];
    public Guid[] SelectedCompanyIds { get; set; } = [];
    public Guid[] PendingCompanyIds { get; set; } = [];
}
