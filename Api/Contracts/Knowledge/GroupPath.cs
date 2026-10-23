namespace Api.Contracts.Knowledge;

public class GroupPath
{
    public Guid CompanyId { get; set; }
    public string[] GroupIds { get; set; } = [];
}
