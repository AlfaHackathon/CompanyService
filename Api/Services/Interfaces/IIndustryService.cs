using Api.Models;

namespace Api.Services.Interfaces;

/// <summary>Справочник отраслей (ОКВЭД).</summary>
public interface IIndustryService
{
    Task<List<Industry>> GetAllAsync(CancellationToken ct = default);
}
