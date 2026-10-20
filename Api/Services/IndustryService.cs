using Api.Contracts;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Services;

/// <summary>Справочник отраслей (ОКВЭД).</summary>
public class IndustryService(IIndustryRepository repository) : IIndustryService
{
    public Task<List<Industry>> GetAllAsync(CancellationToken ct = default)
        => repository.GetAllAsync(ct);
}
