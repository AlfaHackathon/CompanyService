namespace Api.Contracts.Companies;

public record MemberResponse(Guid UserId, DateTime JoinedAt, bool IsCreator);
