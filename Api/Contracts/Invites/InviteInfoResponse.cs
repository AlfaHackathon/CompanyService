namespace Api.Contracts.Invites;

/// <summary>Публичная валидация кода перед регистрацией (что покажет фронт на инвайт-странице).</summary>
public record InviteInfoResponse(bool Valid, string? CompanyName, string? Reason);
