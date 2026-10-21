using Api.Models;

namespace Api.Repositories.Interfaces;

public interface IInviteRepository
{
    Task<InviteDocument> CreateAsync(Guid companyId, Guid createdBy, string code, TimeSpan ttl);
    Task<List<InviteDocument>> ListByCompanyAsync(Guid companyId);
    Task<InviteDocument?> GetByCodeAsync(string code);

    /// <summary>Атомарно пометить использованным (если ещё не использован и не протух).</summary>
    Task<InviteDocument?> TryUseAsync(string code, Guid userId);

    /// <summary>Удалить инвайт по коду (после успешного использования).</summary>
    Task DeleteAsync(string code);

    Task InsertExpiredForTestAsync(string code, Guid companyId); // проверка TTL в смоуке
}
