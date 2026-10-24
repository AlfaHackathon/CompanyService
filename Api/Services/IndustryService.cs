using AlfaHackaton.Result;
using Api.Contracts.Industries;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Services;

public class IndustryService(IIndustryRepository repository) : IIndustryService
{
    public async Task<Result<List<IndustryResponse>>> GetAllAsync(CancellationToken ct = default)
    {
        var industries = await repository.GetAllAsync(ct);
        return new(industries.Select(industry => new IndustryResponse { Id = industry.Id, Code = industry.Code, Name = industry.Name }).ToList(), null);
    }
}
