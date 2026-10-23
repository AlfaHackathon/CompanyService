namespace Api.Contracts.Invites;

public class InviteResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid? UsedBy { get; set; }
    public DateTime? UsedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
