using AlfaHackaton.Result;
using Api.Contracts.Industries;

namespace Api.Services.Interfaces;

public interface IIndustryService
{
    Task<Result<List<IndustryResponse>>> GetAllAsync(CancellationToken ct = default);
}
