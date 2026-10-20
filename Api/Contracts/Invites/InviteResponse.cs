namespace Api.Contracts.Invites;

public record InviteResponse(
    Guid Id,
    string Code,
    Guid CompanyId,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    Guid? UsedBy,
    DateTime? UsedAt,
    string Status);
