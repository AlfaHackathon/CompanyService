namespace Api.Contracts.Companies;

public class MemberResponse
{
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsCreator { get; set; }
}
