namespace Api.Contracts.Knowledge;

public class RoleScope
{
    public RoleProfile[] Roles { get; set; } = [];
    public string[] MissingPersonIds { get; set; } = [];
    public Guid[] CompanyIds { get; set; } = [];
}
