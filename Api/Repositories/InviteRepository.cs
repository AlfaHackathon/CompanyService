using AlfaHackaton.MongoContext;
using Api.Models;
using Api.Repositories.Interfaces;
using MongoDB.Driver;

namespace Api.Repositories;

public class InviteRepository(IMongoContext mongo) : IInviteRepository
{
    private const string CollectionName = "invites";
    private IMongoCollection<InviteDocument> Collection => mongo.GetCollection<InviteDocument>(CollectionName);

    public async Task<InviteDocument> CreateAsync(Guid companyId, Guid createdBy, string code, TimeSpan ttl)
    {
        var invite = new InviteDocument
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = code,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
        await Collection.InsertOneAsync(invite);
        return invite;
    }

    public async Task<List<InviteDocument>> ListByCompanyAsync(Guid companyId) =>
        await Collection.Find(i => i.CompanyId == companyId)
            .SortByDescending(i => i.CreatedAt)
            .ToListAsync();

    public async Task<InviteDocument?> GetByCodeAsync(string code) =>
        await Collection.Find(i => i.Code == code).FirstOrDefaultAsync();

    public async Task<InviteDocument?> TryUseAsync(string code, Guid userId)
    {
        var filter = Builders<InviteDocument>.Filter.And(
            Builders<InviteDocument>.Filter.Eq(i => i.Code, code),
            Builders<InviteDocument>.Filter.Eq(i => i.UsedAt, null),
            Builders<InviteDocument>.Filter.Gt(i => i.ExpiresAt, DateTime.UtcNow));
        var update = Builders<InviteDocument>.Update
            .Set(i => i.UsedBy, userId)
            .Set(i => i.UsedAt, DateTime.UtcNow);
        return await Collection.FindOneAndUpdateAsync(filter, update);
    }

    public async Task DeleteAsync(string code)
    {
        await Collection.DeleteOneAsync(i => i.Code == code);
    }

    public async Task InsertExpiredForTestAsync(string code, Guid companyId) =>
        await Collection.InsertOneAsync(new InviteDocument
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = code,
            CreatedBy = Guid.Empty,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1) // TTL-индекс удалит такой документ ~в течение минуты
        });
}
