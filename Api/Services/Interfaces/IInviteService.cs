using Api.Contracts.Companies;
using Api.Contracts.Invites;
using Api.Models;

namespace Api.Services.Interfaces;

/// <summary>Бизнес-операции над инвайтами. Контроллеры работают только с сервисом.</summary>
public interface IInviteService
{
    /// <summary>Создать инвайт (ttl указывать в днях; null/некорректное — дефолт 7 дней).</summary>
    Task<InviteResponse> CreateAsync(Guid companyId, Guid userId, int? expiresInDays, CancellationToken ct = default);

    /// <summary>Инвайты компании со статусами.</summary>
    Task<List<InviteResponse>> ListByCompanyAsync(Guid companyId);

    /// <summary>Инвайт по коду, null — не найден.</summary>
    Task<InviteDocument?> GetByCodeAsync(string code);

    /// <summary>Публичная валидация кода (для страницы инвайта).</summary>
    Task<InviteInfoResponse> ValidateAsync(string code, CancellationToken ct = default);

    /// <summary>internal: пометить использованным и добавить юзера в компанию (UseInviteResult.Error = null — успех).</summary>
    Task<UseInviteResult> UseInternalAsync(string code, Guid userId, CancellationToken ct = default);

    Task InsertExpiredForTestAsync(string code, Guid companyId);
}
