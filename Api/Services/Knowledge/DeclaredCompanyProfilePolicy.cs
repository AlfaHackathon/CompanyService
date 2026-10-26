using System.Globalization;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Context;
using Api.Contracts.Knowledge;

namespace Api.Services;

public static class DeclaredCompanyProfilePolicy
{
    public static bool Valid(CompanyProfile profile)
    {
        var declared = profile.DeclaredProfile;
        if (declared is null) return true;
        if (declared.Region?.Length > 128 || declared.CountOfEmployees < 0 || declared.SourceRefs is null || declared.SourceRefs.Length != 1 ||
            declared.IndustrySections is { } sections && (sections.Length > 21 || sections.Distinct(StringComparer.Ordinal).Count() != sections.Length ||
                sections.Any(s => s is null || s.Length != 1 || s[0] is < 'A' or > 'U'))) return false;
        if (declared.Revenue is { } revenue && (revenue.Currency is not null || revenue.Period is not null ||
            revenue.Amount is null || !decimal.TryParse(revenue.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount < 0)) return false;
        return declared.SourceRefs.All(r => r is not null && profile.SourceRefs.Any(s => s is not null &&
            s.MaterialId == r.MaterialId && s.VersionId == r.VersionId && s.ContentHash == r.ContentHash));
    }

    public static bool Matches(CompanyProfile profile, MaterialEvidence source)
    {
        var declared = profile.DeclaredProfile!;
        var reference = declared.SourceRefs[0];
        var expected = new DeclaredCompanySnapshot(profile.CompanyId, declared.Region, declared.IndustrySections, declared.Revenue, declared.CountOfEmployees);
        return source.MaterialId == reference.MaterialId && source.VersionId == reference.VersionId && source.ContentHash == reference.ContentHash &&
            source.SourceId == DeclaredCompanyCaptureService.SourceId && source.Kind == "company" && source.CompanyIds.SequenceEqual([profile.CompanyId]) &&
            source.KnownAt != default && source.KnownAt <= profile.KnownAt && source.Text == JsonSerializer.Serialize(expected, KnowledgeJson.Options);
    }
}
