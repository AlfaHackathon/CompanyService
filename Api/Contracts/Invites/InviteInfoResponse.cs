namespace Api.Contracts.Invites;

/// <summary>Публичная валидация кода перед регистрацией (что покажет фронт на инвайт-странице).
/// StatusCode — какой код ответа консьюмеру отдавать, если инвайт невалиден (null — валиден).</summary>
public record InviteInfoResponse(bool Valid, string? CompanyName, string? Reason, int? StatusCode = null);
