using System.Net;
using Api.Contracts.Knowledge;
using Api.Repositories;

namespace Api.Services.PublicKnowledge;

public interface IPublicCompanyProfiles
{
    Task<CompanyProfile> Get(Guid company, DateTimeOffset asOf);
    Task<CompanyProfile> Save(CompanyProfile profile);
}
public sealed class PublicCompanyProfiles(KnowledgeStore store) : IPublicCompanyProfiles
{
    public async Task<CompanyProfile> Get(Guid company, DateTimeOffset asOf)
    {
        var profile = await store.Get<CompanyProfile>("company", company.ToString("D"), asOf);
        return profile ?? throw new PublicKnowledgeError(HttpStatusCode.NotFound, "Company knowledge is not available.");
    }
    public async Task<CompanyProfile> Save(CompanyProfile profile)
    {
        // The public use case already resolved every source with its request deadline,
        // exact hash/time and current bank scope. Reuse the same immutable writer/change log.
        var result = await store.Save("company", profile.CompanyId.ToString("D"), profile.RevisionId, profile.KnownAt, profile);
        if (result.Error is { } error)
            throw new PublicKnowledgeError(error.HttpStatusCode ?? HttpStatusCode.ServiceUnavailable,
                "Company knowledge was not confirmed. Retry the same Idempotency-Key after checking the request.");
        return result.Content!;
    }
}
