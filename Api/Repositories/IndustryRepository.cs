using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Resources;

namespace Api.Repositories;

public class IndustryRepository(IDapperContext context) : IIndustryRepository
{
    public async Task<List<Industry>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.ListOrEmpty<Industry>(new QueryObject(IndustrySql.GetAll));
    }

    public async Task<List<Industry>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        return await context.ListOrEmpty<Industry>(new QueryObject(IndustrySql.GetByCodes, new { codes = codes.ToArray() }));
    }
}
