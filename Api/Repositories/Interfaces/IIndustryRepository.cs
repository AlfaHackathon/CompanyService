using Api.Contracts;
using Api.Models;

namespace Api.Repositories.Interfaces;

public interface IIndustryRepository
{
    Task<List<Industry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Отрасли по кодам ОКВЭД ('G', 'C'); неизвестные коды игнорируются.</summary>
    Task<List<Industry>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default);
}
