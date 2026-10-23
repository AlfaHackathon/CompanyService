
namespace Api.Contracts.Knowledge;

public sealed record PersonProfile(string PersonId, string RevisionId, DateTimeOffset KnownAt,
    PersonCompanyLink[] CompanyLinks, SourceReference[] SourceRefs);
