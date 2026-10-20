namespace Api.Contracts.Invites;

/// <summary>internal: отметить инвайт использованным и добавить юзера в компанию.</summary>
public record UseInviteInternalRequest(Guid UserId);
