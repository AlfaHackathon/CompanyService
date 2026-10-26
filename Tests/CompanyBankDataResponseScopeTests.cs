using Api.Contracts.Companies;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Xunit;

public sealed class CompanyBankDataResponseScopeTests
{
    [Fact]
    public async Task GroupPreviewDoesNotGrantBankDataOfParentOrChildCompanies()
    {
        var actor = Guid.NewGuid();
        var parent = Company(actor);
        var current = Company(actor);
        var child = Company(actor);
        current.ParentCompanyId = parent.Id;
        var repository = new Companies(current, parent, child);
        var service = new Api.Services.CompanyService(repository, new Industries());
        var result = await service.GetMyGroupAsync(actor);
        Assert.Null(result.Error);
        Assert.Equal(current.BankData, result.Content!.Current.BankData);
        Assert.Null(result.Content.Parent!.BankData);
        Assert.Null(Assert.Single(result.Content.Children).BankData);
    }

    private static Company Company(Guid creator) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Synthetic operational company",
        CreatorId = creator,
        CreatedAt = DateTime.UtcNow,
        BankData = CompanyBankDataTests.AllFields()
    };
    private sealed class Industries : IIndustryRepository
    {
        public Task<List<Industry>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<Industry>());
        public Task<List<Industry>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default) => Task.FromResult(new List<Industry>());
    }
    private sealed class Companies(Company current, Company parent, Company child) : ICompanyRepository
    {
        public Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Company?>(current);
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Company?>(id == parent.Id ? parent : null);
        public Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.FromResult(new List<Company> { child });
        public Task<List<Industry>> GetIndustriesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new List<Industry>());
        public Task<Company?> TryCreateAsync(Company company, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetIndustriesAsync(Guid companyId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<CompanyMember>> GetMembersAsync(Guid companyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JoinCompanyOutcome> TryJoinAsync(Guid companyId, Guid userId, Func<Task<bool>> claim, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
