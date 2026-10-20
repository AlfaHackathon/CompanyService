namespace Api.Models;

/// <summary>Участник компании.</summary>
public class CompanyMember
{
    public required Guid CompanyId { get; set; }
    public required Guid UserId { get; set; }
    public required DateTime JoinedAt { get; set; }
}
