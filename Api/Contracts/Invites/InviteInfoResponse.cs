namespace Api.Contracts.Invites;

public class InviteInfoResponse
{
    public bool Valid { get; set; }
    public string? CompanyName { get; set; }
    public string? Reason { get; set; }
    public int? StatusCode { get; set; }
}
